<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  ContextMenuItem,
  DropdownMenuRoot,
  DropdownMenuTrigger,
  DropdownMenuPortal,
  DropdownMenuContent,
  DropdownMenuItem,
} from 'reka-ui'
import Tree from '../../components/Tree.vue'
import Button from '../../components/Button.vue'
import EmptyState from '../../components/EmptyState.vue'
import TextField from '../../components/TextField.vue'
import HoverTip from '../../components/HoverTip.vue'
import ImportDialog from './ImportDialog.vue'
import IconSearch from '~icons/lucide/search'
import { MENU_ITEM_CLASS as menuItemClass } from '../../utils/menu.js'
import { exportSubtree, exportAll } from '../../services/exportService.js'
import { useNotesStore, TEMPLATES_ID, TRASH_ID } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'

const emit = defineEmits(['request-delete', 'request-purge', 'request-restore', 'request-template'])

const router = useRouter()
const notesStore = useNotesStore()
const settingsStore = useSettingsStore()
const treeRef = ref(null)

// --- Header ---

const importOpen = ref(false)
const importParentId = ref(null)

function openImport(parentId = null) {
  importParentId.value = parentId
  importOpen.value = true
}

// Below this the filter and the four icon buttons no longer share the row, so
// the icons collapse into one kebab menu - all or nothing, because a partial
// collapse would make actions appear and disappear one at a time as the
// splitter moves. Measured (not a window breakpoint): the splitter is what
// changes this panel's width.
const COLLAPSE_WIDTH = 300

const headerRef = ref(null)
const collapsed = ref(false)
let headerObserver = null

onMounted(() => {
  headerObserver = new ResizeObserver((entries) => {
    collapsed.value = entries[0].contentRect.width < COLLAPSE_WIDTH
  })
  headerObserver.observe(headerRef.value)
})

onUnmounted(() => {
  headerObserver?.disconnect()
})

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

// Two different empties, and they are not the same message. Templates and Trash
// are always in the tree, so "no notes" is no note-type roots rather than an
// empty tree - the message sits under those two rows, not instead of them.
const noMatches = computed(() => filtering.value && visibleItems.value.length === 0)
const noNotes = computed(() =>
  !filtering.value && !notesStore.treeItems.some((item) => item.type === 'note'))

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

