<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { SplitterGroup, SplitterPanel, SplitterResizeHandle, DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle, DialogDescription, TooltipProvider } from 'reka-ui'
import HoverTip from '../../components/HoverTip.vue'
import NotesTreePanel from './NotesTreePanel.vue'
import AttachmentsPanel from './AttachmentsPanel.vue'
import ToolPanelShell from '../../components/ToolPanelShell.vue'
import NewFromTemplateDialog from './NewFromTemplateDialog.vue'
import MarkdownEditor from '../../components/MarkdownEditor.vue'
import MarkdownPreview from '../../components/MarkdownPreview.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import Button from '../../components/Button.vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'
import { formatTimestamp } from '../../utils/dateFormat.js'

const notesStore = useNotesStore()
const settingsStore = useSettingsStore()

const treePanel = ref(null)
const editorRef = ref(null)
const previewOpen = ref(false)
const toolOpen = ref(null)

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
let editingNoteId = null

// immediate: the store keeps the selection across route changes, so on remount the
// editor must hydrate from the already-loaded note, not wait for a change.
watch(currentNote, async (note) => {
  if (editingNoteId && editingNoteId !== note?.id) {
    await flushFor(editingNoteId)
  }
  editingNoteId = note?.id ?? null
  body.value = note?.body ?? ''
  dirty.value = false
  clearTimeout(saveTimer)
}, { immediate: true })

// An agent edit to the open note (data_version watcher) reloads it only while the
// editor is clean. Mid-edit the pending autosave is the later write under LWW, so
// the buffer already holds the version that wins - there is nothing stale to show,
// and replacing the document would throw away what is being typed.
function onNotesChanged() {
  if (!dirty.value && editingNoteId) {
    notesStore.select(editingNoteId)
  }
}

onMounted(() => window.addEventListener('notes:changed', onNotesChanged))
onUnmounted(() => window.removeEventListener('notes:changed', onNotesChanged))

function onBodyChange(value) {
  if (readonly.value || !editingNoteId) return
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
  return flushFor(editingNoteId)
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
  return formatTimestamp(iso, app.dateFormat, app.timeFormat)
})

function onEditorFocusRequest() {
  editorRef.value?.focus()
}

function onBeforeUnload() {
  // Best-effort synchronous kick-off; the bridge call itself is async.
  flush()
}

onMounted(() => {
  window.addEventListener('editor:focus', onEditorFocusRequest)
  window.addEventListener('beforeunload', onBeforeUnload)
})

