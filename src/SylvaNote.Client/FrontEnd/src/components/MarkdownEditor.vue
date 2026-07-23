<script setup>
import { ref, watch, onMounted, onUnmounted, computed } from 'vue'
import { EditorView, keymap, lineNumbers, highlightActiveLine, placeholder, Decoration, ViewPlugin } from '@codemirror/view'
import { EditorState, EditorSelection, Compartment, RangeSetBuilder } from '@codemirror/state'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { syntaxHighlighting, defaultHighlightStyle, syntaxTree } from '@codemirror/language'
import { dropTargetForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { useSettingsStore } from '../stores/settingsStore.js'

const props = defineProps({
  modelValue: { type: String, default: '' },
  readonly: { type: Boolean, default: false },
})

const emit = defineEmits(['update:modelValue', 'save'])

const settingsStore = useSettingsStore()
const host = ref(null)

let view = null
let syncingFromProp = false
const configurable = new Compartment()
const readonlyCompartment = new Compartment()

const editorSettings = computed(() => settingsStore.editor)

function settingsExtensions() {
  const editor = editorSettings.value
  const extensions = []
  if (editor.showLineCount) extensions.push(lineNumbers())
  if (editor.highlightActiveLine) extensions.push(highlightActiveLine())
  extensions.push(EditorView.contentAttributes.of({ spellcheck: editor.spellcheckEnabled ? 'true' : 'false' }))
  extensions.push(EditorView.theme({
    '&': { fontSize: `${editor.fontSize}px`, height: '100%' },
    '.cm-scroller': {
      fontFamily: editor.fontFamily && editor.fontFamily.trim().length > 0 ? editor.fontFamily : 'var(--font-mono)',
      lineHeight: '1.6',
    },
    '.cm-content': { caretColor: 'var(--color-accent)' },
    '&.cm-focused': { outline: 'none' },
    '.cm-gutters': {
      backgroundColor: 'transparent',
      color: 'var(--color-on-surface-muted)',
      border: 'none',
    },
    '.cm-lineNumbers .cm-gutterElement': { paddingLeft: '8px', paddingRight: '14px' },
    // !important: the italic comes from the syntax-highlight class on the same
    // span, which this mark class must override.
    '.cm-md-underline': { textDecoration: 'underline', fontStyle: 'normal !important' },
    '.cm-activeLine': { backgroundColor: 'color-mix(in srgb, var(--color-on-surface) 5%, transparent)' },
    '.cm-activeLineGutter': { backgroundColor: 'transparent', color: 'var(--color-on-surface)' },
    '.cm-cursor': { borderLeftColor: 'var(--color-on-surface)' },
    '.cm-selectionBackground, &.cm-focused .cm-selectionBackground': {
      backgroundColor: 'color-mix(in srgb, var(--color-accent) 25%, transparent) !important',
    },
  }))
  return extensions
}

// The markdown parser tags *text* and _text_ as the same Emphasis node, but the
// preview (markdown-it-underline) renders underscores as underline — mirror that
// here by checking the delimiter character and restyling underscore spans.
const underlineMark = Decoration.mark({ class: 'cm-md-underline' })

function underscoreDecorations(view) {
  const builder = new RangeSetBuilder()
  for (const { from, to } of view.visibleRanges) {
    syntaxTree(view.state).iterate({
      from,
      to,
      enter: (node) => {
        if (node.name === 'Emphasis' && view.state.sliceDoc(node.from, node.from + 1) === '_') {
          builder.add(node.from, node.to, underlineMark)
        }
      },
    })
  }
  return builder.finish()
}

const underscoreEmphasis = ViewPlugin.fromClass(class {
  constructor(view) {
    this.decorations = underscoreDecorations(view)
  }

  update(update) {
    if (update.docChanged || update.viewportChanged) {
      this.decorations = underscoreDecorations(update.view)
    }
  }
}, { decorations: (plugin) => plugin.decorations })

function createView() {
  view = new EditorView({
    parent: host.value,
    state: EditorState.create({
      doc: props.modelValue,
      extensions: [
        history(),
        keymap.of([
          {
            key: 'Mod-s',
            run: () => {
              emit('save')
              return true
            },
          },
          ...defaultKeymap,
          ...historyKeymap,
        ]),
        markdown(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        underscoreEmphasis,
        EditorView.lineWrapping,
        // placeholder('Start writing…'),
        configurable.of(settingsExtensions()),
        readonlyCompartment.of(EditorState.readOnly.of(props.readonly)),
        EditorView.updateListener.of((update) => {
          // Prop-driven doc swaps (note switch) must not echo back as edits —
          // the parent would mark the note dirty just for selecting it.
          if (update.docChanged && !syncingFromProp) {
            emit('update:modelValue', update.state.doc.toString())
          }
        }),
      ],
    }),
  })
}

let dropCleanup = null

onMounted(() => {
  createView()
  // Attachment cards dragged into the editor insert a link at the cursor —
  // images use the embed form so they render inline in the preview.
  dropCleanup = dropTargetForElements({
    element: host.value,
    canDrop: ({ source }) => !!source.data.attachmentId && !props.readonly,
    onDrop: ({ source }) => {
      const { attachmentId, filename, mimeType } = source.data
      const embed = (mimeType || '').startsWith('image/') ? '!' : ''
      insertAtCursor(`${embed}[${filename}](attachment://${attachmentId})`)
    },
  })
})

onUnmounted(() => {
  if (dropCleanup) dropCleanup()
  if (view) view.destroy()
})

watch(() => props.modelValue, (value) => {
  if (view && value !== view.state.doc.toString()) {
    syncingFromProp = true
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } })
    syncingFromProp = false
  }
})

