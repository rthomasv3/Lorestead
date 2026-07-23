<script setup>
import { ref, computed, watch, nextTick } from 'vue'
import MarkdownIt from 'markdown-it'
import taskLists from 'markdown-it-task-lists'
import footnote from 'markdown-it-footnote'
import mark from 'markdown-it-mark'
import underline from 'markdown-it-underline'
import hljs from 'highlight.js/lib/common'
import { useSettingsStore } from '../stores/settingsStore.js'
import { useNotesStore } from '../stores/notesStore.js'

const props = defineProps({
  markdown: { type: String, default: '' },
})

const settingsStore = useSettingsStore()
const notesStore = useNotesStore()
const container = ref(null)

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

// attachment:// sources resolve to cached object URLs after each render.
watch([html, container], async () => {
  await nextTick()
  const root = container.value
  if (!root) return
  for (const img of root.querySelectorAll('img[src^="attachment://"]')) {
    const id = img.getAttribute('src').slice('attachment://'.length)
    notesStore.getAttachmentUrl(id).then((url) => { img.src = url }).catch(() => { })
  }
  for (const link of root.querySelectorAll('a[href^="attachment://"]')) {
    const id = link.getAttribute('href').slice('attachment://'.length)
    link.addEventListener('click', (e) => {
      e.preventDefault()
      notesStore.getAttachmentUrl(id).then((url) => window.open(url)).catch(() => { })
    })
  }
}, { immediate: true })
</script>

<template>
  <div ref="container" class="markdown-preview text-sm leading-relaxed max-w-none" v-html="html" />
</template>
