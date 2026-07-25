<script setup>
import { ref, computed, watch, provide, onMounted, onUnmounted, nextTick, toRef } from 'vue'
import { draggable as makeDraggable, dropTargetForElements, monitorForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { combine } from '@atlaskit/pragmatic-drag-and-drop/combine'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import TreeNode from './TreeNode.vue'

const props = defineProps({
  items: { type: Array, default: () => [] },
  childrenKey: { type: String, default: 'children' },
  selectedId: { type: [String, Number, null], default: null },
  defaultExpanded: { type: Array, default: () => [] },
  // canDrag(item) → bool. resolveDrop(sourceItem, targetItem, zone) → false | true |
  // { targetId, zone } - zones are 'above' | 'below' | 'into'; returning a different
  // targetId redirects the drop (e.g. trash children redirect to the Trash node).
  canDrag: { type: Function, default: null },
  resolveDrop: { type: Function, default: null },
  canRename: { type: Function, default: null },
  contextMenuFor: { type: Function, default: null },
  // Controlled expansion: when provided, the parent owns the Set (v-model:expanded-ids)
  // and it survives this component unmounting; otherwise expansion is internal.
  expandedIds: { type: Set, default: null },
})

const emit = defineEmits(['select', 'toggle', 'rename', 'drop', 'update:expandedIds'])

const internalExpanded = ref(new Set(props.defaultExpanded))
const expanded = computed({
  get: () => props.expandedIds ?? internalExpanded.value,
  set: (value) => {
    if (props.expandedIds !== null) {
      emit('update:expandedIds', value)
    } else {
      internalExpanded.value = value
    }
  },
})

// --- Inline editing ---

const editingId = ref(null)
let editStartedAt = 0

function startEditing(item) {
  if (!props.canRename || props.canRename(item)) {
    editingId.value = item.id
    editStartedAt = Date.now()
  }
}

function commitEdit(item, newLabel) {
  const trimmed = newLabel.trim()
  if (trimmed && trimmed !== item.label) {
    emit('rename', { item, newLabel: trimmed })
  }
  editingId.value = null
}

function cancelEdit() {
  editingId.value = null
}

function onEditBlur(item, e) {
  // Radix context menu restores focus to the trigger button after closing,
  // which steals focus from our input. Ignore blur events that fire within
  // 200ms of the edit starting and re-focus the input instead.
  if (Date.now() - editStartedAt < 200) {
    e.target.focus()
    return
  }
  commitEdit(item, e.target.value)
}

const contextMenuItemId = ref(null)

function onContextMenuOpenChange(itemId, open) {
  contextMenuItemId.value = open ? itemId : null
}

function hasContextMenu(item) {
  return props.contextMenuFor ? props.contextMenuFor(item) : false
}

const focusedId = ref(null)

defineExpose({ startEditing, expanded, focusItem })

// --- Tree logic ---

function hasChildren(item) {
  const children = item[props.childrenKey]
  return Array.isArray(children) && children.length > 0
}

function isExpandable(item) {
  return hasChildren(item) || !!item.expandable
}

function isExpanded(item) {
  return expanded.value.has(item.id)
}

function toggle(item) {
  const set = new Set(expanded.value)
  if (set.has(item.id)) {
    set.delete(item.id)
  } else {
    set.add(item.id)
  }
  expanded.value = set
  emit('toggle', item)
}

function click(item) {
  if (editingId.value) return
  if (item.selectable === false) {
    if (isExpandable(item)) toggle(item)
    return
  }
  emit('select', item)
}

function onChevronClick(item, e) {
  if (item.selectable !== false) {
    e.stopPropagation()
    toggle(item)
  }
}

function onRowMousedown(item, e) {
  // Prevent the button from stealing focus from the inline edit input,
  // but allow clicks inside the input itself so the cursor can be repositioned.
  if (editingId.value === item.id && e.target.tagName !== 'INPUT') {
    e.preventDefault()
  }
}

// --- Recursive lookups ---

function findParentId(itemId) {
  function search(items, parentId) {
    for (const item of items) {
      if (item.id === itemId) return parentId
      const children = item[props.childrenKey]
      if (Array.isArray(children)) {
        const found = search(children, item.id)
        if (found !== undefined) return found
      }
    }
    return undefined
  }
  const result = search(props.items, null)
  return result === undefined ? null : result
}

function findItemById(id) {
  function search(items) {
    for (const item of items) {
      if (item.id === id) return item
      const children = item[props.childrenKey]
      if (Array.isArray(children)) {
        const found = search(children)
        if (found) return found
      }
    }
    return null
  }
  return search(props.items)
}

function isDescendantOf(potentialAncestorId, itemId) {
  const item = findItemById(potentialAncestorId)
  if (!item) return false
  function search(items) {
    for (const i of items) {
      if (i.id === itemId) return true
      const children = i[props.childrenKey]
      if (Array.isArray(children) && search(children)) return true
    }
    return false
  }
  return search(item[props.childrenKey] ?? [])
}

// --- Keyboard navigation ---

function walkVisible(items, depth, parentId, callback) {
  for (const item of items) {
    callback(item, depth, parentId)
    const children = item[props.childrenKey]
    if (Array.isArray(children) && isExpanded(item)) {
      walkVisible(children, depth + 1, item.id, callback)
    }
  }
}

function getVisibleItems() {
  const result = []
  walkVisible(props.items, 0, null, (item) => result.push(item))
  return result
}

function focusItem(id) {
  focusedId.value = id
  nextTick(() => {
    const el = rowRefs.get(id)
    if (el) el.focus()
  })
}

function handleTreeKeydown(e) {
  if (editingId.value) return

  const visible = getVisibleItems()
  if (visible.length === 0) return

  const currentIndex = visible.findIndex((item) => item.id === focusedId.value)

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    const nextIndex = currentIndex < visible.length - 1 ? currentIndex + 1 : 0
    focusItem(visible[nextIndex].id)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    const prevIndex = currentIndex > 0 ? currentIndex - 1 : visible.length - 1
    focusItem(visible[prevIndex].id)
  } else if (e.key === 'ArrowRight') {
    e.preventDefault()
    const item = visible[currentIndex]
    if (item && isExpandable(item) && !isExpanded(item)) {
      toggle(item)
    } else if (item && isExpanded(item) && hasChildren(item)) {
      const children = item[props.childrenKey]
      focusItem(children[0].id)
    }
  } else if (e.key === 'ArrowLeft') {
    e.preventDefault()
    const item = visible[currentIndex]
    if (item && isExpandable(item) && isExpanded(item)) {
      toggle(item)
    } else if (item) {
      const parentId = findParentId(item.id)
      if (parentId !== null) {
        focusItem(parentId)
      }
    }
  } else if (e.key === 'Home') {
    e.preventDefault()
    focusItem(visible[0].id)
  } else if (e.key === 'End') {
    e.preventDefault()
    focusItem(visible[visible.length - 1].id)
  } else if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    const item = visible[currentIndex]
    if (item) click(item)
  } else if (e.key === 'F2') {
    e.preventDefault()
    const item = visible[currentIndex] ?? findItemById(props.selectedId)
    if (item) startEditing(item)
  }
}

