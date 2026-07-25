<script setup>
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { combine } from '@atlaskit/pragmatic-drag-and-drop/combine'
import { draggable, dropTargetForElements, monitorForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import { attachClosestEdge, extractClosestEdge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/closest-edge'
import { ContextMenuRoot, ContextMenuTrigger, ContextMenuPortal, ContextMenuContent, ContextMenuItem } from 'reka-ui'
import { MENU_ITEM_CLASS as menuItemClass } from '../../utils/menu.js'

const props = defineProps({
  board: { type: Object, required: true },
  selected: { type: Boolean, default: false },
  renaming: { type: Boolean, default: false },
})

const emit = defineEmits(['select', 'rename', 'rename-done', 'request-delete', 'request-rename', 'drop'])

const row = ref(null)
const editInput = ref(null)
const dropEdge = ref(null)
const dragging = ref(false)
let cleanup = null

watch(() => props.renaming, async (value) => {
  if (value) {
    await nextTick()
    editInput.value?.focus()
    editInput.value?.select()
  }
})

function commitRename(e) {
  if (props.renaming) {
    const value = e.target.value.trim()
    if (value && value !== props.board.name) {
      emit('rename', value)
    }
    emit('rename-done')
  }
}

function cancelRename() {
  emit('rename-done')
}

// Same reka as-child hazard as TaskCard: the cloned vnode's ref oscillates on
// re-render, so the dnd ref lives on a plain inner div and attach follows it.
function attachDnd(element) {
  return combine(
    draggable({
      element,
      getInitialData: () => ({ kind: 'board', boardId: props.board.id }),
      onDragStart: () => (dragging.value = true),
      onDrop: () => (dragging.value = false),
      onGenerateDragPreview: ({ nativeSetDragImage }) => {
        setCustomNativeDragPreview({
          getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
          render: ({ container }) => {
            const preview = document.createElement('div')
            preview.textContent = props.board.name || 'Untitled board'
            preview.style.cssText = 'padding: 4px 10px; border-radius: 4px; font-size: 13px; background: var(--color-surface-alt); color: var(--color-on-surface); border: 1px solid var(--color-border); white-space: nowrap;'
            container.appendChild(preview)
          },
          nativeSetDragImage,
        })
      },
    }),
    dropTargetForElements({
      element,
      canDrop: ({ source }) => source.data.kind === 'board' && source.data.boardId !== props.board.id,
      getData: ({ input, element }) =>
        attachClosestEdge({ boardId: props.board.id }, { input, element, allowedEdges: ['top', 'bottom'] }),
      onDrag: ({ self }) => (dropEdge.value = extractClosestEdge(self.data)),
      onDragLeave: () => (dropEdge.value = null),
      onDrop: ({ self, source }) => {
        emit('drop', { sourceId: source.data.boardId, targetId: props.board.id, edge: extractClosestEdge(self.data) })
        dropEdge.value = null
      },
    }),
    monitorForElements({
      onDrop: () => {
        dropEdge.value = null
        dragging.value = false
      },
    }),
  )
}

watch(row, (element) => {
  if (cleanup) {
    cleanup()
    cleanup = null
  }
  if (element) {
    cleanup = attachDnd(element)
  }
})

onMounted(() => {
  if (!cleanup && row.value) {
    cleanup = attachDnd(row.value)
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
      <div ref="row" class="relative">
        <div v-if="dropEdge === 'top'" class="absolute -top-px left-2 right-2 h-0.5 rounded bg-accent z-10" />
        <div v-if="dropEdge === 'bottom'" class="absolute -bottom-px left-2 right-2 h-0.5 rounded bg-accent z-10" />
        <button
          class="w-full flex items-center gap-2 rounded-md px-2.5 h-8 text-sm text-left"
          :class="[
            selected ? 'bg-accent-soft text-on-surface' : 'text-on-surface-muted hover:bg-surface-alt hover:text-on-surface',
            dragging ? 'opacity-40' : '',
          ]"
          @click="emit('select')" @dblclick="emit('request-rename')">
          <i-lucide-square-kanban class="size-4 shrink-0" />
          <input v-if="renaming" ref="editInput" :value="board.name" placeholder="Untitled board"
            class="flex-1 min-w-0 bg-transparent text-sm border-b border-accent outline-none text-on-surface"
            @blur="commitRename" @keydown.enter="$event.target.blur()" @keydown.esc.stop="cancelRename"
            @click.stop @dblclick.stop />
          <span v-else class="truncate border-b border-transparent">{{ board.name || 'Untitled board' }}</span>
        </button>
      </div>
      </div>
    </ContextMenuTrigger>
    <ContextMenuPortal>
      <ContextMenuContent class="bg-surface-elevated border border-border rounded-lg shadow-lg p-1 min-w-40 z-50">
        <ContextMenuItem :class="menuItemClass" @select="emit('request-rename')">
          <i-lucide-pencil class="size-4 text-on-surface-muted" />
          Rename
        </ContextMenuItem>
        <ContextMenuItem :class="menuItemClass" @select="emit('request-delete')">
          <i-lucide-trash-2 class="size-4 text-red-500" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenuPortal>
  </ContextMenuRoot>
</template>
