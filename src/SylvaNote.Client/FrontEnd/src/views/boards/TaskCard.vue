<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { combine } from '@atlaskit/pragmatic-drag-and-drop/combine'
import { draggable, dropTargetForElements, monitorForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import { attachClosestEdge, extractClosestEdge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/closest-edge'
import { ContextMenuRoot, ContextMenuTrigger, ContextMenuPortal, ContextMenuContent, ContextMenuItem } from 'reka-ui'
import MarkdownPreview from '../../components/MarkdownPreview.vue'
import { useSettingsStore } from '../../stores/settingsStore.js'
import { formatTimestamp } from '../../utils/dateFormat.js'

const props = defineProps({
  task: { type: Object, required: true },
})

const emit = defineEmits(['open', 'request-delete', 'drop'])

const settingsStore = useSettingsStore()
const card = ref(null)
const dropEdge = ref(null)
const dragging = ref(false)
let cleanup = null

const menuItemClass = 'flex items-center gap-2 px-2.5 py-1.5 text-sm rounded-md cursor-default select-none outline-none data-highlighted:bg-surface-alt'

// A trimmed slice of the body is enough for the card - the clamp below hides
// anything past a few lines anyway, and short input keeps markdown-it cheap.
const snippet = computed(() => {
  const body = (props.task.body || '').trim()
  return body.length > 400 ? body.slice(0, 400) : body
})

const dateLabel = computed(() => {
  const app = settingsStore.application
  return formatTimestamp(props.task.updatedAt || props.task.createdAt, app.dateFormat, app.timeFormat)
})

// The dnd element must NOT be the node reka's as-child trigger clones: reka
// re-merges that vnode's ref every render, so the template ref oscillates
// null/element and would tear down drag wiring mid-drag. The ref lives on a
// plain inner div instead, and attach follows the ref for safety.
function attachDnd(element) {
  return combine(
    draggable({
      element,
      getInitialData: () => ({ kind: 'task', taskId: props.task.id, columnId: props.task.columnId }),
      onDragStart: () => (dragging.value = true),
      onDrop: () => (dragging.value = false),
      onGenerateDragPreview: ({ nativeSetDragImage }) => {
        setCustomNativeDragPreview({
          getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
          render: ({ container }) => {
            const preview = document.createElement('div')
            preview.textContent = props.task.title || 'Untitled task'
            preview.style.cssText = 'padding: 4px 10px; border-radius: 4px; font-size: 13px; background: var(--color-surface-alt); color: var(--color-on-surface); border: 1px solid var(--color-border); white-space: nowrap; max-width: 240px; overflow: hidden; text-overflow: ellipsis;'
            container.appendChild(preview)
          },
          nativeSetDragImage,
        })
      },
    }),
    // Self stays a valid target so dropping a card back onto itself is swallowed
    // as a no-op - otherwise the drop falls through to the column's open area
    // and a cancelled drag appends the card to the end of its own list.
    dropTargetForElements({
      element,
      canDrop: ({ source }) => source.data.kind === 'task',
      getData: ({ input, element }) =>
        attachClosestEdge({ taskId: props.task.id }, { input, element, allowedEdges: ['top', 'bottom'] }),
      onDrag: ({ self, source }) => {
        dropEdge.value = source.data.taskId !== props.task.id ? extractClosestEdge(self.data) : null
      },
      onDragLeave: () => (dropEdge.value = null),
      onDrop: ({ self, source }) => {
        if (source.data.taskId !== props.task.id) {
          emit('drop', { taskId: source.data.taskId, targetTaskId: props.task.id, edge: extractClosestEdge(self.data) })
        }
        dropEdge.value = null
      },
    }),
    // Every drag ending (drop elsewhere, cancel) clears local indicator state so
    // a missed onDragLeave can never leave a stuck line or a dimmed card.
    monitorForElements({
      onDrop: () => {
        dropEdge.value = null
        dragging.value = false
      },
    }),
  )
}

watch(card, (element) => {
  if (cleanup) {
    cleanup()
    cleanup = null
  }
  if (element) {
    cleanup = attachDnd(element)
  }
})

onMounted(() => {
  if (!cleanup && card.value) {
    cleanup = attachDnd(card.value)
  }
})

onUnmounted(() => {
  if (cleanup) cleanup()
})
</script>

<template>
  <ContextMenuRoot>
    <ContextMenuTrigger as-child>
      <div>
      <div ref="card" class="relative">
        <div v-if="dropEdge === 'top'" class="absolute -top-1 left-1 right-1 h-0.5 rounded bg-accent z-10" />
        <div v-if="dropEdge === 'bottom'" class="absolute -bottom-1 left-1 right-1 h-0.5 rounded bg-accent z-10" />
        <div
          class="group rounded-md border border-border bg-surface-elevated px-2.5 py-2 cursor-pointer hover:border-accent/50 flex flex-col gap-1"
          :class="dragging ? 'opacity-40' : ''" @click="emit('open')">
          <div class="flex items-start gap-1.5">
            <div class="flex-1 min-w-0 text-sm">{{ task.title || 'Untitled task' }}</div>
            <button
              class="opacity-0 group-hover:opacity-100 shrink-0 mt-0.5 text-on-surface-muted hover:text-red-500 transition-opacity"
              title="Delete task" @click.stop="emit('request-delete')">
              <i-lucide-trash-2 class="size-3.5" />
            </button>
          </div>
          <div v-if="snippet"
            class="max-h-16 overflow-hidden text-xs text-on-surface-muted pointer-events-none [&_.markdown-preview]:text-xs">
            <MarkdownPreview :markdown="snippet" />
          </div>
          <div class="flex items-center gap-1 text-xs text-on-surface-muted/80">
            <span v-if="task.attachmentCount > 0" class="flex items-center gap-0.5">
              <i-lucide-paperclip class="size-3" />
              {{ task.attachmentCount }}
            </span>
            <span class="ml-auto">{{ dateLabel }}</span>
          </div>
        </div>
      </div>
      </div>
    </ContextMenuTrigger>
    <ContextMenuPortal>
      <ContextMenuContent class="bg-surface-elevated border border-border rounded-lg shadow-lg p-1 min-w-40 z-50">
        <ContextMenuItem :class="menuItemClass" @select="emit('open')">
          <i-lucide-pencil class="size-4 text-on-surface-muted" />
          Edit
        </ContextMenuItem>
        <ContextMenuItem :class="menuItemClass" @select="emit('request-delete')">
          <i-lucide-trash-2 class="size-4 text-red-500" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenuPortal>
  </ContextMenuRoot>
</template>
