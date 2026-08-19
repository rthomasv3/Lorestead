<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { dropTargetForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { useRouter } from 'vue-router'
import { DialogTitle, VisuallyHidden, PopoverRoot, PopoverAnchor, PopoverPortal, PopoverContent } from 'reka-ui'
import AppDialog from '../../components/AppDialog.vue'
import MarkdownEditor from '../../components/MarkdownEditor.vue'
import MarkdownPreview from '../../components/MarkdownPreview.vue'
import AttachmentCard from '../../components/AttachmentCard.vue'
import AttachmentPreviewDialog from '../../components/AttachmentPreviewDialog.vue'
import Button from '../../components/Button.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import HoverTip from '../../components/HoverTip.vue'
import EmptyState from '../../components/EmptyState.vue'
import * as attachmentService from '../../services/attachmentService.js'
import { createImageThumbnail } from '../../utils/thumbnails.js'
import { useBoardsStore } from '../../stores/boardsStore.js'
import { useNotesStore } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'
import { formatTimestamp } from '../../utils/dateFormat.js'
import { TOOLBAR_ACTIONS } from '../../utils/editorToolbar.js'
import { shortcut } from '../../utils/platform.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  taskId: { type: String, default: null },
  // New tasks land in the dialog right after creation - focus per settings.
  isNew: { type: Boolean, default: false },
})

const emit = defineEmits(['update:open'])

const router = useRouter()
const boardsStore = useBoardsStore()
const notesStore = useNotesStore()
const settingsStore = useSettingsStore()

const task = ref(null)
const title = ref('')
const body = ref('')
const noteIds = ref([])
const attachments = ref([])
const editingBody = ref(false)
const dirty = ref(false)
const updatedAt = ref('')

const titleInput = ref(null)
const editorRef = ref(null)
const fileInput = ref(null)
const dragOver = ref(false)
const pendingDeleteAttachment = ref(null)
const previewAttachment = ref(null)

let saveTimer = null

const MAX_SIZE = 100 * 1024 * 1024

// immediate matters: a jump from another route (search result, backlink card)
// mounts BoardsView with the request already pending, so this dialog's first
// render already has open=true and there is no false->true edge to catch. The
// close branch checks `previous` so mounting with open=false stays inert.
watch(() => props.open, async (value, previous) => {
  if (value && props.taskId) {
    task.value = null
    editingBody.value = false
    dirty.value = false
    const response = await boardsStore.getTask(props.taskId)
    task.value = response.task
    title.value = response.task?.title ?? ''
    body.value = response.task?.body ?? ''
    noteIds.value = [...(response.task?.noteIds ?? [])]
    attachments.value = response.attachments ?? []
    updatedAt.value = response.task?.updatedAt ?? ''
    if (!notesStore.loaded) notesStore.load()
    await nextTick()
    resizeTitle()
    if (props.isNew) {
      if (settingsStore.application.newTaskFocus === 'body') {
        enterEdit()
      } else {
        titleInput.value?.focus()
      }
    }
  } else if (!value && previous) {
    await flush()
    boardsStore.refreshBoard()
  }
}, { immediate: true })

// An agent edit reaching the open dialog splits two ways. Attachments save through
// their own commands, so they are never part of a pending edit and always refresh.
// Title, body and links ride the save debounce below, so they refresh only while
// clean - mid-edit the pending save is the later write under LWW.
async function onBoardsChanged() {
  if (props.open && task.value) {
    const response = await boardsStore.getTask(task.value.id)
    attachments.value = response.attachments ?? []
    if (!dirty.value) {
      title.value = response.task?.title ?? ''
      body.value = response.task?.body ?? ''
      noteIds.value = [...(response.task?.noteIds ?? [])]
      updatedAt.value = response.task?.updatedAt ?? ''
      await nextTick()
      resizeTitle()
    }
  }
}

// A note:// link in the body jumps to the Notes page. Closing here rather than
// letting the route change unmount us is what runs the close path's flush, so a
// pending debounced edit is not lost on the way out.
function onNoteNavigate() {
  if (props.open) emit('update:open', false)
}