onUnmounted(() => {
  flush()
  window.removeEventListener('editor:focus', onEditorFocusRequest)
  window.removeEventListener('beforeunload', onBeforeUnload)
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

const toolbarActions = [
  { name: 'bold', title: 'Bold' },
  { name: 'italic', title: 'Italic' },
  { name: 'underline', title: 'Underline' },
  { name: 'strikethrough', title: 'Strikethrough' },
  { name: 'heading', title: 'Heading' },
  { name: 'bulletList', title: 'Bulleted list' },
  { name: 'numberedList', title: 'Numbered list' },
  { name: 'checkboxList', title: 'Checkbox list' },
  { name: 'link', title: 'Link' },
  { name: 'inlineCode', title: 'Inline code' },
  { name: 'codeBlock', title: 'Code block' },
  { name: 'quote', title: 'Quote' },
  { name: 'table', title: 'Table' },
]

function runToolbar(name) {
  editorRef.value?.[name]?.()
}

function toggleTool(name) {
  toolOpen.value = toolOpen.value === name ? null : name
}

onMounted(() => {
  if (!notesStore.loaded) notesStore.load()
})
</script>

<template>
  <div class="flex-1 flex min-h-0">
    <SplitterGroup direction="horizontal" auto-save-id="sylvanote-notes-layout">
      <SplitterPanel :default-size="22" :min-size="14" class="border-r border-border bg-surface">
        <NotesTreePanel ref="treePanel" @request-delete="onRequestDelete" @request-purge="onRequestPurge"
          @request-restore="onRequestRestore" @request-template="onRequestTemplate" />
      </SplitterPanel>
      <SplitterResizeHandle class="w-px bg-transparent hover:bg-accent/50 transition-colors" />

      <SplitterPanel :min-size="30">
        <div class="h-full flex min-h-0">
          <div class="flex-1 min-w-0 flex flex-col min-h-0">
        <div v-if="!currentNote" class="h-full flex items-center justify-center p-8">
          <p class="text-on-surface-muted text-center">Select a note, or create one with the + button in the tree.</p>
        </div>

        <div v-else class="h-full flex flex-col min-h-0">
          <div class="flex items-center gap-0.5 px-2 h-10 shrink-0 border-b border-border">
            <button v-for="action in toolbarActions" :key="action.name" :title="action.title"
              class="p-1.5 rounded text-on-surface-muted hover:text-on-surface hover:bg-surface-alt disabled:opacity-40"
              :disabled="readonly" @click="runToolbar(action.name)">
              <i-lucide-bold v-if="action.name === 'bold'" class="size-4" />
              <i-lucide-italic v-else-if="action.name === 'italic'" class="size-4" />
              <i-lucide-underline v-else-if="action.name === 'underline'" class="size-4" />
              <i-lucide-strikethrough v-else-if="action.name === 'strikethrough'" class="size-4" />
              <i-lucide-heading v-else-if="action.name === 'heading'" class="size-4" />
              <i-lucide-list v-else-if="action.name === 'bulletList'" class="size-4" />
              <i-lucide-list-ordered v-else-if="action.name === 'numberedList'" class="size-4" />
              <i-lucide-list-checks v-else-if="action.name === 'checkboxList'" class="size-4" />
              <i-lucide-link v-else-if="action.name === 'link'" class="size-4" />
              <i-lucide-code v-else-if="action.name === 'inlineCode'" class="size-4" />
              <i-lucide-square-code v-else-if="action.name === 'codeBlock'" class="size-4" />
              <i-lucide-text-quote v-else-if="action.name === 'quote'" class="size-4" />
              <i-lucide-table v-else class="size-4" />
            </button>
            <div class="flex-1" />
            <span v-if="readonly" class="text-xs text-on-surface-muted border border-border rounded px-1.5 py-0.5 mr-1">
              In Trash - read-only
            </span>
            <button title="Toggle preview" class="p-1.5 rounded"
              :class="previewOpen ? 'text-accent bg-accent-soft' : 'text-on-surface-muted hover:text-on-surface hover:bg-surface-alt'"
              @click="previewOpen = !previewOpen">
              <i-lucide-columns-2 class="size-4" />
            </button>
          </div>

          <SplitterGroup direction="horizontal" class="flex-1 min-h-0">
            <SplitterPanel :min-size="25">
              <MarkdownEditor ref="editorRef" :model-value="body" :readonly="readonly"
                @update:model-value="onBodyChange" @save="flush" />
            </SplitterPanel>
            <template v-if="previewOpen">
              <SplitterResizeHandle class="w-px bg-border hover:bg-accent/50 transition-colors" />
              <SplitterPanel :default-size="50" :min-size="20" class="bg-surface">
                <div class="h-full overflow-y-auto p-4">
                  <MarkdownPreview :markdown="body" />
                </div>
              </SplitterPanel>
            </template>
          </SplitterGroup>

          <div
            class="grid grid-cols-3 items-center px-3 h-7 shrink-0 border-t border-border text-xs text-on-surface-muted">
            <span>{{ wordCount }} {{ wordCount === 1 ? 'word' : 'words' }}</span>
            <span class="text-center truncate">{{ modifiedLabel }}</span>
            <span class="text-right">{{ readonly ? 'read-only' : dirty ? 'unsaved' : 'saved' }}</span>
          </div>
        </div>
          </div>

          <ToolPanelShell :open="toolOpen === 'attachments'">
            <AttachmentsPanel />
          </ToolPanelShell>

          <TooltipProvider :delay-duration="300">
            <div class="w-11 shrink-0 border-l border-border bg-surface flex flex-col items-center py-2 gap-1">
              <HoverTip text="Attachments" side="left">
                <button class="relative p-2 rounded-md"
                  :class="toolOpen === 'attachments' ? 'text-accent bg-accent-soft' : 'text-on-surface-muted hover:text-on-surface hover:bg-surface-alt'"
                  @click="toggleTool('attachments')">
                  <i-lucide-paperclip class="size-4.5" />
                  <span v-if="notesStore.currentAttachments.length > 0"
                    class="absolute -top-0.5 -right-0.5 min-w-4 h-4 px-0.5 rounded-full bg-accent-strong text-white text-[10px] leading-4 text-center">
                    {{ notesStore.currentAttachments.length }}
                  </span>
                </button>
              </HoverTip>
            </div>
          </TooltipProvider>
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
      confirm-label="Delete Permanently" @update:open="(v) => { if (!v) pendingPurge = null }"
      @confirm="confirmPurge" />

    <DialogRoot :open="restoreTarget !== null" @update:open="(v) => { if (!v) restoreTarget = null }">
      <DialogPortal>
        <DialogOverlay class="fixed inset-0 bg-black/40 z-40" />
        <DialogContent
          class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-sm rounded-lg border border-border bg-surface-elevated p-5 shadow-xl">
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
