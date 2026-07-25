<script setup>
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { draggable as makeDraggable } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import IconImage from '~icons/lucide/image'
import IconFileText from '~icons/lucide/file-text'
import IconFileArchive from '~icons/lucide/file-archive'
import IconFile from '~icons/lucide/file'
import { useNotesStore } from '../stores/notesStore.js'
import * as attachmentService from '../services/attachmentService.js'

const props = defineProps({
  attachment: { type: Object, required: true },
  readonly: { type: Boolean, default: false },
})

const emit = defineEmits(['rename', 'delete', 'preview'])

const notesStore = useNotesStore()
const card = ref(null)
const editing = ref(false)
const editInput = ref(null)
const thumbnailUrl = ref(null)
let dragCleanup = null

// Only the small thumbnail crosses the bridge for the card; images without one
// (synced from elsewhere) fall back to the type icon until first full view.
// thumbnailVersion is in the source so a backfill (preview of an image that arrived
// from sync or MCP without one) repaints the card instead of waiting for a remount.
watch(() => [props.attachment.id, notesStore.thumbnailVersion], async ([id]) => {
  thumbnailUrl.value = null
  if ((props.attachment.mimeType || '').startsWith('image/')) {
    const url = await notesStore.getAttachmentThumbnailUrl(id)
    if (props.attachment.id === id) {
      thumbnailUrl.value = url
    }
  }
}, { immediate: true })

const typeIcon = computed(() => {
  const mime = props.attachment.mimeType || ''
  if (mime.startsWith('image/')) return IconImage
  if (mime.startsWith('text/') || mime.includes('pdf')) return IconFileText
  if (mime.includes('zip') || mime.includes('compressed')) return IconFileArchive
  return IconFile
})

const sizeLabel = computed(() => {
  const bytes = props.attachment.sizeBytes || 0
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
})

async function startRename() {
  if (props.readonly) return
  editing.value = true
  await nextTick()
  editInput.value?.focus()
  editInput.value?.select()
}

function commitRename(e) {
  const value = e.target.value.trim()
  if (editing.value && value && value !== props.attachment.filename) {
    emit('rename', value)
  }
  editing.value = false
}

function cancelRename() {
  editing.value = false
}

onMounted(() => {
  dragCleanup = makeDraggable({
    element: card.value,
    getInitialData: () => ({ attachmentId: props.attachment.id, filename: props.attachment.filename, mimeType: props.attachment.mimeType }),
    onGenerateDragPreview: ({ nativeSetDragImage }) => {
      setCustomNativeDragPreview({
        getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
        render: ({ container }) => {
          const preview = document.createElement('div')
          preview.textContent = props.attachment.filename
          preview.style.cssText = 'padding: 4px 10px; border-radius: 4px; font-size: 12px; background: var(--color-surface-alt); color: var(--color-on-surface); border: 1px solid var(--color-border); white-space: nowrap; max-width: 200px; overflow: hidden; text-overflow: ellipsis;'
          container.appendChild(preview)
        },
        nativeSetDragImage,
      })
    },
  })
})

onUnmounted(() => {
  if (dragCleanup) dragCleanup()
})
</script>

<template>
  <div ref="card"
    class="group flex items-center gap-2.5 rounded-md border border-border bg-surface-alt px-2.5 py-2 cursor-pointer"
    @click="emit('preview')">
    <div class="flex size-12 shrink-0 items-center justify-center">
      <img v-if="thumbnailUrl" :src="thumbnailUrl" :alt="attachment.filename"
        class="size-12 rounded-md object-cover" />
      <component :is="typeIcon" v-else class="size-6 text-on-surface-muted" />
    </div>
    <div class="flex-1 min-w-0">
      <!-- block: this column is not a flex row, so an inline-block input would sit
           on the text baseline and add descender space under it. -->
      <input v-if="editing" ref="editInput" :value="attachment.filename"
        class="block w-full bg-transparent text-sm border-b border-accent outline-none"
        @keydown.enter="commitRename" @keydown.esc="cancelRename" @blur="commitRename" @click.stop />
      <!-- The transparent border matches the rename input's underline, so
           entering edit mode doesn't grow the card by a pixel. -->
      <div v-else class="text-sm truncate border-b border-transparent" :title="attachment.filename"
        @dblclick="startRename">
        {{ attachment.filename }}</div>
      <div class="text-xs text-on-surface-muted">{{ sizeLabel }}</div>
    </div>
    <span class="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity shrink-0">
      <button class="p-1 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
        title="Download attachment" @click.stop="attachmentService.downloadAttachment({ id: attachment.id })">
        <i-lucide-download class="size-4" />
      </button>
      <button v-if="!readonly" class="p-1 rounded text-on-surface-muted hover:text-on-surface hover:bg-on-surface/10"
        title="Rename attachment" @click.stop="startRename">
        <i-lucide-pencil class="size-4" />
      </button>
      <button v-if="!readonly" class="p-1 rounded text-on-surface-muted hover:text-red-500" title="Delete attachment"
        @click.stop="emit('delete')">
        <i-lucide-trash-2 class="size-4" />
      </button>
    </span>
  </div>
</template>
