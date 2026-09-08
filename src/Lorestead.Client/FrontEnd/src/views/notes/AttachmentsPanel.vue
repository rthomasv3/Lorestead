<script setup>
import { ref, computed } from 'vue'
import AttachmentCard from '../../components/AttachmentCard.vue'
import Button from '../../components/Button.vue'
import HoverTip from '../../components/HoverTip.vue'
import EmptyState from '../../components/EmptyState.vue'
import AttachmentPreviewDialog from '../../components/AttachmentPreviewDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { useFinePointer } from '../../composables/useFinePointer.js'
import { filePathsFrom, filesFromPaths } from '../../utils/attachmentFiles.js'
import { claimDrop } from '../../utils/nativeFileDrop.js'

const notesStore = useNotesStore()
// Drag-drop needs a pointer that can drag; touch gets tap wording instead.
const hasFinePointer = useFinePointer()
const readonly = computed(() => !!notesStore.currentNote?.deleted)
const dragOver = ref(false)
const pendingDelete = ref(null)
const previewAttachment = ref(null)
const fileInput = ref(null)

async function addFiles(files) {
  if (readonly.value) return
  await notesStore.addAttachmentFiles(files)
}

async function onDrop(e) {
  dragOver.value = false
  const files = [...(e.dataTransfer?.files ?? [])]
  if (files.length > 0) {
    await addFiles(files)
  } else {
    // A file manager drag carries paths and no bytes, so `files` is empty for it.
    // Read them before the handler returns and dataTransfer is emptied.
    const paths = filePathsFrom(e.dataTransfer)
    if (paths.length > 0) await addFiles(await filesFromPaths(paths))
  }
}

// On WebKit this drop never reaches the page at all - the host takes it and says
// so afterwards, so the zone has to be claimed while the drag is still overhead.
function onDragOver() {
  dragOver.value = !readonly.value
  if (!readonly.value) {
    claimDrop(async (paths) => addFiles(await filesFromPaths(paths)))
  }
}

function onPick(e) {
  addFiles([...e.target.files])
  e.target.value = ''
}
</script>

<template>
  <div class="h-full flex flex-col min-h-0">
    <div class="flex items-center justify-between px-3 h-10 shrink-0 border-b border-border">
      <span class="text-sm font-medium">Attachments</span>
      <HoverTip :text="readonly ? 'Note is in the Trash' : 'Add attachment'" side="bottom" wrap>
        <Button variant="ghost" size="icon" :disabled="readonly" @click="fileInput.click()">
          <i-lucide-plus class="size-4" />
        </Button>
      </HoverTip>
      <input ref="fileInput" type="file" multiple class="hidden" @change="onPick" />
    </div>

    <div class="flex-1 min-h-0 overflow-y-auto p-2 flex flex-col gap-1.5" :class="dragOver ? 'bg-drop-target' : ''"
      @dragover.prevent="onDragOver" @dragleave="dragOver = false" @drop.prevent="onDrop">
      <AttachmentCard v-for="attachment in notesStore.currentAttachments" :key="attachment.id" :attachment="attachment"
        :readonly="readonly" @rename="(filename) => notesStore.renameAttachment(attachment.id, filename)"
        @delete="pendingDelete = attachment" @preview="previewAttachment = attachment" />

      <EmptyState v-if="notesStore.currentAttachments.length === 0" class="flex-1" :drop-target="!readonly">
        <template v-if="readonly">This note is in the Trash</template>
        <template v-else-if="hasFinePointer">Drop files here or use + to attach. Up to 100 MB each.</template>
        <template v-else>Tap + to attach files. Up to 100 MB each.</template>
      </EmptyState>
    </div>

    <AttachmentPreviewDialog :open="previewAttachment !== null" :attachment="previewAttachment"
      @update:open="(v) => { if (!v) previewAttachment = null }" />

    <ConfirmDialog :open="pendingDelete !== null" title="Delete attachment?"
      :message="`&quot;${pendingDelete?.filename}&quot; will be removed from this note.`" confirm-label="Delete"
      @update:open="(v) => { if (!v) pendingDelete = null }"
      @confirm="notesStore.removeAttachment(pendingDelete.id); pendingDelete = null" />
  </div>
</template>
