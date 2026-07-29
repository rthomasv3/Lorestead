<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { SplitterGroup, SplitterPanel, SplitterResizeHandle, DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle, DialogDescription } from 'reka-ui'
import HoverTip from '../../components/HoverTip.vue'
import NotesTreePanel from './NotesTreePanel.vue'
import AttachmentsPanel from './AttachmentsPanel.vue'
import BacklinksPanel from './BacklinksPanel.vue'
import HistoryPanel from './HistoryPanel.vue'
import ToolPanelShell from '../../components/ToolPanelShell.vue'
import NewFromTemplateDialog from './NewFromTemplateDialog.vue'
import MarkdownEditor from '../../components/MarkdownEditor.vue'
import MarkdownPreview from '../../components/MarkdownPreview.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import Button from '../../components/Button.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'
import { useScrollSync } from '../../composables/useScrollSync.js'
import { formatTimestamp } from '../../utils/dateFormat.js'
import { exportNote } from '../../services/exportService.js'
import { TOOLBAR_ACTIONS } from '../../utils/editorToolbar.js'
import { shortcut } from '../../utils/platform.js'

const notesStore = useNotesStore()
const settingsStore = useSettingsStore()

const treePanel = ref(null)
const editorRef = ref(null)
const previewScroller = ref(null)

const { onEditorScroll, onPreviewScroll } = useScrollSync(editorRef, previewScroller)

// Count bubble on a rail button; the button itself supplies `relative`.
const RAIL_BADGE_CLASS =
  'absolute -top-0.5 -right-0.5 min-w-4 h-4 px-0.5 rounded-full bg-accent-strong text-white text-[10px] leading-4 text-center'

const pendingTrash = ref(null)
const pendingPurge = ref(null)
const restoreTarget = ref(null)
const templateDialog = ref({ open: false, parentId: null })

const currentNote = computed(() => notesStore.currentNote)
const readonly = computed(() => !!currentNote.value?.deleted)

// --- Body editing + autosave ---

const body = ref('')
const dirty = ref(false)
let saveTimer = null
// Reactive because it is also the editor's document key: the id of the text
// actually in the buffer, which lags the store's selection by one fetch. Keyed
// on selectedId instead, opening a note reads as "new document" while the old
// note's text is still loaded, and the remembered offset gets clamped to that.
const editingNoteId = ref(null)

// The store keeps only the selection across route changes - content is dropped on
// unmount and refetched on mount - so on remount this fires with null (empty
// buffer) and again when the fetch lands. immediate: a reselect of the same id
// still swaps the buffer through here.
watch(currentNote, async (note) => {
  if (editingNoteId.value && editingNoteId.value !== note?.id) {
    await flushFor(editingNoteId.value)
  }
  editingNoteId.value = note?.id ?? null
  body.value = note?.body ?? ''
  dirty.value = false
  clearTimeout(saveTimer)
}, { immediate: true })

// An agent edit to the open note (data_version watcher) reloads it only while the
// editor is clean. Mid-edit the pending autosave is the later write under LWW, so
// the buffer already holds the version that wins - there is nothing stale to show,
// and replacing the document would throw away what is being typed.
function onNotesChanged() {
  if (!dirty.value && editingNoteId.value) {
    notesStore.select(editingNoteId.value)
  }
}

onMounted(() => window.addEventListener('notes:changed', onNotesChanged))
onUnmounted(() => window.removeEventListener('notes:changed', onNotesChanged))

function onBodyChange(value) {
  if (readonly.value || !editingNoteId.value) return
  body.value = value
  dirty.value = true
  clearTimeout(saveTimer)
  saveTimer = setTimeout(flush, settingsStore.editor.autosaveDebounceMs || 1000)
}

async function flushFor(noteId) {
  if (!dirty.value || !noteId) return
  const text = body.value
  dirty.value = false
  clearTimeout(saveTimer)
  await notesStore.saveBody(noteId, text)
  await maybeAutoFillTitle(noteId, text)
}

function flush() {
  return flushFor(editingNoteId.value)
}