async function onSelect(item) {
  if (item.type === 'note') {
    // Selection is the route (unified routing): the view's param watcher does
    // the fetch. Reselecting the open note is a no-op navigation, and the focus
    // event still fires for it - same refocus behavior as before.
    await router.push(`/notes/${item.noteId}`)
    window.dispatchEvent(new CustomEvent('editor:focus'))
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
  await router.push(`/notes/${note.id}`)
  if (settingsStore.application.newNoteFocus === 'body') {
    window.dispatchEvent(new CustomEvent('editor:focus'))
  } else {
    requestAnimationFrame(() => {
      treeRef.value?.startEditing({ id: note.id, label: note.title || 'Untitled', type: 'note' })
      treeRef.value?.focusItem(note.id)
    })
  }
}

async function duplicateNote(item) {
  const response = await notesStore.duplicate(item.noteId)
  await router.push(`/notes/${response.rootId}`)
  requestAnimationFrame(() => {
    treeRef.value?.startEditing({ id: response.rootId, label: response.title, type: 'note' })
    treeRef.value?.focusItem(response.rootId)
  })
}

// Function refs re-fire on re-render; the guard keeps a background refresh from
// clobbering the caret and reselecting mid-typing.
function focusEditInput(el) {
  if (el && document.activeElement !== el) {
    el.focus()
    el.select()
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

// Returns null both for "root level" and "not found" - callers only pass ids that
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

// Esc in the editor lands here. Selecting a note moves focus into the document,
// and nothing else brings it back - without this the tree's F2, arrows and Enter
// are unreachable the moment you open anything.
function focusTree() {
  treeRef.value?.focusTree(notesStore.selectedId)
}

defineExpose({ treeRef, addNote, focusTree })
</script>

<template>
  <div class="h-full flex flex-col min-h-0">
    <div ref="headerRef" class="flex items-center gap-1 px-2 h-10 shrink-0 border-b border-border">
      <TextField v-model="query" size="small" :icon="IconSearch" placeholder="Filter notes" class="flex-1" />
      <template v-if="!collapsed">
        <HoverTip text="Import notes" side="bottom">
          <Button variant="ghost" size="icon" @click="openImport()">
            <i-lucide-import class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Export all notes" side="bottom">
          <Button variant="ghost" size="icon" @click="exportAll()">
            <i-lucide-folder-output class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="New note from template" side="bottom">
          <Button variant="ghost" size="icon" @click="emit('request-template', { parentId: null })">
            <i-lucide-layout-template class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="New note" side="bottom">
          <Button variant="ghost" size="icon" @click="addNote(null)">
            <i-lucide-plus class="size-4" />
          </Button>
        </HoverTip>
      </template>
      <DropdownMenuRoot v-else>
        <DropdownMenuTrigger as-child>
          <Button variant="ghost" size="icon">
            <i-lucide-ellipsis-vertical class="size-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuPortal>
          <DropdownMenuContent :side-offset="6" align="end"
            class="z-50 min-w-48 rounded-md border border-border bg-surface-elevated p-1 shadow-lg">
            <DropdownMenuItem :class="menuItemClass" @select="addNote(null)">
              <i-lucide-plus class="size-4 text-on-surface-muted" />
              New note
            </DropdownMenuItem>
            <DropdownMenuItem :class="menuItemClass" @select="emit('request-template', { parentId: null })">
              <i-lucide-layout-template class="size-4 text-on-surface-muted" />
              New note from template
            </DropdownMenuItem>
            <DropdownMenuItem :class="menuItemClass" @select="exportAll()">
              <i-lucide-folder-output class="size-4 text-on-surface-muted" />
              Export all notes
            </DropdownMenuItem>
            <DropdownMenuItem :class="menuItemClass" @select="openImport()">
              <i-lucide-import class="size-4 text-on-surface-muted" />
              Import notes
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenuPortal>
      </DropdownMenuRoot>
    </div>

    <ImportDialog v-model:open="importOpen" :parent-id="importParentId" />

    <div class="flex-1 min-h-0 overflow-y-auto overflow-x-auto pb-2">
      <!-- Above the tree, because that is where the notes are: the message stands
           in for the missing rows, and Templates and Trash stay put below it. -->
      <EmptyState v-if="noMatches">
        No notes match the filter
      </EmptyState>
      <EmptyState v-else-if="noNotes">
        No notes yet. Create one with +.
      </EmptyState>

      <Tree ref="treeRef" :items="visibleItems" :selected-id="notesStore.selectedId"
        :expanded-ids="notesStore.expandedIds" :can-drag="canDrag" :resolve-drop="resolveDrop" :can-rename="canRename"
        :context-menu-for="contextMenuFor" @select="onSelect" @rename="onRename" @drop="onDrop"
        @update:expanded-ids="(s) => (notesStore.expandedIds = s)">
        <template #item="{ item, editing, onEditBlur, cancelEdit }">
          <template v-if="item.type === 'note'">
            <!-- .prevent on Enter: committing hands focus back to the row button,
                 and Enter's default action is "activate whatever is focused" -
                 applied after the handlers run, so without this the row is
                 activated by the same keystroke that finished the rename. -->
            <input v-if="editing" :value="item.label === 'Untitled' ? '' : item.label" placeholder="Untitled"
              class="flex-1 min-w-0 bg-transparent text-sm border-b border-accent outline-none"
              :ref="focusEditInput" @blur="onEditBlur(item, $event)"
              @keydown.enter.prevent="$event.target.blur()" @keydown.esc.stop="cancelEdit()" @click.stop />
            <template v-else>
              <!-- The transparent border matches the rename input's underline, so
                   entering edit mode doesn't grow the row by a pixel. -->
              <span class="truncate text-sm border-b border-transparent"
                :class="item.trashed ? 'text-on-surface-muted' : ''">
                {{ item.label }}</span>
              <span v-if="!item.trashed" class="ml-auto shrink-0 hidden group-hover:flex items-center gap-1" @click.stop
                @dblclick.stop>
                <HoverTip text="Add child from template" side="bottom">
                  <span role="button"
                    class="p-0.5 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
                    @click="emit('request-template', { parentId: item.noteId })">
                    <i-lucide-layout-template class="size-3.5" />
                  </span>
                </HoverTip>
                <HoverTip text="Add child note" side="bottom">
                  <span role="button"
                    class="p-0.5 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
                    @click="addNote(item.noteId)">
                    <i-lucide-plus class="size-3.5" />
                  </span>
                </HoverTip>
              </span>
            </template>
          </template>
          <template v-else>
            <i-lucide-layout-template v-if="item.type === 'templates-root'"
              class="size-4 shrink-0 text-on-surface-muted" />
            <i-lucide-trash-2 v-else class="size-4 shrink-0 text-on-surface-muted" />
            <!-- Templates/Trash never rename, but they share the row height. -->
            <span class="truncate text-sm text-on-surface-muted border-b border-transparent">{{ item.label }}</span>
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
              Delete permanently
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
            <ContextMenuItem :class="menuItemClass" @select="duplicateNote(item)">
              <i-lucide-copy class="size-4 text-on-surface-muted" />
              Duplicate
            </ContextMenuItem>
            <ContextMenuItem :class="menuItemClass" @select="exportSubtree(item.noteId)">
              <i-lucide-folder-output class="size-4 text-on-surface-muted" />
              Export as markdown
            </ContextMenuItem>
            <!-- Templates are excluded: the destination picker only offers live
                 normal notes, so a template target has nothing to preselect. -->
            <ContextMenuItem v-if="!item.template" :class="menuItemClass" @select="openImport(item.noteId)">
              <i-lucide-import class="size-4 text-on-surface-muted" />
              Import notes
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