// --- Drag and drop ---

const rowRefs = new Map()
const draggedId = ref(null)
const lineIndicator = ref(null)
const dropTargetId = ref(null)

let dndCleanup = null

function setRowRef(id, el) {
  if (el) {
    rowRefs.set(id, el)
  }
}

function normalizeDrop(sourceItem, targetItem, zone) {
  if (!props.resolveDrop) return null
  const result = props.resolveDrop(sourceItem, targetItem, zone)
  if (!result) return null
  if (result === true) return { targetId: targetItem.id, zone }
  return result
}

function computeZone(sourceItem, item, input, element) {
  const into = normalizeDrop(sourceItem, item, 'into')
  const above = normalizeDrop(sourceItem, item, 'above')
  const below = normalizeDrop(sourceItem, item, 'below')
  const rect = element.getBoundingClientRect()
  const relativeY = (input.clientY - rect.top) / rect.height

  let resolved = null
  if (into && !above && !below) {
    resolved = into
  } else if (into) {
    if (relativeY < 0.25 && above) resolved = above
    else if (relativeY > 0.75 && below) resolved = below
    else resolved = into
  } else if (above || below) {
    const preferred = relativeY < 0.5 ? above : below
    resolved = preferred ?? above ?? below
  }
  return resolved
}

function attachDnD() {
  if (dndCleanup) {
    dndCleanup()
    dndCleanup = null
  }

  if (!props.canDrag || !props.resolveDrop) return

  // Prune stale refs
  for (const [id, el] of rowRefs) {
    if (!el.isConnected) rowRefs.delete(id)
  }

  const cleanups = []

  walkVisible(props.items, 0, null, (item, depth, parentId) => {
    attachToRow(item, depth, parentId, cleanups)
  })

  cleanups.push(
    monitorForElements({
      onDrop({ source, location }) {
        handleDrop(source, location)
        draggedId.value = null
        lineIndicator.value = null
        dropTargetId.value = null
      },
    }),
  )

  dndCleanup = combine(...cleanups)
}

