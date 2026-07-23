<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { draggable as makeDraggable } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import IconImage from '~icons/lucide/image'
import IconFileText from '~icons/lucide/file-text'
import IconFileArchive from '~icons/lucide/file-archive'
import IconFile from '~icons/lucide/file'

const props = defineProps({
  attachment: { type: Object, required: true },
})

const emit = defineEmits(['rename', 'delete'])

const card = ref(null)
const editing = ref(false)
const editInput = ref(null)
let dragCleanup = null

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
    class="group flex items-center gap-2.5 rounded-md border border-border bg-surface-alt px-2.5 py-2 cursor-grab">
    <component :is="typeIcon" class="size-4 shrink-0 text-on-surface-muted" />
    <div class="flex-1 min-w-0">
      <input v-if="editing" ref="editInput" :value="attachment.filename"
        class="w-full bg-transparent text-sm border-b border-accent outline-none"
        @keydown.enter="commitRename" @keydown.esc="cancelRename" @blur="commitRename" @click.stop />
      <button v-else class="block w-full text-left text-sm truncate hover:text-accent" :title="attachment.filename"
        @dblclick="startRename">{{ attachment.filename }}</button>
      <div class="text-xs text-on-surface-muted">{{ sizeLabel }}</div>
    </div>
    <button
      class="opacity-0 group-hover:opacity-100 shrink-0 text-on-surface-muted hover:text-red-500 transition-opacity"
      title="Delete attachment" @click.stop="emit('delete')">
      <i-lucide-trash-2 class="size-4" />
    </button>
  </div>
</template>
