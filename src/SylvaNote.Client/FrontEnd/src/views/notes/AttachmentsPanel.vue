<script setup>
import { ref } from 'vue'
import AttachmentCard from '../../components/AttachmentCard.vue'
import AttachmentPreviewDialog from '../../components/AttachmentPreviewDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { createImageThumbnail } from '../../utils/thumbnails.js'

const notesStore = useNotesStore()
const dragOver = ref(false)
const pendingDelete = ref(null)
const previewAttachment = ref(null)
const fileInput = ref(null)

const MAX_SIZE = 100 * 1024 * 1024

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
      // Errors never toast (conventions) - an oversized file is silently skipped;
      // the limit is stated in the drop zone hint.
      continue
    }
    const dataBase64 = await readFile(file)
    const thumbnailBase64 = await createImageThumbnail(file, file.type)
    await notesStore.addAttachment({
      filename: file.name,
      mimeType: file.type || 'application/octet-stream',
      dataBase64,
      thumbnailBase64,
    })
  }
}

function onDrop(e) {
  dragOver.value = false
  addFiles([...(e.dataTransfer?.files ?? [])])
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
      <button class="text-on-surface-muted hover:text-on-surface" title="Add attachment" @click="fileInput.click()">
        <i-lucide-plus class="size-4" />
      </button>
      <input ref="fileInput" type="file" multiple class="hidden" @change="onPick" />
    </div>

    <div class="flex-1 min-h-0 overflow-y-auto p-2 flex flex-col gap-1.5" :class="dragOver ? 'bg-accent/5' : ''"
      @dragover.prevent="dragOver = true" @dragleave="dragOver = false" @drop.prevent="onDrop">
      <AttachmentCard v-for="attachment in notesStore.currentAttachments" :key="attachment.id" :attachment="attachment"
        @rename="(filename) => notesStore.renameAttachment(attachment.id, filename)"
        @delete="pendingDelete = attachment" @preview="previewAttachment = attachment" />

      <div v-if="notesStore.currentAttachments.length === 0"
        class="flex-1 flex items-center justify-center text-center text-sm text-on-surface-muted/60 rounded-md border border-dashed border-border m-1 p-4">
        Drop files here or use + to attach.<br />Up to 100 MB each.
      </div>
    </div>

    <AttachmentPreviewDialog :open="previewAttachment !== null" :attachment="previewAttachment"
      @update:open="(v) => { if (!v) previewAttachment = null }" />

    <ConfirmDialog :open="pendingDelete !== null" title="Delete attachment?"
      :message="`&quot;${pendingDelete?.filename}&quot; will be removed from this note.`" confirm-label="Delete"
      @update:open="(v) => { if (!v) pendingDelete = null }"
      @confirm="notesStore.removeAttachment(pendingDelete.id); pendingDelete = null" />
  </div>
</template>