onMounted(() => {
  window.addEventListener('boards:changed', onBoardsChanged)
  window.addEventListener('note:navigate', onNoteNavigate)
})

onUnmounted(() => {
  window.removeEventListener('boards:changed', onBoardsChanged)
  window.removeEventListener('note:navigate', onNoteNavigate)
})

// --- Save (title + body + links ride one debounce) ---

function markDirty() {
  dirty.value = true
  clearTimeout(saveTimer)
  saveTimer = setTimeout(flush, settingsStore.editor.autosaveDebounceMs || 1000)
}

async function flush() {
  if (dirty.value && task.value) {
    dirty.value = false
    clearTimeout(saveTimer)
    const response = await boardsStore.saveTask({
      id: task.value.id,
      title: title.value,
      body: body.value,
      noteIds: noteIds.value,
    })
    updatedAt.value = response.updatedAt
  }
}

function onTitleInput(e) {
  title.value = e.target.value
  resizeTitle()
  markDirty()
}

// The title is a wrapping textarea (an input can only cut long text off); it
// grows to fit its content and Enter blurs instead of adding lines.
function resizeTitle() {
  const el = titleInput.value
  if (el) {
    el.style.height = 'auto'
    el.style.height = `${el.scrollHeight}px`
  }
}

function onBodyChange(value) {
  body.value = value
  markDirty()
}

// --- Body render/edit modes ---

async function enterEdit() {
  editingBody.value = true
  await nextTick()
  editorRef.value?.focus()
}

// The one way out of edit mode: the toolbar's save button, Ctrl+S, and blurring
// all land here. Leaving the editor is what "saved" means in this dialog - the
// button that flushed but stayed in edit mode would look like it had done
// nothing, since the save status lives in the notes footer, not here.
function leaveEdit() {
  editingBody.value = false
  flush()
}

// Blur returns to reading mode (ui/pages/task-edit.md) - but not when focus just
// moved to the toolbar, or every button click would close the editor.
function onEditorFocusOut(e) {
  if (!e.currentTarget.contains(e.relatedTarget)) {
    leaveEdit()
  }
}

// A scrollbar press must scroll, not open the editor. clientWidth/clientHeight
// exclude scrollbars, so a press in a gutter lands outside the pressed
// element's client box - checked on e.target so a code block's own horizontal
// scrollbar is covered too. Keying off where the press started (not the click
// position) handles dragging the thumb and releasing over the content.
let scrollbarPress = false

function onReadingMousedown(e) {
  scrollbarPress = e.offsetX >= e.target.clientWidth || e.offsetY >= e.target.clientHeight
}

function onReadingClick() {
  if (!scrollbarPress) {
    enterEdit()
  }
  scrollbarPress = false
}

// A scrollbar press also focuses the div (it is a tab stop), so focus alone
// can't mean "start editing". :focus-visible separates the two: Chromium
// applies it for keyboard focus but not mouse, so Tab from the title still
// lands in the editor while a mouse press leaves the mode to the click guard.
function onReadingFocus(e) {
  if (e.target.matches(':focus-visible')) {
    enterEdit()
  }
}

// Reading mode accepts attachment-card drags too - the editor's own drop target
// only exists in edit mode, and drag-to-link should not require entering it.
// The element is behind v-if, so attachment follows the ref.
const readingArea = ref(null)
const attachDragOver = ref(false)
let readingDropCleanup = null

watch(readingArea, (element) => {
  if (readingDropCleanup) {
    readingDropCleanup()
    readingDropCleanup = null
  }
  if (element) {
    readingDropCleanup = dropTargetForElements({
      element,
      canDrop: ({ source }) => !!source.data.attachmentId,
      onDragEnter: () => (attachDragOver.value = true),
      onDragLeave: () => (attachDragOver.value = false),
      onDrop: ({ source }) => {
        attachDragOver.value = false
        const { attachmentId, filename, mimeType } = source.data
        const embed = (mimeType || '').startsWith('image/') ? '!' : ''
        const link = `${embed}[${filename}](attachment://${attachmentId})`
        body.value = body.value.trim() ? `${body.value.replace(/\s+$/, '')}\n\n${link}` : link
        markDirty()
      },
    })
  }
})

