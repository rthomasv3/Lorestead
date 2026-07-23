<script setup>
import { ref, computed, watch } from 'vue'
import { ContextMenuItem, DropdownMenuRoot, DropdownMenuTrigger, DropdownMenuPortal, DropdownMenuContent, DropdownMenuItem } from 'reka-ui'
import Tree from '../../components/Tree.vue'
import { useNotesStore, TEMPLATES_ID, TRASH_ID } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'

const emit = defineEmits(['request-delete', 'request-purge', 'request-restore', 'request-template'])

const notesStore = useNotesStore()
const settingsStore = useSettingsStore()
const treeRef = ref(null)

// --- Filter ---

const query = ref('')
const ftsIds = ref(new Set())
let filterTimer = null
let expandedSnapshot = null

watch(query, (value) => {
  clearTimeout(filterTimer)
  if (!value.trim()) {
    ftsIds.value = new Set()
    if (expandedSnapshot) {
      notesStore.expandedIds = expandedSnapshot
      expandedSnapshot = null
    }
    return
  }
  filterTimer = setTimeout(async () => {
    const results = await notesStore.search(value.trim(), { includeTrashed: true })
    ftsIds.value = new Set(results.map((r) => r.id))
    expandFiltered()
  }, 250)
})

const filtering = computed(() => query.value.trim().length > 0)

function matches(item) {
  if (item.type !== 'note') return false
  const q = query.value.trim().toLowerCase()
  return item.label.toLowerCase().includes(q) || ftsIds.value.has(item.noteId)
}

function prune(item) {
  const children = (item.children ?? []).map(prune).filter(Boolean)
  if (matches(item) || children.length > 0) {
    return { ...item, children }
  }
  return null
}

const visibleItems = computed(() => {
  if (!filtering.value) return notesStore.treeItems
  return notesStore.treeItems
    .map(prune)
    .filter((item) => item && (item.type === 'note' || item.children.length > 0))
})

function expandFiltered() {
  if (!expandedSnapshot) {
    expandedSnapshot = new Set(notesStore.expandedIds)
  }
  const ids = new Set()
  function collect(items) {
    for (const item of items) {
      if (item.children?.length) {
        ids.add(item.id)
        collect(item.children)
      }
    }
  }
  collect(visibleItems.value)
  notesStore.expandedIds = ids
}

// --- Selection / rename / add ---

function onSelect(item) {
  if (item.type === 'note') {
    notesStore.select(item.noteId)
  }
}

function onRename({ item, newLabel }) {
  if (item.type === 'note') {
    notesStore.rename(item.noteId, newLabel)
  }
}

function canRename(item) {
  return item.type === 'note' && !item.trashed
}

async function addNote(parentId) {
  const note = await notesStore.create({ parentId })
  if (parentId && !notesStore.expandedIds.has(parentId)) {
    notesStore.expandedIds = new Set([...notesStore.expandedIds, parentId])
  }
  await notesStore.select(note.id)
  if (settingsStore.application.newNoteFocus === 'body') {
    window.dispatchEvent(new CustomEvent('editor:focus'))
  } else {
    requestAnimationFrame(() => {
      treeRef.value?.startEditing({ id: note.id, label: note.title || 'Untitled', type: 'note' })
      treeRef.value?.focusItem(note.id)
    })
  }
}

// --- Drag & drop ---

function canDrag(item) {
  return !filtering.value && item.type === 'note'
}

function resolveDrop(source, target, zone) {
  let result = false
  if (source.type === 'note' && !filtering.value) {
    if (source.trashed) {
      // Drag-out restore: normal notes section only, placed where dropped.
      result = target.type === 'note' && !target.trashed && !target.template
    } else if (target.type === 'trash-root') {
      result = zone === 'into'
    } else if (target.type === 'note' && target.trashed) {
      // The expanded Trash body is one whole "Delete Item" drop area.
      result = { targetId: TRASH_ID, zone: 'into' }
    } else if (target.type === 'templates-root') {
      result = zone === 'into'
    } else if (target.type === 'note') {
      result = true
    }
  }
  return result
}

