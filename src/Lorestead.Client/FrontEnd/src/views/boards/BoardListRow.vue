<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
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

const contextOpen = ref(false)

const emit = defineEmits(['select', 'rename', 'rename-done', 'request-delete', 'request-rename', 'drop'])

const row = ref(null)
const rowButton = ref(null)
const dropEdge = ref(null)
const dragging = ref(false)
let cleanup = null

let renameStartedAt = 0

// The input binds to this draft, not board.name (same fix as the notes tree):
// a boards:changed load() re-renders mid-edit, and :value bound to the store
// name would reset the input to it, wiping whatever was typed.
const renameDraft = ref('')

watch(() => props.renaming, (value) => {
  if (value) renameDraft.value = props.board.name
}, { immediate: true })

// The as-child clone hazard (below) bites template refs too: a named ref on the
// input oscillates on re-render, so a watch awaiting nextTick can find it null
// and focus nothing. A function ref runs when the element actually attaches -
// the same mechanism the notes tree's rename input uses.
function focusEditInput(el) {
  if (el && document.activeElement !== el) {
    renameStartedAt = Date.now()
    el.focus()
    el.select()
  }
}

function commitRename(e) {
  if (props.renaming) {
    // Same hazard as the notes Tree: the context menu restores focus to its
    // trigger after closing, blurring the input right as the rename starts -
    // committing here would end the rename before it began. Take focus back.
    if (Date.now() - renameStartedAt < 200) {
      e.target.focus()
      e.target.select()
      return
    }
    const value = e.target.value.trim()
    if (value && value !== props.board.name) {
      emit('rename', value)
    }
    emit('rename-done')
    // Same trap as the notes Tree: Enter blurs the input with nowhere for focus
    // to go, so it lands on body and the panel's F2 handler hears nothing until
    // a row is clicked. A blur with a relatedTarget is the other kind - the user
    // clicked something - and taking focus back would fight the click.
    if (!e.relatedTarget) {
      rowButton.value?.focus()
    }
  }
}

function cancelRename() {
  emit('rename-done')
  rowButton.value?.focus()
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
  <ContextMenuRoot @update:open="contextOpen = $event">
    <ContextMenuTrigger as-child>
      <div>
        <div ref="row" class="relative">
          <div v-if="dropEdge === 'top'" class="absolute -top-px left-0 right-0 h-0.5 rounded bg-accent z-10" />
          <div v-if="dropEdge === 'bottom'" class="absolute -bottom-px left-0 right-0 h-0.5 rounded bg-accent z-10" />
          <!-- Same shape and states as a notes tree row (TreeNode): square, full
             width of the panel, its own px-3; full-strength label, accent-soft when
             selected, a neutral tint while its context menu is open, the shared
             on-surface wash on hover. py-1.5 rather than a fixed height, which is what the tree does:
             the two rows then derive the same height from the same text instead of
             agreeing on a number that only holds at today's type scale. -->
          <button ref="rowButton" class="w-full flex items-center gap-2 px-3 py-1.5 text-sm text-left transition-colors"
            :class="[
              contextOpen ? 'bg-on-surface/5' : selected ? 'bg-accent-soft' : 'hover:bg-hover-wash',
              dragging ? 'opacity-40' : '',
            ]" @click="emit('select')" @dblclick="emit('request-rename')">
            <!-- <i-lucide-square-kanban class="size-4 shrink-0 text-on-surface-muted" /> -->
            <input v-if="renaming" :ref="focusEditInput" :value="renameDraft" placeholder="Untitled board"
              class="flex-1 min-w-0 bg-transparent text-sm border-b border-accent outline-none text-on-surface"
              @input="renameDraft = $event.target.value" @blur="commitRename"
              @keydown.enter="$event.target.blur()" @keydown.esc.stop="cancelRename" @click.stop @dblclick.stop />
            <span v-else class="truncate border-b border-transparent">{{ board.name || 'Untitled board' }}</span>
          </button>
        </div>
      </div>
    </ContextMenuTrigger>
    <ContextMenuPortal>
      <!-- Same as TreeNode: without this, reka hands focus back to the trigger
           after the menu closes, stealing it from the rename input. -->
      <ContextMenuContent class="bg-surface-elevated border border-border rounded-lg shadow-lg p-1 min-w-40 z-50"
        @closeAutoFocus.prevent>
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
