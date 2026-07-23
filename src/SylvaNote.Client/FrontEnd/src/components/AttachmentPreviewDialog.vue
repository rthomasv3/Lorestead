<script setup>
import { ref, computed, watch } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import Button from './Button.vue'
import * as attachmentService from '../services/attachmentService.js'
import { useNotesStore } from '../stores/notesStore.js'
import { createImageThumbnail, base64ToBlob } from '../utils/thumbnails.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  attachment: { type: Object, default: null },
})

const emit = defineEmits(['update:open'])

const notesStore = useNotesStore()

// mode: 'loading' | 'image' | 'pdf' | 'text' | 'none'
const mode = ref('loading')
const contentUrl = ref(null)
const textContent = ref('')
const textTruncated = ref(false)
// Full metadata for the open attachment - markdown links pass only { id }, so
// filename/mime may need resolving from the data call.
const meta = ref(null)
const shown = computed(() => meta.value ?? props.attachment)

const TEXT_CAP = 1024 * 1024

// Any file that decodes as UTF-8 without binary tells is previewed as text,
// regardless of extension or declared mime type.
function sniffText(bytes) {
  let text = null
  const sample = bytes.subarray(0, 8192)
  let controlCount = 0
  let binary = false
  for (const byte of sample) {
    if (byte === 0) {
      binary = true
      break
    }
    if (byte < 32 && byte !== 9 && byte !== 10 && byte !== 13) controlCount++
  }
  if (!binary && (sample.length === 0 || controlCount / sample.length < 0.05)) {
    try {
      text = new TextDecoder('utf-8', { fatal: true }).decode(bytes.subarray(0, TEXT_CAP))
    } catch {
      // Not valid UTF-8 - treated as binary below.
      text = null
    }
  }
  return text
}

watch(() => props.open, async (value) => {
  if (value && props.attachment) {
    mode.value = 'loading'
    contentUrl.value = null
    textContent.value = ''
    textTruncated.value = false
    meta.value = null

    let attachment = props.attachment
    let fetched = null
    if (!attachment.mimeType) {
      fetched = await attachmentService.getAttachmentData({ id: attachment.id })
      attachment = {
        id: attachment.id,
        filename: fetched.filename,
        mimeType: fetched.mimeType,
        sizeBytes: Math.floor((fetched.dataBase64?.length ?? 0) * 3 / 4),
      }
    }
    meta.value = attachment
    const mime = attachment.mimeType || ''

    if (mime.startsWith('image/')) {
      contentUrl.value = await notesStore.getAttachmentUrl(attachment.id)
      mode.value = 'image'
      maybeBackfillThumbnail(attachment)
    } else if (mime.includes('pdf')) {
      contentUrl.value = await notesStore.getAttachmentUrl(attachment.id)
      mode.value = 'pdf'
    } else {
      const data = fetched ?? await attachmentService.getAttachmentData({ id: attachment.id })
      const bytes = Uint8Array.from(atob(data.dataBase64 || ''), (c) => c.charCodeAt(0))
      const text = sniffText(bytes)
      if (text !== null) {
        textContent.value = text
        textTruncated.value = bytes.length > TEXT_CAP
        mode.value = 'text'
      } else {
        mode.value = 'none'
      }
    }
  }
})

// Attachments that arrived without a local thumbnail (sync, MCP) get one now
// that the full image is here anyway.
async function maybeBackfillThumbnail(attachment) {
  const existing = await notesStore.getAttachmentThumbnailUrl(attachment.id)
  if (!existing) {
    const data = await attachmentService.getAttachmentData({ id: attachment.id })
    const thumbnail = await createImageThumbnail(base64ToBlob(data.dataBase64, attachment.mimeType), attachment.mimeType)
    if (thumbnail) {
      await notesStore.storeAttachmentThumbnail(attachment.id, thumbnail)
    }
  }
}

function download() {
  attachmentService.downloadAttachment({ id: shown.value.id })
}

const sizeLabel = () => {
  const bytes = shown.value?.sizeBytes || 0
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
</script>

<template>
  <!-- Lightbox: full-screen dark backdrop, floating top bar, content centered.
       Clicking the empty backdrop closes; Esc via the dialog root. -->
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/80 z-50" />
      <DialogContent class="fixed inset-0 z-50 flex flex-col outline-none">
        <div class="flex items-center gap-1 px-4 h-12 shrink-0">
          <DialogTitle class="flex-1 min-w-0 truncate text-sm text-white/90">
            {{ shown?.filename }}
          </DialogTitle>
          <button class="p-2 rounded-md text-white/80 hover:text-white hover:bg-white/10" title="Download"
            @click="download">
            <i-lucide-download class="size-4.5" />
          </button>
          <button class="p-2 rounded-md text-white/80 hover:text-white hover:bg-white/10" title="Close"
            @click="emit('update:open', false)">
            <i-lucide-x class="size-4.5" />
          </button>
        </div>

        <div class="flex-1 min-h-0 flex items-center justify-center px-6 pb-6"
          @click.self="emit('update:open', false)">
          <template v-if="mode === 'loading'" />

          <img v-else-if="mode === 'image'" :src="contentUrl" :alt="shown?.filename"
            class="max-w-full max-h-full object-contain rounded shadow-2xl" />

          <iframe v-else-if="mode === 'pdf'" :src="contentUrl"
            class="w-full max-w-4xl h-full border-0 rounded-lg shadow-2xl bg-white" :title="shown?.filename" />

          <div v-else-if="mode === 'text'"
            class="w-full max-w-3xl max-h-full overflow-auto rounded-lg border border-border bg-surface-elevated shadow-2xl p-4">
            <pre class="text-xs font-mono whitespace-pre-wrap break-words">{{ textContent }}</pre>
            <p v-if="textTruncated" class="mt-3 text-xs text-on-surface-muted">
              Preview truncated - download the file to see the rest.
            </p>
          </div>

          <div v-else
            class="flex flex-col items-center gap-3 rounded-lg border border-border bg-surface-elevated shadow-2xl px-10 py-8 text-on-surface-muted">
            <i-lucide-file class="size-10" />
            <div class="text-sm">No preview available for this file type.</div>
            <div class="text-xs">{{ shown?.filename }} ({{ sizeLabel() }})</div>
            <Button variant="outline" @click="download">
              <i-lucide-download class="size-4" />
              Download
            </Button>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