// Returns null both for "root level" and "not found" — callers only pass ids that
// exist in the tree, so null always means root level.
function findParentItem(items, id, parent = null) {
  for (const item of items) {
    if (item.id === id) return parent
    const found = findParentItem(item.children ?? [], id, item)
    if (found) return found
  }
  return null
}

function siblingsOf(parentItem) {
  if (parentItem === null) {
    return notesStore.treeItems.filter((item) => item.type === 'note')
  }
  return parentItem.children ?? []
}

function neighborsForEdge(target, zone, sourceId) {
  const parentItem = findParentItem(notesStore.treeItems, target.id)
  const siblings = siblingsOf(parentItem).filter((s) => s.id !== sourceId)
  const targetIndex = siblings.findIndex((s) => s.id === target.id)
  const insertAt = zone === 'above' ? targetIndex : targetIndex + 1
  return {
    parentItem,
    previousId: insertAt > 0 ? siblings[insertAt - 1].noteId : null,
    nextId: insertAt < siblings.length ? siblings[insertAt].noteId : null,
  }
}

function onDrop({ source, target, zone }) {
  if (target.type === 'trash-root') {
    emit('request-delete', { item: source, viaDrag: true })
    return
  }

  let parentId = null
  let previousId = null
  let nextId = null
  let template = false

  if (zone === 'into') {
    if (target.type === 'templates-root') {
      const templates = target.children.filter((c) => c.id !== source.id)
      parentId = null
      previousId = templates.length > 0 ? templates[templates.length - 1].noteId : null
      template = true
    } else {
      const children = (target.children ?? []).filter((c) => c.id !== source.id)
      parentId = target.noteId
      previousId = children.length > 0 ? children[children.length - 1].noteId : null
    }
  } else {
    const edge = neighborsForEdge(target, zone, source.id)
    previousId = edge.previousId
    nextId = edge.nextId
    if (edge.parentItem === null) {
      parentId = null
    } else if (edge.parentItem.type === 'templates-root') {
      parentId = null
      template = true
    } else {
      parentId = edge.parentItem.noteId
    }
  }

  if (source.trashed) {
    notesStore.restoreAt({ id: source.noteId, parentId, previousId, nextId })
  } else {
    notesStore.move({ id: source.noteId, parentId, previousId, nextId, template })
  }
}

// --- Context menus ---

function contextMenuFor(item) {
  return item.type === 'note'
}

function onRestoreClick(item) {
  const parentItem = findParentItem(notesStore.treeItems, item.id)
  const nestedInTrash = parentItem && parentItem.type === 'note' && parentItem.trashed
  emit('request-restore', { item, nested: !!nestedInTrash })
}

const menuItemClass = 'flex items-center gap-2 px-2.5 py-1.5 text-sm rounded-md cursor-default select-none outline-none data-highlighted:bg-surface-alt'

defineExpose({ treeRef, addNote })
</script>