onUnmounted(() => {
  if (readingDropCleanup) readingDropCleanup()
})

function runToolbar(name) {
  editorRef.value?.[name]?.()
}

// --- Attachments ---

async function refreshAttachments() {
  const response = await boardsStore.getTask(task.value.id)
  attachments.value = response.attachments ?? []
  boardsStore.refreshTaskAttachmentCount(task.value.id, attachments.value.length)
}

function readFile(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result.split(',')[1])
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

async function addFiles(files) {
  for (const file of files) {
    if (file.size > MAX_SIZE) {
      // Errors never toast (conventions) - oversized files are silently skipped;
      // the limit is stated in the drop zone hint.
      continue
    }
    const dataBase64 = await readFile(file)
    const thumbnailBase64 = await createImageThumbnail(file, file.type)
    await attachmentService.addAttachment({
      taskId: task.value.id,
      filename: file.name,
      mimeType: file.type || 'application/octet-stream',
      dataBase64,
      thumbnailBase64,
    })
  }
  await refreshAttachments()
}

function onDrop(e) {
  dragOver.value = false
  addFiles([...(e.dataTransfer?.files ?? [])])
}

function onPick(e) {
  addFiles([...e.target.files])
  e.target.value = ''
}

async function renameAttachment(id, filename) {
  await attachmentService.renameAttachment({ id, filename })
  await refreshAttachments()
}

async function removeAttachment(id) {
  await attachmentService.deleteAttachment({ id })
  await refreshAttachments()
}

// --- Linked notes (chips + autocomplete) ---

const linkQuery = ref('')
const linkIndex = ref(0)
const linkInputFocused = ref(false)

const linkedNotes = computed(() =>
  noteIds.value
    .map((id) => notesStore.byId.get(id))
    .filter(Boolean))

const linkSuggestions = computed(() => {
  const q = linkQuery.value.trim().toLowerCase()
  if (!q) return []
  return notesStore.summaries
    .filter((s) => !s.deleted
      && !noteIds.value.includes(s.id)
      && (s.title || 'Untitled').toLowerCase().includes(q))
    .slice(0, 8)
})

function addLink(note) {
  noteIds.value = [...noteIds.value, note.id]
  linkQuery.value = ''
  linkIndex.value = 0
  markDirty()
}

function removeLink(id) {
  noteIds.value = noteIds.value.filter((n) => n !== id)
  markDirty()
}

function onLinkKeydown(e) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    linkIndex.value = Math.min(linkIndex.value + 1, linkSuggestions.value.length - 1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    linkIndex.value = Math.max(linkIndex.value - 1, 0)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const note = linkSuggestions.value[linkIndex.value]
    if (note) addLink(note)
  } else if (e.key === 'Backspace' && !linkQuery.value && noteIds.value.length > 0) {
    removeLink(noteIds.value[noteIds.value.length - 1])
  }
}

async function openLinkedNote(id) {
  await flush()
  emit('update:open', false)
  notesStore.reveal(id)
  await router.push(`/notes/${id}`)
}

// --- Footer ---

const updatedLabel = computed(() => {
  const iso = updatedAt.value || task.value?.createdAt
  const app = settingsStore.application
  return iso ? `Last updated ${formatTimestamp(iso, app.dateFormat, app.timeFormat)}` : ''
})

function onDialogKeydown(e) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
    e.preventDefault()
    flush()
  }
}
</script>

