<script setup>
import { ref, computed, watch, nextTick } from 'vue'
import MarkdownIt from 'markdown-it'
import taskLists from 'markdown-it-task-lists'
import footnote from 'markdown-it-footnote'
import mark from 'markdown-it-mark'
import underline from 'markdown-it-underline'
import hljs from 'highlight.js/lib/common'
import AttachmentPreviewDialog from './AttachmentPreviewDialog.vue'
import { useSettingsStore } from '../stores/settingsStore.js'
import { useNotesStore } from '../stores/notesStore.js'

const props = defineProps({
  markdown: { type: String, default: '' },
})

const settingsStore = useSettingsStore()
const notesStore = useNotesStore()
const container = ref(null)
// The dialog lives here so attachment links preview in every surface that
// renders markdown; the link only knows the id - the dialog resolves the rest.
const previewId = ref(null)

const md = computed(() => {
  const editor = settingsStore.editor
  const instance = new MarkdownIt({
    html: false,
    linkify: !!editor.mdAutolinks,
    highlight: editor.mdCodeHighlighting
      ? (code, lang) => {
        if (lang && hljs.getLanguage(lang)) {
          try {
            return hljs.highlight(code, { language: lang }).value
          } catch {
            // Fall through to unhighlighted output below.
          }
        }
        return ''
      }
      : null,
  })
  if (!editor.mdTables) instance.disable('table')
  if (!editor.mdStrikethrough) instance.disable('strikethrough')
  if (editor.mdTaskLists) instance.use(taskLists, { enabled: false })
  if (editor.mdFootnotes) instance.use(footnote)
  if (editor.mdHighlight) instance.use(mark)
  instance.use(underline)
  return instance
})

const html = computed(() => md.value.render(props.markdown || ''))

function noteIdOf(href) {
  return href.slice('note://'.length).toLowerCase()
}

// attachment:// sources resolve to cached object URLs; note:// links are classified
// against the loaded note index (decisions.md) - no lookup call, so no unresolved
// frame. Re-runs when summaries arrive, which is why it only sets attributes:
// clicks are delegated below and cannot double-bind.
watch([html, container, () => notesStore.summaries], async () => {
  await nextTick()
  const root = container.value
  if (!root) return

  for (const img of root.querySelectorAll('img[src^="attachment://"]')) {
    const id = img.getAttribute('src').slice('attachment://'.length)
    notesStore.getAttachmentUrl(id).then((url) => { img.src = url }).catch(() => { })
  }

  const noteLinks = root.querySelectorAll('a[href^="note://"]')
  // A task body can render note links before Notes has ever been visited.
  if (noteLinks.length > 0 && !notesStore.loaded) notesStore.load()

  for (const link of noteLinks) {
    // Trashed targets stay styled as normal links - they still navigate, and the
    // read-only editor says where you landed. Purged and never-existed are
    // indistinguishable locally, so both render broken.
    const missing = !notesStore.byId.get(noteIdOf(link.getAttribute('href')))
    link.classList.toggle('broken-link', missing)
    if (missing) {
      link.setAttribute('title', 'This note no longer exists')
    } else {
      link.removeAttribute('title')
    }
  }
}, { immediate: true })

// Delegated: survives every re-render without stacking listeners on the same node.
function onPreviewClick(event) {
  const anchor = event.target.closest?.('a[href^="attachment://"], a[href^="note://"]')
  if (!anchor) return

  // stopPropagation: in the task dialog's reading mode a container click enters
  // edit mode - following a link must not do that too.
  event.preventDefault()
  event.stopPropagation()

  const href = anchor.getAttribute('href')
  if (href.startsWith('attachment://')) {
    previewId.value = href.slice('attachment://'.length)
  } else {
    const id = noteIdOf(href)
    // A broken link is inert; preventDefault above is the whole behaviour.
    if (notesStore.byId.get(id)) {
      // Routing and closing whatever surface we are in belong to the hosts, not to
      // a markdown renderer - App.vue navigates, the task dialog closes itself.
      window.dispatchEvent(new CustomEvent('note:navigate', { detail: { id } }))
    }
  }
}
</script>

<template>
  <div>
    <div ref="container" class="markdown-preview text-sm leading-relaxed max-w-none" v-html="html"
      @click="onPreviewClick" />
    <AttachmentPreviewDialog :open="previewId !== null" :attachment="previewId ? { id: previewId } : null"
      @update:open="(v) => { if (!v) previewId = null }" />
  </div>
</template>