<template>
  <div class="h-full flex flex-col min-h-0">
    <div class="flex items-center gap-1.5 p-2 shrink-0">
      <div class="flex-1 min-w-0 flex items-center gap-2 rounded-md border border-border bg-surface-alt px-2 h-8">
        <i-lucide-search class="size-3.5 shrink-0 text-on-surface-muted" />
        <input v-model="query" placeholder="Filter notes"
          class="w-full bg-transparent text-sm outline-none placeholder:text-on-surface-muted/60" />
      </div>
      <div class="flex shrink-0 rounded-md border border-border overflow-hidden">
        <button class="h-8 px-2 hover:bg-surface-alt text-on-surface-muted hover:text-on-surface" title="New note"
          @click="addNote(null)">
          <i-lucide-plus class="size-4" />
        </button>
        <DropdownMenuRoot>
          <DropdownMenuTrigger
            class="h-8 px-1 border-l border-border hover:bg-surface-alt text-on-surface-muted hover:text-on-surface">
            <i-lucide-chevron-down class="size-3.5" />
          </DropdownMenuTrigger>
          <DropdownMenuPortal>
            <DropdownMenuContent align="end" :side-offset="4"
              class="bg-surface-elevated border border-border rounded-lg shadow-lg p-1 min-w-40 z-50">
              <DropdownMenuItem :class="menuItemClass" @select="emit('request-template', { parentId: null })">
                <i-lucide-layout-template class="size-4 text-on-surface-muted" />
                From template
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenuPortal>
        </DropdownMenuRoot>
      </div>
    </div>

    <div class="flex-1 min-h-0 overflow-y-auto overflow-x-auto pb-2">
      <Tree ref="treeRef" :items="visibleItems" :selected-id="notesStore.selectedId"
        :expanded-ids="notesStore.expandedIds" :can-drag="canDrag"
        :resolve-drop="resolveDrop" :can-rename="canRename" :context-menu-for="contextMenuFor" @select="onSelect"
        @rename="onRename" @drop="onDrop" @update:expanded-ids="(s) => (notesStore.expandedIds = s)">
        <template #item="{ item, editing, onEditBlur, cancelEdit }">
          <template v-if="item.type === 'note'">
            <input v-if="editing" :value="item.label === 'Untitled' ? '' : item.label" placeholder="Untitled"
              class="flex-1 min-w-0 bg-transparent text-sm border-b border-accent outline-none"
              :ref="(el) => el && el.focus()" @blur="onEditBlur(item, $event)" @keydown.enter="$event.target.blur()"
              @keydown.esc.stop="cancelEdit()" @click.stop />
            <template v-else>
              <span class="truncate text-sm" :class="item.trashed ? 'text-on-surface-muted' : ''">
                {{ item.label }}</span>
              <span v-if="!item.trashed" class="ml-auto shrink-0 hidden group-hover:flex items-center gap-1" @click.stop
                @dblclick.stop>
                <span role="button" title="Add child note"
                  class="p-0.5 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
                  @click="addNote(item.noteId)">
                  <i-lucide-plus class="size-3.5" />
                </span>
                <span role="button" title="Add child from template"
                  class="p-0.5 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
                  @click="emit('request-template', { parentId: item.noteId })">
                  <i-lucide-layout-template class="size-3.5" />
                </span>
              </span>
            </template>
          </template>
          <template v-else>
            <i-lucide-layout-template v-if="item.type === 'templates-root'"
              class="size-4 shrink-0 text-on-surface-muted" />
            <i-lucide-trash-2 v-else class="size-4 shrink-0 text-on-surface-muted" />
            <span class="truncate text-sm text-on-surface-muted">{{ item.label }}</span>
          </template>
        </template>

        <template #context-menu="{ item }">
          <template v-if="item.trashed">
            <ContextMenuItem :class="menuItemClass" @select="onRestoreClick(item)">
              <i-lucide-archive-restore class="size-4 text-on-surface-muted" />
              Restore
            </ContextMenuItem>
            <ContextMenuItem :class="menuItemClass" @select="emit('request-purge', { item })">
              <i-lucide-trash-2 class="size-4 text-red-500" />
              Delete Permanently
            </ContextMenuItem>
          </template>
          <template v-else>
            <ContextMenuItem :class="menuItemClass" @select="addNote(item.noteId)">
              <i-lucide-plus class="size-4 text-on-surface-muted" />
              Add child note
            </ContextMenuItem>
            <ContextMenuItem :class="menuItemClass" @select="emit('request-template', { parentId: item.noteId })">
              <i-lucide-layout-template class="size-4 text-on-surface-muted" />
              Add from template
            </ContextMenuItem>
            <ContextMenuItem :class="menuItemClass" @select="treeRef.startEditing(item)">
              <i-lucide-pencil class="size-4 text-on-surface-muted" />
              Rename
            </ContextMenuItem>
            <ContextMenuItem :class="menuItemClass" @select="emit('request-delete', { item, viaDrag: false })">
              <i-lucide-trash-2 class="size-4 text-red-500" />
              Delete
            </ContextMenuItem>
          </template>
        </template>
      </Tree>
    </div>
  </div>
</template>
