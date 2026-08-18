<script setup>
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { combine } from '@atlaskit/pragmatic-drag-and-drop/combine'
import { draggable, dropTargetForElements, monitorForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import { attachClosestEdge, extractClosestEdge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/closest-edge'
import TaskCard from './TaskCard.vue'
import Button from '../../components/Button.vue'
import HoverTip from '../../components/HoverTip.vue'
import TruncatedText from '../../components/TruncatedText.vue'

const props = defineProps({
  column: { type: Object, required: true },
  tasks: { type: Array, default: () => [] },
  renaming: { type: Boolean, default: false },
})

const emit = defineEmits([
  'rename', 'rename-done', 'request-rename', 'request-delete',
  'add-task', 'open-task', 'request-delete-task', 'task-drop', 'column-drop',
])

const lane = ref(null)
const header = ref(null)
const cardsArea = ref(null)
const editInput = ref(null)
const dropEdge = ref(null)
const areaOver = ref(false)
const dragging = ref(false)
let cleanup = null

// The input binds to this draft, not column.name (same fix as the notes tree):
// a boards:changed refresh re-renders mid-edit, and :value bound to the store
// name would reset the input to it, wiping whatever was typed.
const renameDraft = ref('')

watch(() => props.renaming, async (value) => {
  if (value) {
    renameDraft.value = props.column.name
    await nextTick()
    editInput.value?.focus()
    editInput.value?.select()
  }
})

function commitRename(e) {
  if (props.renaming) {
    const value = e.target.value.trim()
    if (value && value !== props.column.name) {
      emit('rename', value)
    }
    emit('rename-done')
  }
}

onMounted(() => {
  cleanup = combine(
    // Only the header drags the list - a whole-lane draggable would swallow any
    // drag started on a card or the empty cards area.
    draggable({
      element: header.value,
      getInitialData: () => ({ kind: 'column', columnId: props.column.id }),
      onDragStart: () => (dragging.value = true),
      onDrop: () => (dragging.value = false),
      onGenerateDragPreview: ({ nativeSetDragImage }) => {
        setCustomNativeDragPreview({
          getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
          render: ({ container }) => {
            const preview = document.createElement('div')
            preview.textContent = props.column.name || 'Untitled list'
            preview.style.cssText = 'padding: 4px 10px; border-radius: 4px; font-size: 13px; background: var(--color-surface-alt); color: var(--color-on-surface); border: 1px solid var(--color-border); white-space: nowrap;'
            container.appendChild(preview)
          },
          nativeSetDragImage,
        })
      },
    }),
    dropTargetForElements({
      element: lane.value,
      canDrop: ({ source }) => source.data.kind === 'column' && source.data.columnId !== props.column.id,
      getData: ({ input, element }) =>
        attachClosestEdge({ columnId: props.column.id }, { input, element, allowedEdges: ['left', 'right'] }),
      onDrag: ({ self }) => (dropEdge.value = extractClosestEdge(self.data)),
      onDragLeave: () => (dropEdge.value = null),
      onDrop: ({ self, source }) => {
        emit('column-drop', { columnId: source.data.columnId, targetColumnId: props.column.id, edge: extractClosestEdge(self.data) })
        dropEdge.value = null
      },
    }),
    // The open cards area (below the last card) appends to the end of this list;
    // per-card edge drops take priority because the card is the inner target.
    dropTargetForElements({
      element: cardsArea.value,
      canDrop: ({ source }) => source.data.kind === 'task',
      onDragStart: () => (areaOver.value = true),
      onDragEnter: () => (areaOver.value = true),
      onDragLeave: () => (areaOver.value = false),
      onDrop: ({ source, location }) => {
        areaOver.value = false
        if (location.current.dropTargets[0]?.element === cardsArea.value) {
          emit('task-drop', { taskId: source.data.taskId, targetTaskId: null, edge: null, columnId: props.column.id })
        }
      },
    }),
    monitorForElements({
      onDrop: () => {
        dropEdge.value = null
        areaOver.value = false
        dragging.value = false
      },
    }),
  )
})

onUnmounted(() => {
  if (cleanup) cleanup()
})
</script>

<template>
  <div ref="lane" class="relative w-64 shrink-0 flex flex-col min-h-0 max-h-full">
    <div v-if="dropEdge === 'left'" class="absolute -left-1.5 top-0 bottom-0 w-0.5 rounded bg-accent z-10" />
    <div v-if="dropEdge === 'right'" class="absolute -right-1.5 top-0 bottom-0 w-0.5 rounded bg-accent z-10" />

    <div class="flex flex-col min-h-0 rounded-lg border border-border bg-surface" :class="dragging ? 'opacity-40' : ''">
      <div ref="header" class="group flex items-center gap-1 px-2.5 h-9 shrink-0 cursor-grab">
        <input v-if="renaming" ref="editInput" :value="renameDraft" placeholder="Untitled list"
          class="flex-1 min-w-0 bg-transparent text-sm font-medium border-b border-accent outline-none"
          @input="renameDraft = $event.target.value" @blur="commitRename"
          @keydown.enter="$event.target.blur()" @keydown.esc.stop="emit('rename-done')" @click.stop
          @dblclick.stop />
        <template v-else>
          <TruncatedText :text="column.name || 'Untitled list'"
            class="flex-1 min-w-0 text-sm font-medium border-b border-transparent"
            @dblclick="emit('request-rename')" />
          <!-- hidden rather than opacity-0, as in the notes tree: a list is 256px
               wide and the title should have all of it until there is a reason not
               to. -->
          <span class="shrink-0 hidden group-hover:flex items-center gap-1" @click.stop @dblclick.stop>
            <HoverTip text="Rename list" side="bottom">
              <Button variant="ghost" size="icon" @click="emit('request-rename')">
                <i-lucide-pencil class="size-4" />
              </Button>
            </HoverTip>
            <HoverTip text="Delete list" side="bottom">
              <Button variant="ghost-danger" size="icon" @click="emit('request-delete')">
                <i-lucide-trash-2 class="size-4" />
              </Button>
            </HoverTip>
          </span>
        </template>
      </div>

      <!-- pt-1 leaves room for the first card's above-indicator, which renders
           4px above the card and would otherwise be clipped by the scroll area. -->
      <div ref="cardsArea" class="flex-1 min-h-6 overflow-y-auto px-2 pt-1 pb-1 flex flex-col gap-1.5"
        :class="areaOver && tasks.length === 0 ? 'bg-drop-target' : ''">
        <TaskCard v-for="task in tasks" :key="task.id" :task="task" @open="emit('open-task', task)"
          @request-delete="emit('request-delete-task', task)"
          @drop="(payload) => emit('task-drop', { ...payload, columnId: column.id })" />
      </div>

      <button
        class="flex items-center gap-1.5 mx-2 mb-2 px-1.5 h-7 shrink-0 rounded text-sm text-on-surface-muted hover:text-on-surface hover:bg-hover-wash"
        @click="emit('add-task')">
        <i-lucide-plus class="size-4" />
        Add task
      </button>
    </div>
  </div>
</template>