<template>
  <AppDialog :open="open" class="md:max-w-2xl md:max-h-[85vh]" @update:open="emit('update:open', $event)"
    @keydown="onDialogKeydown">
    <VisuallyHidden>
      <DialogTitle>Edit task</DialogTitle>
    </VisuallyHidden>

    <div v-if="task" class="flex items-start gap-2 px-5 pt-4 shrink-0">
      <textarea ref="titleInput" :value="title" rows="1" placeholder="Untitled task"
        class="flex-1 min-w-0 resize-none overflow-hidden bg-transparent text-lg font-semibold outline-none rounded-md border border-transparent hover:cursor-text focus:border-accent px-2 py-1"
        @input="onTitleInput" @keydown.enter.prevent="$event.target.blur()" />
      <!-- Out of the tab order so Tab from the title reaches the description
           instead of the one control that throws the dialog away. Esc is the
           keyboard close, same as every other dialog; the toolbar and save
           buttons below opt out for the same reason. -->
      <HoverTip text="Close" side="left">
        <Button variant="ghost" size="icon" class="mt-1" tabindex="-1" @click="emit('update:open', false)">
          <i-lucide-x class="size-4" />
        </Button>
      </HoverTip>
    </div>

    <div v-if="task" class="flex-1 min-h-0 overflow-y-auto px-5 pb-5 pt-3 flex flex-col gap-4">
      <div>
        <div class="text-sm font-medium text-on-surface-muted mb-1.5 ml-1">Description</div>
        <!-- Both modes render at the same fixed height so switching between
             reading and editing never resizes the dialog. -->
        <!-- A tab stop that hands straight over to the editor: Tab from the
             title puts the caret in the body, which is the whole point of the
             stop. Reading mode is never what focus rests on. -->
        <div v-if="!editingBody" ref="readingArea" tabindex="0"
          class="h-64 overflow-y-auto rounded-md border cursor-text px-2.5 py-2 outline-none"
          :class="attachDragOver ? 'border-accent bg-drop-target' : 'border-border/60 hover:border-border'"
          @mousedown="onReadingMousedown" @click="onReadingClick" @focus="onReadingFocus">
          <MarkdownPreview v-if="body.trim()" :markdown="body" editable @update:markdown="onBodyChange" />
          <p v-else class="text-sm text-on-surface-muted/60">Click to add a description...</p>
        </div>
        <div v-else class="h-64 rounded-md border border-border focus-within:border-accent flex flex-col"
          @focusout="onEditorFocusOut">
          <div class="flex items-center gap-0.5 px-1.5 h-9 shrink-0 border-b border-border flex-wrap">
            <HoverTip v-for="action in TOOLBAR_ACTIONS" :key="action.name" :text="action.title"
              :hotkey="action.hotkey" side="bottom">
              <Button variant="ghost" size="icon" tabindex="-1" @click="runToolbar(action.name)">
                <component :is="action.icon" class="size-4" />
              </Button>
            </HoverTip>
            <div class="flex-1" />
            <HoverTip text="Save" :hotkey="shortcut('mod', 'S')" side="bottom">
              <Button variant="ghost" size="icon" tabindex="-1" @click="leaveEdit">
                <i-lucide-save class="size-4" />
              </Button>
            </HoverTip>
          </div>
          <div class="flex-1 min-h-0">
            <!-- document-key without remember-cursor: an agent editing this
                 task under you shouldn't move the caret, but the position
                 store is garbage-collected against the note index, so a task
                 id would be swept on the next load. -->
            <MarkdownEditor ref="editorRef" :model-value="body" :attachments="attachments"
              :document-key="taskId ?? ''" @update:model-value="onBodyChange" @save="leaveEdit" />
          </div>
        </div>
      </div>

      <div>
        <div class="flex items-center justify-between mb-1.5">
          <span class="text-sm font-medium text-on-surface-muted ml-1">Attachments</span>
          <HoverTip text="Add attachment" side="left">
            <button class="text-on-surface-muted hover:text-on-surface" @click="fileInput.click()">
              <i-lucide-plus class="size-4" />
            </button>
          </HoverTip>
          <input ref="fileInput" type="file" multiple class="hidden" @change="onPick" />
        </div>
        <div class="flex flex-col gap-1.5 rounded-md" :class="dragOver ? 'bg-drop-target' : ''"
          @dragover.prevent="dragOver = true" @dragleave="dragOver = false" @drop.prevent="onDrop">
          <AttachmentCard v-for="attachment in attachments" :key="attachment.id" :attachment="attachment"
            @rename="(filename) => renameAttachment(attachment.id, filename)"
            @delete="pendingDeleteAttachment = attachment" @preview="previewAttachment = attachment" />
          <EmptyState v-if="attachments.length === 0" drop-target>
            Drop files here or use + to attach. Up to 100 MB each.
          </EmptyState>
        </div>
      </div>

      <div>
        <div class="text-sm font-medium text-on-surface-muted mb-1.5 ml-1">Linked notes</div>
        <!-- A popover rather than an absolute dropdown: positioned inside the
             dialog's scroll area the list extended the scrollable content, so
             opening it scrolled the dialog and cut the list off. The portal
             takes it out of that flow entirely; focus stays in the input, so
             both auto-focus hops are suppressed. -->
        <PopoverRoot :open="linkInputFocused && linkSuggestions.length > 0">
          <PopoverAnchor as-child>
            <div
              class="flex flex-wrap items-center gap-1.5 rounded-md border border-border px-2 py-1.5 min-h-9 focus-within:border-accent">
              <span v-for="note in linkedNotes" :key="note.id"
                class="flex items-center gap-1 rounded bg-accent-soft text-sm px-1.5 py-0.5">
                <button class="hover:text-accent truncate max-w-48" :title="note.title || 'Untitled'"
                  @click="openLinkedNote(note.id)">{{ note.title || 'Untitled' }}</button>
                <HoverTip text="Remove link">
                  <button class="text-on-surface-muted hover:text-on-surface" @click="removeLink(note.id)">
                    <i-lucide-x class="size-3" />
                  </button>
                </HoverTip>
              </span>
              <input v-model="linkQuery" placeholder="Link a note..."
                class="flex-1 min-w-24 bg-transparent text-sm outline-none placeholder:text-on-surface-muted/60"
                @keydown="onLinkKeydown" @focus="linkInputFocused = true" @blur="linkInputFocused = false" />
            </div>
          </PopoverAnchor>
          <PopoverPortal>
            <PopoverContent side="bottom" align="start" :side-offset="4"
              class="z-[60] w-[var(--reka-popover-trigger-width)] rounded-lg border border-border bg-surface-elevated shadow-lg p-1 max-h-48 overflow-y-auto"
              @open-auto-focus.prevent @close-auto-focus.prevent>
              <!-- Selection follows the mouse, so hovering a row selects it and
                   there is no separate hover state to paint. -->
              <button v-for="(note, index) in linkSuggestions" :key="note.id"
                class="w-full text-left rounded-md px-2.5 py-1.5 text-sm truncate"
                :class="index === linkIndex ? 'bg-accent-soft' : ''"
                @mouseenter="linkIndex = index" @mousedown.prevent="addLink(note)">
                {{ note.title || 'Untitled' }}
              </button>
            </PopoverContent>
          </PopoverPortal>
        </PopoverRoot>
      </div>
    </div>

    <div v-if="task"
      class="flex items-center justify-between px-6 h-8 shrink-0 border-t border-border text-xs text-on-surface-muted">
      <span class="truncate">{{ updatedLabel }}</span>
      <span class="shrink-0">{{ dirty ? 'Unsaved' : 'Saved' }}</span>
    </div>

    <AttachmentPreviewDialog :open="previewAttachment !== null" :attachment="previewAttachment"
      @update:open="(v) => { if (!v) previewAttachment = null }" />

    <ConfirmDialog :open="pendingDeleteAttachment !== null" title="Delete attachment?"
      :message="`&quot;${pendingDeleteAttachment?.filename}&quot; will be removed from this task.`"
      confirm-label="Delete" @update:open="(v) => { if (!v) pendingDeleteAttachment = null }"
      @confirm="removeAttachment(pendingDeleteAttachment.id); pendingDeleteAttachment = null" />
  </AppDialog>
</template>