// data.md: an empty title auto-fills from the body's first line; edits to the title
// itself never write back to the body.
async function maybeAutoFillTitle(noteId, text) {
  const summary = notesStore.byId.get(noteId)
  if (summary && !summary.title && text.trim()) {
    const firstLine = text.trim().split('\n')[0].replace(/^[#>\-*\s\d.[\]]+/, '').trim().slice(0, 120)
    if (firstLine) {
      await notesStore.rename(noteId, firstLine)
    }
  }
}

const wordCount = computed(() => {
  const text = body.value.trim()
  return text ? text.split(/\s+/).length : 0
})

// Reads the summary row (not currentNote) so the stamp advances live as
// autosaves land; falls back to created when updated is missing.
const modifiedLabel = computed(() => {
  const note = currentNote.value
  if (!note) return ''
  const summary = notesStore.byId.get(note.id)
  const iso = summary?.updatedAt || note.updatedAt || summary?.createdAt || note.createdAt
  const app = settingsStore.application
  const stamp = formatTimestamp(iso, app.dateFormat, app.timeFormat)
  return stamp ? `Last updated ${stamp}` : ''
})

function onEditorFocusRequest() {
  editorRef.value?.focus()
}

// Esc is the way back out of the document. Not when CodeMirror already used the
// key - an open `[[` completion closes on the first Esc and only the second one
// leaves the editor.
function onEditorEscape(e) {
  if (!e.defaultPrevented) {
    treePanel.value?.focusTree()
  }
}

function onBeforeUnload() {
  // Best-effort synchronous kick-off; the bridge call itself is async.
  flush()
}

// Preview and export act on the whole note rather than on the text under the
// caret, so they are the page's keys, not the editor's - the task dialog has
// neither. Shift on both: plain Mod+P prints and Mod+S is the save flush.
const PREVIEW_KEY = shortcut('mod', 'shift', 'P')
const EXPORT_KEY = shortcut('mod', 'shift', 'S')

// On window, not on the view's root: the editor holds focus most of the time and
// CodeMirror stops keydown from reaching an ancestor listener.
function onViewKeydown(e) {
  if (!(e.ctrlKey || e.metaKey) || !e.shiftKey) return
  const key = e.key.toLowerCase()
  if (key === 'p') {
    e.preventDefault()
    notesStore.previewOpen = !notesStore.previewOpen
  } else if (key === 's' && !readonly.value && currentNote.value) {
    e.preventDefault()
    exportNote(currentNote.value.id)
  }
}

onMounted(() => {
  window.addEventListener('editor:focus', onEditorFocusRequest)
  window.addEventListener('beforeunload', onBeforeUnload)
  window.addEventListener('keydown', onViewKeydown)
})

onUnmounted(() => {
  // flush() registers its save synchronously (notesStore.pendingSave), so the
  // clear cannot race it and the next mount's fetch waits for the write.
  flush()
  notesStore.clearContent()
  window.removeEventListener('editor:focus', onEditorFocusRequest)
  window.removeEventListener('beforeunload', onBeforeUnload)
  window.removeEventListener('keydown', onViewKeydown)
})

// --- Tree panel dialog requests ---

function hasChildren(item) {
  return (item.children ?? []).length > 0
}

function onRequestDelete({ item }) {
  pendingTrash.value = item
}

function confirmTrash() {
  notesStore.trash(pendingTrash.value.noteId)
  pendingTrash.value = null
}

function onRequestPurge({ item }) {
  pendingPurge.value = item
}

function confirmPurge() {
  notesStore.purge(pendingPurge.value.noteId)
  pendingPurge.value = null
}

function onRequestRestore({ item, nested }) {
  if (nested) {
    restoreTarget.value = item
  } else {
    notesStore.restore(item.noteId)
  }
}

function restoreAlone() {
  notesStore.restore(restoreTarget.value.noteId)
  restoreTarget.value = null
}

function restoreWithParent() {
  notesStore.restore(restoreTarget.value.noteId, { withAncestors: true })
  restoreTarget.value = null
}

function onRequestTemplate({ parentId }) {
  templateDialog.value = { open: true, parentId }
}

function onTemplateCreated(rootId) {
  if (rootId) notesStore.select(rootId)
}

// --- Toolbar ---

function runToolbar(name) {
  editorRef.value?.[name]?.()
}

// The pending autosave is cancelled first: it is the later write under LWW, so it
// would overwrite the restored text the moment it fired. The store's re-select then
// swaps the buffer through the currentNote watch below (decisions.md).
async function onRestoreVersion(version) {
  if (readonly.value || !editingNoteId.value || !version) return
  clearTimeout(saveTimer)
  dirty.value = false
  await notesStore.restoreVersion(version.id)
}

function toggleTool(name) {
  notesStore.toolOpen = notesStore.toolOpen === name ? null : name
  // History carries every retained version's payload, so it is fetched on open and
  // dropped on close rather than riding along with the note (decisions.md).
  if (notesStore.toolOpen === 'history') notesStore.loadHistory()
  else notesStore.clearHistory()
}

// Switching notes must not leave the previous note's versions on screen, and every
// save appends one - so track the summary's updatedAt, which is what saveBody and
// an incoming agent edit both touch (currentNote is not re-fetched on save).
watch(
  () => [notesStore.selectedId, notesStore.byId.get(notesStore.selectedId)?.updatedAt],
  () => {
    if (notesStore.toolOpen === 'history') notesStore.loadHistory()
  })

onMounted(() => {
  if (!notesStore.loaded) notesStore.load()
  // Everything for the selected note - body, attachments, backlinks - is fetched
  // fresh on every mount; the store carries no content across routes (decisions.md).
  // select() awaits any save still in flight from the unmount flush.
  if (notesStore.selectedId) notesStore.select(notesStore.selectedId)
  // History is not part of getNote, so the reopened panel loads its own data.
  if (notesStore.toolOpen === 'history') notesStore.loadHistory()
})
</script>

<template>
  <div class="flex-1 flex min-h-0">
    <SplitterGroup direction="horizontal" auto-save-id="lorestead-notes-layout">
      <SplitterPanel :default-size="22" :min-size="14" class="border-r border-border bg-surface">
        <NotesTreePanel ref="treePanel" @request-delete="onRequestDelete" @request-purge="onRequestPurge"
          @request-restore="onRequestRestore" @request-template="onRequestTemplate" />
      </SplitterPanel>
      <SplitterResizeHandle class="w-px bg-transparent hover:bg-accent/50 transition-colors" />

      <SplitterPanel :min-size="30">
        <div class="h-full flex min-h-0">
          <div class="flex-1 min-w-0 flex flex-col min-h-0">
            <EmptyState v-if="!currentNote" class="h-full">
              Select a note, or create one with the + button in the tree
            </EmptyState>

            <div v-else class="h-full flex flex-col min-h-0">
              <div class="flex items-center gap-0.5 px-2 h-10 shrink-0 border-b border-border">
                <HoverTip v-for="action in TOOLBAR_ACTIONS" :key="action.name" :text="action.title"
                  :hotkey="action.hotkey" side="bottom" wrap>
                  <Button variant="ghost" size="icon" :disabled="readonly" @click="runToolbar(action.name)">
                    <component :is="action.icon" class="size-4" />
                  </Button>
                </HoverTip>
                <div class="flex-1" />
                <span v-if="readonly"
                  class="text-xs text-on-surface-muted border border-border rounded px-1.5 py-0.5 mr-1">
                  In Trash - read-only
                </span>
                <HoverTip :text="readonly ? 'Note is in the Trash' : 'Export note'" :hotkey="EXPORT_KEY"
                  side="bottom" wrap>
                  <Button variant="ghost" size="icon" :disabled="readonly" @click="exportNote(currentNote.id)">
                    <i-lucide-folder-output class="size-4" />
                  </Button>
                </HoverTip>
                <HoverTip text="Toggle preview" :hotkey="PREVIEW_KEY" side="bottom">
                  <Button variant="ghost" size="icon" :active="notesStore.previewOpen"
                    @click="notesStore.previewOpen = !notesStore.previewOpen">
                    <i-lucide-columns-2 class="size-4" />
                  </Button>
                </HoverTip>
              </div>

              <!-- Its own auto-save-id, like the two splitters around it: the
                   editor/preview ratio is a size, so it outlives a launch even
                   though whether the preview is open does not. Reka keys the
                   saved layout by the panel set, so the closed and open shapes
                   are stored separately and cannot overwrite each other. -->
              <SplitterGroup direction="horizontal" class="flex-1 min-h-0" auto-save-id="lorestead-notes-preview">
                <SplitterPanel :min-size="25">
                  <MarkdownEditor ref="editorRef" :model-value="body" :readonly="readonly"
                    :attachments="notesStore.currentAttachments" :document-key="editingNoteId ?? ''" remember-cursor
                    @update:model-value="onBodyChange" @save="flush" @scroll="onEditorScroll"
                    @keydown.esc="onEditorEscape" />
                </SplitterPanel>
                <template v-if="notesStore.previewOpen">
                  <SplitterResizeHandle class="w-px bg-border hover:bg-accent/50 transition-colors" />
                  <SplitterPanel :default-size="50" :min-size="20" class="bg-surface">
                    <div ref="previewScroller" class="h-full overflow-y-auto p-4" @scroll="onPreviewScroll">
                      <MarkdownPreview :markdown="body" :editable="!readonly" @update:markdown="onBodyChange" />
                    </div>
                  </SplitterPanel>
                </template>
              </SplitterGroup>

              <div
                class="grid grid-cols-3 items-center px-3 h-7 shrink-0 border-t border-border text-xs text-on-surface-muted">
                <span>{{ wordCount }} {{ wordCount === 1 ? 'word' : 'words' }}</span>
                <span class="text-center truncate">{{ modifiedLabel }}</span>
                <span class="text-right">{{ readonly ? 'Read-only' : dirty ? 'Unsaved' : 'Saved' }}</span>
              </div>
            </div>
          </div>

          <!-- One shell for all three tools: switching keeps the panel's width and
               crossfades the content instead of collapsing and re-expanding. -->
          <ToolPanelShell :open="notesStore.toolOpen !== null" :content-key="notesStore.toolOpen">
            <AttachmentsPanel v-if="notesStore.toolOpen === 'attachments'" />
            <BacklinksPanel v-else-if="notesStore.toolOpen === 'backlinks'" />
            <HistoryPanel v-else-if="notesStore.toolOpen === 'history'" :current-body="body" :readonly="readonly"
              @restore="onRestoreVersion" />
          </ToolPanelShell>

          <div class="w-11 shrink-0 border-l border-border bg-surface flex flex-col items-center py-2 gap-2.5">
            <HoverTip text="Attachments" side="left">
              <Button variant="ghost" size="icon" class="relative" :active="notesStore.toolOpen === 'attachments'"
                @click="toggleTool('attachments')">
                <i-lucide-paperclip class="size-4" />
                <span v-if="notesStore.currentAttachments.length > 0" :class="RAIL_BADGE_CLASS">
                  {{ notesStore.currentAttachments.length }}
                </span>
              </Button>
            </HoverTip>
            <HoverTip text="Backlinks" side="left">
              <Button variant="ghost" size="icon" class="relative" :active="notesStore.toolOpen === 'backlinks'"
                @click="toggleTool('backlinks')">
                <i-lucide-link class="size-4" />
                <span v-if="notesStore.currentBacklinks.length > 0" :class="RAIL_BADGE_CLASS">
                  {{ notesStore.currentBacklinks.length }}
                </span>
              </Button>
            </HoverTip>
            <HoverTip text="History" side="left">
              <Button variant="ghost" size="icon" :active="notesStore.toolOpen === 'history'" @click="toggleTool('history')">
                <i-lucide-history class="size-4" />
              </Button>
            </HoverTip>
          </div>
        </div>
      </SplitterPanel>
    </SplitterGroup>

    <!-- Dialogs -->
    <ConfirmDialog :open="pendingTrash !== null" title="Move to Trash?" :message="pendingTrash && hasChildren(pendingTrash)
      ? `&quot;${pendingTrash.label}&quot; and all of its child notes will move to Trash.`
      : `&quot;${pendingTrash?.label}&quot; will move to Trash.`" confirm-label="Delete"
      @update:open="(v) => { if (!v) pendingTrash = null }" @confirm="confirmTrash" />

    <ConfirmDialog :open="pendingPurge !== null" title="Delete permanently?" :message="pendingPurge && hasChildren(pendingPurge)
      ? `&quot;${pendingPurge.label}&quot; and all of its child notes will be permanently deleted. This cannot be undone.`
      : `&quot;${pendingPurge?.label}&quot; will be permanently deleted. This cannot be undone.`"
      confirm-label="Delete permanently" @update:open="(v) => { if (!v) pendingPurge = null }"
      @confirm="confirmPurge" />

    <DialogRoot :open="restoreTarget !== null" @update:open="(v) => { if (!v) restoreTarget = null }">
      <DialogPortal>
        <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
        <DialogContent
          class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-sm rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade">
          <DialogTitle class="font-semibold mb-2">Restore note</DialogTitle>
          <DialogDescription class="text-sm text-on-surface-muted mb-5">
            This note's parent is still in the Trash. Restore it alone at the root level, or bring its parent back too.
          </DialogDescription>
          <div class="flex justify-end gap-2">
            <Button variant="outline" @click="restoreTarget = null">Cancel</Button>
            <Button variant="outline" @click="restoreAlone">Restore</Button>
            <Button variant="primary" @click="restoreWithParent">Restore with parent</Button>
          </div>
        </DialogContent>
      </DialogPortal>
    </DialogRoot>

    <NewFromTemplateDialog :open="templateDialog.open" :parent-id="templateDialog.parentId"
      @update:open="(v) => (templateDialog = { ...templateDialog, open: v })" @created="onTemplateCreated" />
  </div>
</template>