function attachToRow(item, depth, parentId, cleanups) {
  const el = rowRefs.get(item.id)
  if (!el || !el.isConnected) return

  if (props.canDrag(item)) {
    cleanups.push(
      makeDraggable({
        element: el,
        // noteId/label ride along so drop targets outside the tree (the editor,
        // which inserts a link) can identify a real note - the virtual
        // Templates/Trash roots carry no noteId.
        getInitialData: () => ({ id: item.id, noteId: item.noteId, label: item.label }),
        onGenerateDragPreview: ({ nativeSetDragImage }) => {
          setCustomNativeDragPreview({
            getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
            render({ container }) {
              const preview = document.createElement('div')
              preview.textContent = item.label
              preview.style.cssText = 'padding: 4px 10px; border-radius: 4px; font-size: 12px; background: var(--color-surface-alt); color: var(--color-on-surface); border: 1px solid var(--color-border); white-space: nowrap; max-width: 200px; overflow: hidden; text-overflow: ellipsis;'
              container.appendChild(preview)
            },
            nativeSetDragImage,
          })
        },
        onDragStart: () => {
          draggedId.value = item.id
        },
        onDrop: () => {
          draggedId.value = null
        },
      }),
    )
  }

  cleanups.push(
    dropTargetForElements({
      element: el,
      canDrop: ({ source, input }) => {
        if (source.data.id === item.id) return false
        if (isDescendantOf(source.data.id, item.id)) return false
        const sourceItem = findItemById(source.data.id)
        if (!sourceItem) return false
        return computeZone(sourceItem, item, input, el) !== null
      },
      getData: ({ input }) => {
        const sourceItem = findItemById(draggedId.value)
        const resolved = sourceItem ? computeZone(sourceItem, item, input, el) : null
        return { id: item.id, resolved }
      },
      onDragEnter: ({ self }) => updateIndicators(item, self.data.resolved),
      onDrag: ({ self }) => updateIndicators(item, self.data.resolved),
      onDragLeave: () => {
        if (lineIndicator.value?.ownerId === item.id) {
          lineIndicator.value = null
        }
        if (dropTargetId.value?.ownerId === item.id) {
          dropTargetId.value = null
        }
      },
      onDrop: () => {
        lineIndicator.value = null
        dropTargetId.value = null
      },
    }),
  )
}

function updateIndicators(item, resolved) {
  if (!resolved) {
    lineIndicator.value = null
    dropTargetId.value = null
  } else if (resolved.zone === 'into') {
    dropTargetId.value = { ownerId: item.id, targetId: resolved.targetId }
    lineIndicator.value = null
  } else {
    lineIndicator.value = { ownerId: item.id, itemId: resolved.targetId, edge: resolved.zone === 'above' ? 'top' : 'bottom' }
    dropTargetId.value = null
  }
}

function handleDrop(source, location) {
  const targets = location.current.dropTargets
  if (targets.length === 0) return

  const resolved = targets[0].data.resolved
  const sourceItem = findItemById(source.data.id)
  if (!resolved || !sourceItem) return

  const targetItem = findItemById(resolved.targetId)
  if (!targetItem) return

  emit('drop', { source: sourceItem, target: targetItem, zone: resolved.zone })
}

onMounted(() => {
  nextTick(() => attachDnD())
})

watch(
  () => [props.items, expanded.value],
  () => attachDnD(),
  { deep: true, flush: 'post' },
)

onUnmounted(() => {
  if (dndCleanup) {
    dndCleanup()
  }
  rowRefs.clear()
})

provide('tree', {
  childrenKey: props.childrenKey,
  selectedId: toRef(props, 'selectedId'),
  expanded,
  editingId,
  focusedId,
  contextMenuItemId,
  draggedId,
  lineIndicator,
  dropTargetId,
  startEditing,
  commitEdit,
  cancelEdit,
  onEditBlur,
  onContextMenuOpenChange,
  hasContextMenu,
  click,
  onChevronClick,
  onRowMousedown,
  setRowRef,
})
</script>

<template>
  <div role="tree" @keydown="handleTreeKeydown">
    <TreeNode v-for="item in items" :key="item.id" :item="item" :depth="0" :parent-id="null">
      <template #item="slotProps">
        <slot name="item" v-bind="slotProps" />
      </template>
      <template #context-menu="slotProps">
        <slot name="context-menu" v-bind="slotProps" />
      </template>
    </TreeNode>
  </div>
</template>