watch(() => props.readonly, (value) => {
  if (view) view.dispatch({ effects: readonlyCompartment.reconfigure(EditorState.readOnly.of(value)) })
})

watch(editorSettings, () => {
  if (view) view.dispatch({ effects: configurable.reconfigure(settingsExtensions()) })
}, { deep: true })

function focus() {
  if (view) view.focus()
}

// --- Toolbar transactions ---

function wrapSelection(prefix, suffix = prefix) {
  if (!view || props.readonly) return
  const { state } = view
  // changeByRange requires real SelectionRange instances — plain {anchor, head}
  // objects corrupt the selection on the next transaction.
  const changes = state.changeByRange((range) => {
    const text = state.sliceDoc(range.from, range.to)
    return {
      changes: { from: range.from, to: range.to, insert: `${prefix}${text}${suffix}` },
      range: text.length === 0
        ? EditorSelection.cursor(range.from + prefix.length)
        : EditorSelection.range(range.from + prefix.length, range.to + prefix.length),
    }
  })
  view.dispatch(changes)
  view.focus()
}

function prefixLines(prefix, { numbered = false } = {}) {
  if (!view || props.readonly) return
  const { state } = view
  const range = state.selection.main
  const fromLine = state.doc.lineAt(range.from).number
  const toLine = state.doc.lineAt(range.to).number
  const changes = []
  let index = 1
  for (let n = fromLine; n <= toLine; n++) {
    const line = state.doc.line(n)
    const linePrefix = numbered ? `${index}. ` : prefix
    changes.push({ from: line.from, insert: linePrefix })
    index++
  }
  view.dispatch({ changes })
  view.focus()
}

function insertBlock(text) {
  if (!view || props.readonly) return
  const { state } = view
  const range = state.selection.main
  const line = state.doc.lineAt(range.from)
  const needsNewline = line.length > 0
  const insert = `${needsNewline ? '\n' : ''}${text}`
  view.dispatch({
    changes: { from: line.to, insert },
    selection: { anchor: line.to + insert.length },
  })
  view.focus()
}

function insertLink() {
  if (!view || props.readonly) return
  const { state } = view
  const range = state.selection.main
  const text = state.sliceDoc(range.from, range.to)
  const insert = `[${text || 'link text'}](url)`
  const urlStart = range.from + 1 + (text || 'link text').length + 2
  view.dispatch({
    changes: { from: range.from, to: range.to, insert },
    selection: { anchor: urlStart, head: urlStart + 3 },
  })
  view.focus()
}

function insertAtCursor(text) {
  if (!view || props.readonly) return
  const range = view.state.selection.main
  view.dispatch({
    changes: { from: range.to, insert: text },
    selection: { anchor: range.to + text.length },
  })
  view.focus()
}

defineExpose({
  focus,
  insertAtCursor,
  bold: () => wrapSelection('**'),
  italic: () => wrapSelection('*'),
  // markdown-it-underline renders _underscore emphasis_ as <u>; * stays italic.
  underline: () => wrapSelection('_'),
  strikethrough: () => wrapSelection('~~'),
  heading: () => prefixLines('## '),
  bulletList: () => prefixLines('- '),
  numberedList: () => prefixLines('', { numbered: true }),
  checkboxList: () => prefixLines('- [ ] '),
  link: insertLink,
  inlineCode: () => wrapSelection('`'),
  codeBlock: () => insertBlock('```\ncode\n```'),
  quote: () => prefixLines('> '),
  table: () => insertBlock('| Column | Column |\n| --- | --- |\n| Cell | Cell |'),
})
</script>

<template>
  <div ref="host" class="h-full min-h-0 p-2 overflow-hidden [&_.cm-editor]:h-full [&_.cm-scroller]:overflow-auto" />
</template>
