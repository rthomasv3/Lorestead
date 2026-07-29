<script setup>
import { ref, watch, onMounted, onUnmounted, computed, nextTick } from 'vue'
import { EditorView, keymap, lineNumbers, highlightActiveLine, placeholder, Decoration, ViewPlugin } from '@codemirror/view'
import { EditorState, EditorSelection, Compartment, RangeSetBuilder } from '@codemirror/state'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { syntaxHighlighting, defaultHighlightStyle, syntaxTree } from '@codemirror/language'
import { autocompletion, acceptCompletion } from '@codemirror/autocomplete'
import { search, SearchQuery, setSearchQuery, getSearchQuery, findNext, findPrevious, replaceNext, replaceAll } from '@codemirror/search'
import { dropTargetForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { useSettingsStore } from '../stores/settingsStore.js'
import { useNotesStore } from '../stores/notesStore.js'
import { getCursor, setCursor, flushCursors } from '../utils/cursorPositions.js'
import { toolbarKeymap } from '../utils/editorToolbar.js'
import { shortcut } from '../utils/platform.js'
import TextField from './TextField.vue'
import Button from './Button.vue'
import HoverTip from './HoverTip.vue'

const props = defineProps({
  modelValue: { type: String, default: '' },
  readonly: { type: Boolean, default: false },
  // The editing item's own attachments, for the `[[` autocomplete. Per-item
  // ownership keeps this list short, which is why it can be offered in full.
  attachments: { type: Array, default: () => [] },
  // Which document the buffer currently holds. Without it the editor cannot tell
  // "you opened something else" from "what you have open was rewritten under
  // you", and those two want opposite things from the caret.
  documentKey: { type: String, default: '' },
  // Whether this editor takes part in remembered positions. The notes editor
  // does; the task dialog does not, because the entries are garbage-collected
  // against the note index and a task id would be swept on the next load.
  rememberCursor: { type: Boolean, default: false },
})

const emit = defineEmits(['update:modelValue', 'save', 'scroll'])

const settingsStore = useSettingsStore()
const notesStore = useNotesStore()
const host = ref(null)

let view = null
let syncingFromProp = false
// The document the buffer is currently showing, so a prop change can be read as
// a switch or a rewrite. Tracked separately from the prop because both it and
// modelValue land in the same update.
let appliedKey = props.documentKey
const configurable = new Compartment()
const readonlyCompartment = new Compartment()

const editorSettings = computed(() => settingsStore.editor)

function remembering() {
  return props.rememberCursor && props.documentKey !== '' && editorSettings.value.rememberCursorPosition
}

// Clamped: the remembered offset is from a previous version of the text, and an
// agent (or another device) can have made it shorter since.
function openingAnchor(key, length) {
  let anchor = 0
  if (props.rememberCursor && key !== '' && editorSettings.value.rememberCursorPosition) {
    const stored = getCursor(key)
    anchor = stored === null ? 0 : Math.min(stored, length)
  }
  return anchor
}

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
    // Our own drop caret (see showDropCaret) - CodeMirror's dropCursor never fires
    // under pragmatic-drag-and-drop, so this is hand-drawn.
    '.cm-drop-caret': {
      position: 'absolute',
      width: '2px',
      backgroundColor: 'var(--color-accent)',
      pointerEvents: 'none',
    },
    '.cm-selectionBackground, &.cm-focused .cm-selectionBackground': {
      backgroundColor: 'color-mix(in srgb, var(--color-accent) 25%, transparent) !important',
    },
  }))
  // The `[[` popup. Must be theme, not baseTheme: base themes are deliberately
  // lower priority than built-in styling, so the autocomplete package's own
  // selection colour would win.
  extensions.push(EditorView.theme({
    '.cm-tooltip.cm-tooltip-autocomplete': {
      border: '1px solid var(--color-border)',
      borderRadius: '6px',
      backgroundColor: 'var(--color-surface-elevated)',
      boxShadow: '0 8px 24px rgb(0 0 0 / 0.18)',
      overflow: 'hidden',
    },
    '.cm-tooltip-autocomplete > ul': {
      fontFamily: 'inherit',
      maxHeight: '16em',
    },
    '.cm-tooltip-autocomplete > ul > li': {
      padding: '4px 10px',
      color: 'var(--color-on-surface)',
    },
    '.cm-tooltip-autocomplete > ul > li[aria-selected]': {
      backgroundColor: 'var(--color-accent-soft)',
      color: 'var(--color-on-surface)',
    },
    '.cm-tooltip-autocomplete > ul > completion-section': {
      padding: '3px 10px',
      color: 'var(--color-on-surface-muted)',
      borderBottom: '1px solid var(--color-border)',
      fontSize: '0.85em',
    },
    '.cm-completionDetail': {
      color: 'var(--color-on-surface-muted)',
      fontStyle: 'normal',
      fontSize: '0.85em',
      marginLeft: '0.75em',
    },
  }))
  return extensions
}

// One `[[` gesture links anything - notes and this item's attachments in a single
// list (features/links.md). The match runs to the cursor, so what you type after
// `[[` filters both sections; picking replaces the `[[` and the query with the
// finished markdown. Typing something with no match and moving on just leaves the
// `[[` as literal text.
const LINK_TRIGGER = /\[\[[^\]\n]*/

function linkCompletions(context) {
  const typed = context.matchBefore(LINK_TRIGGER)
  if (!typed || props.readonly) return null

  const query = typed.text.slice(2).trim().toLowerCase()
  const options = []

  for (const attachment of props.attachments) {
    const filename = attachment.filename || 'Attachment'
    if (query && !filename.toLowerCase().includes(query)) continue
    // Images embed so they render inline in the preview - same rule as a dragged
    // attachment card.
    const embed = (attachment.mimeType || '').startsWith('image/') ? '!' : ''
    options.push({
      label: filename,
      detail: 'attachment',
      section: 'Attachments',
      apply: `${embed}[${filename}](attachment://${attachment.id})`,
    })
  }

  // Same lookup as the task dialog's linked-notes input: active notes by title.
  for (const note of notesStore.summaries) {
    if (note.deleted) continue
    const title = note.title || 'Untitled'
    if (query && !title.toLowerCase().includes(query)) continue
    options.push({
      label: title,
      detail: 'note',
      section: 'Notes',
      apply: `[${title}](note://${note.id})`,
    })
  }

  // filter: false because the matched text starts with `[[`, which would never
  // match a bare title - the filtering above is the real one.
  return { from: typed.from, options, filter: false }
}

// The markdown parser tags *text* and _text_ as the same Emphasis node, but the
// preview (markdown-it-underline) renders underscores as underline - mirror that
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

// Overriding @codemirror/search's own yellow/cyan match colours. The class names
// are its; only the tones are ours.
const searchTheme = EditorView.theme({
  '.cm-searchMatch': {
    backgroundColor: 'color-mix(in srgb, var(--color-accent) 22%, transparent)',
    borderRadius: '2px',
  },
  // The current match has to carry itself: the editor loses focus the moment you
  // type in the find field, and without drawSelection() the browser stops
  // painting the selection that would otherwise mark it. A solid accent-strong
  // fill is the one tone the theme guarantees white text against.
  '.cm-searchMatch.cm-searchMatch-selected': {
    backgroundColor: 'var(--color-accent-strong)',
    color: 'white',
  },
})

// @codemirror/search only decorates matches while its own panel is open, and the
// find bar here is a Vue overlay instead (its panel cannot be made to float, and
// its word-labelled buttons cannot be made to look like the rest of the app). So
// the decorations are ours. Everything underneath them - the query, the search
// cursor, wrap-around, replace - is still the package's.
const matchMark = Decoration.mark({ class: 'cm-searchMatch' })
const currentMatchMark = Decoration.mark({ class: 'cm-searchMatch cm-searchMatch-selected' })

function matchDecorations(view) {
  const query = getSearchQuery(view.state)
  if (!query.valid) return Decoration.none

  const builder = new RangeSetBuilder()
  for (const range of view.visibleRanges) {
    const cursor = query.getCursor(view.state, range.from, range.to)
    while (!cursor.next().done) {
      const { from, to } = cursor.value
      const current = view.state.selection.ranges.some((r) => r.from === from && r.to === to)
      builder.add(from, to, current ? currentMatchMark : matchMark)
    }
  }
  return builder.finish()
}

const matchHighlighter = ViewPlugin.fromClass(class {
  constructor(view) {
    this.decorations = matchDecorations(view)
  }

  update(update) {
    const queried = update.transactions.some((tr) => tr.effects.some((e) => e.is(setSearchQuery)))
    if (queried || update.docChanged || update.selectionSet || update.viewportChanged) {
      this.decorations = matchDecorations(update.view)
    }
  }
}, { decorations: (plugin) => plugin.decorations })

// --- Find and replace ---

// Counting scans the whole document, which is what makes "3 of 17" mean
// anything, and is capped so a pathological note cannot stall a keystroke.
const MATCH_LIMIT = 1000

const findOpen = ref(false)
const findField = ref(null)
const findText = ref('')
const replaceText = ref('')
const matchCase = ref(false)
const useRegexp = ref(false)
const wholeWord = ref(false)
const matchCount = ref(0)
const matchIndex = ref(0)

function buildQuery() {
  return new SearchQuery({
    search: findText.value,
    replace: replaceText.value,
    caseSensitive: matchCase.value,
    regexp: useRegexp.value,
    wholeWord: wholeWord.value,
  })
}

function activeQuery() {
  const query = view ? getSearchQuery(view.state) : null
  return query?.valid ? query : null
}

function countMatches() {
  const query = activeQuery()
  if (!query) {
    matchCount.value = 0
    matchIndex.value = 0
    return
  }

  const main = view.state.selection.main
  const cursor = query.getCursor(view.state)
  let count = 0
  let index = 0
  while (count < MATCH_LIMIT && !cursor.next().done) {
    count++
    if (cursor.value.from === main.from && cursor.value.to === main.to) index = count
  }
  matchCount.value = count
  matchIndex.value = index
}

function applyQuery() {
  if (!view) return
  const query = buildQuery()
  if (!query.eq(getSearchQuery(view.state))) {
    view.dispatch({ effects: setSearchQuery.of(query) })
  }
  countMatches()
}

watch([findText, replaceText, matchCase, useRegexp, wholeWord], applyQuery)

const matchLabel = computed(() => {
  if (!findText.value) return ''
  if (useRegexp.value && !buildQuery().valid) return 'Invalid pattern'
  if (matchCount.value === 0) return 'No results'
  const total = matchCount.value >= MATCH_LIMIT ? `${MATCH_LIMIT}+` : String(matchCount.value)
  // Index 0 means the caret is not sitting on a match, which is the state you are
  // in before pressing Enter for the first time.
  return matchIndex.value > 0 ? `${matchIndex.value} of ${total}` : `${total} found`
})

function openFind() {
  if (!view) return
  const main = view.state.selection.main
  const selected = view.state.sliceDoc(main.from, main.to)
  // Seeded from the selection the way every editor does it, but a single line of
  // it: a whole paragraph in the find field is never what was meant.
  if (selected && !selected.includes('\n')) findText.value = selected
  findOpen.value = true
  applyQuery()
  nextTick(() => {
    findField.value?.focus()
    findField.value?.select()
  })
}

// An empty query is an invalid one, which is what clears the highlights. The
// terms themselves stay, so reopening picks up where it left off.
function closeFind(refocus = true) {
  findOpen.value = false
  matchCount.value = 0
  matchIndex.value = 0
  if (view) {
    view.dispatch({ effects: setSearchQuery.of(new SearchQuery({ search: '' })) })
    if (refocus) view.focus()
  }
}

function findStep(back) {
  if (!activeQuery()) return
  if (back) findPrevious(view)
  else findNext(view)
}

function runReplace(all) {
  if (props.readonly || !activeQuery()) return
  if (all) replaceAll(view)
  else replaceNext(view)
}

// Keys, rather than @codemirror/search's own keymap: its Mod-f opens the panel
// this overlay replaces.
function findKeymap() {
  const step = (back) => () => {
    findStep(back)
    return true
  }
  return [
    { key: 'Mod-f', run: () => { openFind(); return true } },
    { key: 'Mod-g', run: step(false), shift: step(true), preventDefault: true },
    { key: 'F3', run: step(false), shift: step(true), preventDefault: true },
    // Only claims the key while the bar is open, so Escape keeps its other jobs -
    // leaving the editor for the tree, dismissing the task dialog.
    { key: 'Escape', run: () => { if (!findOpen.value) return false; closeFind(); return true } },
  ]
}

function createView() {
  const anchor = openingAnchor(props.documentKey, props.modelValue.length)
  view = new EditorView({
    parent: host.value,
    state: EditorState.create({
      doc: props.modelValue,
      selection: { anchor },
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
          // Tab accepts the `[[` completion. acceptCompletion returns false when
          // no popup is open, so Tab keeps its normal behaviour the rest of the
          // time (Enter is bound by the autocomplete keymap already).
          { key: 'Tab', run: acceptCompletion },
          // Ahead of defaultKeymap so Mod-e beats the emacs-style cursorLineEnd
          // it binds on macOS. Bound here rather than by the hosts, so the task
          // dialog's editor gets the shortcuts without asking for them.
          ...toolbarKeymap(actions),
          // Ahead of defaultKeymap so Escape reaches the find bar before
          // simplifySelection takes it.
          ...findKeymap(),
          ...defaultKeymap,
          ...historyKeymap,
        ]),
        // For the query state and the search cursor behind findNext/replaceAll.
        // Its panel is never opened.
        search(),
        searchTheme,
        matchHighlighter,
        markdown(),
        autocompletion({ override: [linkCompletions], icons: false }),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        underscoreEmphasis,
        EditorView.lineWrapping,
        // placeholder('Start writing...'),
        configurable.of(settingsExtensions()),
        readonlyCompartment.of(EditorState.readOnly.of(props.readonly)),
        EditorView.updateListener.of((update) => {
          // Prop-driven doc swaps (note switch) must not echo back as edits -
          // the parent would mark the note dirty just for selecting it.
          if (update.docChanged && !syncingFromProp) {
            emit('update:modelValue', update.state.doc.toString())
          }
          // Recorded as it moves, not on save: a note you opened and scrolled
          // through without editing should still remember where you were.
          if ((update.selectionSet || update.docChanged) && !syncingFromProp && remembering()) {
            setCursor(props.documentKey, update.state.selection.main.head)
          }
          // Both the total and which match is current move under the bar: typing
          // adds and removes matches, and stepping through them changes the
          // selection.
          if (findOpen.value && (update.selectionSet || update.docChanged)) {
            countMatches()
          }
        }),
      ],
    }),
  })
  // Not domEventHandlers: those are bound to the content, and scroll does not
  // bubble up out of the element that scrolled.
  view.scrollDOM.addEventListener('scroll', onScroll)
  // A restored offset is real but off-screen until something scrolls to it, and
  // EditorState.create has no way to ask for that.
  if (anchor > 0) {
    view.dispatch({ selection: { anchor }, scrollIntoView: true })
  }
}

function onScroll() {
  emit('scroll')
}

// Where the viewport sits, how far it can travel, and where a given source line
// sits, all in the scroller's own coordinate space. lineTop has to come from here
// because only CodeMirror knows how many visual rows a wrapped line takes up.
function scrollGeometry() {
  if (!view) return null
  const scroller = view.scrollDOM
  // Block tops are measured from the top of the document, which sits below
  // scrollTop 0 by the content's padding.
  const pad = view.documentTop - scroller.getBoundingClientRect().top + scroller.scrollTop
  return {
    top: scroller.scrollTop,
    max: scroller.scrollHeight - scroller.clientHeight,
    lineTop: (line) => {
      const doc = view.state.doc
      const number = Math.min(Math.max(line + 1, 1), doc.lines)
      return view.lineBlockAt(doc.line(number).from).top + pad
    },
  }
}

function scrollTo(top) {
  if (view) view.scrollDOM.scrollTop = top
}

let dropCleanup = null
let dropCaret = null

// CodeMirror's own dropCursor() cannot be used here: pragmatic-drag-and-drop parks
// a "honey pot" div under the pointer for the whole drag, so every dragover targets
// that element and CodeMirror's contentDOM listeners never fire. Drawing the caret
// from the drop target's own onDrag is the only way to show where a drop will land.
function showDropCaret(input) {
  if (!view || props.readonly) return
  const pos = view.posAtCoords({ x: input.clientX, y: input.clientY }, false)
  const rect = pos == null ? null : view.coordsAtPos(pos)
  if (!rect) {
    hideDropCaret()
    return
  }
  if (!dropCaret) {
    dropCaret = view.scrollDOM.appendChild(document.createElement('div'))
    dropCaret.className = 'cm-drop-caret'
  }
  const outer = view.scrollDOM.getBoundingClientRect()
  dropCaret.style.left = `${rect.left - outer.left + view.scrollDOM.scrollLeft}px`
  dropCaret.style.top = `${rect.top - outer.top + view.scrollDOM.scrollTop}px`
  dropCaret.style.height = `${rect.bottom - rect.top}px`
}

function hideDropCaret() {
  if (dropCaret) {
    dropCaret.remove()
    dropCaret = null
  }
}

onMounted(() => {
  createView()
  // Attachment cards and tree notes dragged into the editor insert a link where the
  // drop caret is pointing - images use the embed form so they render inline in the
  // preview. The tree's own drop handler ignores this target (it looks for a
  // resolved zone), so a note dropped here is not also moved.
  dropCleanup = dropTargetForElements({
    element: host.value,
    canDrop: ({ source }) => !props.readonly && (!!source.data.attachmentId || !!source.data.noteId),
    onDrag: ({ location }) => showDropCaret(location.current.input),
    onDragLeave: () => hideDropCaret(),
    onDrop: ({ source, location }) => {
      hideDropCaret()
      const { attachmentId, filename, mimeType, noteId, label } = source.data
      const embed = (mimeType || '').startsWith('image/') ? '!' : ''
      const text = attachmentId
        ? `${embed}[${filename}](attachment://${attachmentId})`
        : `[${label || 'Untitled'}](note://${noteId})`
      insertAtPoint(text, location.current.input)
    },
  })
})

onUnmounted(() => {
  if (dropCleanup) dropCleanup()
  hideDropCaret()
  if (view) {
    view.scrollDOM.removeEventListener('scroll', onScroll)
    view.destroy()
  }
  // The position store writes on a debounce; a note closed inside that window
  // would otherwise lose the last move.
  flushCursors()
})

// Both props land in the same update, so they are watched as one source: which
// of them changed is the whole question.
watch(() => [props.documentKey, props.modelValue], ([key, value]) => {
  if (!view) return
  const openedAnother = key !== appliedKey
  const textChanged = value !== view.state.doc.toString()
  if (!openedAnother && !textChanged) return

  // A find bar left open across a document switch is showing counts for text that
  // is no longer there. No refocus - the caret is being placed by this same
  // update, and stealing focus would fight it.
  if (openedAnother && findOpen.value) closeFind(false)

  // Holding the caret when the open document is rewritten under you - an agent
  // edit landing on it, a version restored from history - is unconditional. The
  // setting governs reopening, not being edited around: turning it off means
  // "don't put me back where I was", never "throw me to the top mid-sentence".
  // Clamped, because the incoming text can be shorter than the old offset.
  const anchor = openedAnother
    ? openingAnchor(key, value.length)
    : Math.min(view.state.selection.main.head, value.length)

  syncingFromProp = true
  view.dispatch({
    changes: textChanged ? { from: 0, to: view.state.doc.length, insert: value } : undefined,
    selection: { anchor },
    scrollIntoView: true,
  })
  syncingFromProp = false
  appliedKey = key
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
  // changeByRange requires real SelectionRange instances - plain {anchor, head}
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

// Where the drop cursor was pointing, not where the selection happens to be - the
// selection is wherever you last typed, which is rarely where you aimed the drag.
// precise=false clamps to the nearest position, so dropping below the last line
// lands at the end instead of nowhere.
function insertAtPoint(text, input) {
  if (!view || props.readonly) return
  const at = view.posAtCoords({ x: input.clientX, y: input.clientY }, false) ?? view.state.selection.main.to
  view.dispatch({
    changes: { from: at, insert: text },
    selection: { anchor: at + text.length },
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

// One object rather than an inline defineExpose, because the key bindings run the
// same methods the toolbar buttons do - a shortcut and its button are the same
// action by construction, not by two implementations that agree today.
const actions = {
  bold: () => wrapSelection('**'),
  italic: () => wrapSelection('*'),
  // markdown-it-underline renders _underscore emphasis_ as <u>; * stays italic.
  underline: () => wrapSelection('_'),
  strikethrough: () => wrapSelection('~~'),
  // The toolbar button passes nothing and gets H2; Mod+1..6 pass the level.
  heading: (level = 2) => prefixLines(`${'#'.repeat(level)} `),
  bulletList: () => prefixLines('- '),
  numberedList: () => prefixLines('', { numbered: true }),
  checkboxList: () => prefixLines('- [ ] '),
  link: insertLink,
  inlineCode: () => wrapSelection('`'),
  codeBlock: () => insertBlock('```\ncode\n```'),
  quote: () => prefixLines('> '),
  table: () => insertBlock('| Column | Column |\n| --- | --- |\n| Cell | Cell |'),
}

defineExpose({ focus, insertAtCursor, scrollGeometry, scrollTo, ...actions })
</script>

<template>
  <div class="relative h-full min-h-0 p-2 overflow-hidden">
    <div ref="host" class="h-full [&_.cm-editor]:h-full [&_.cm-scroller]:overflow-auto" />

    <!-- Floated over the text rather than stacked above it: opening find should
         not move the line you were about to read. -->
    <div v-if="findOpen"
      class="absolute top-2 right-6.5 z-20 flex flex-col gap-1 rounded-lg border border-border bg-surface-elevated p-1.5 shadow-lg">
      <div class="flex items-center gap-1">
        <TextField ref="findField" v-model="findText" size="small" placeholder="Find" class="w-44"
          @keydown.enter.prevent="findStep($event.shiftKey)" @keydown.esc.prevent.stop="closeFind()" />
        <!-- Fixed width: the label changes on every keystroke, and a bar that
             resized with it would be its own kind of jitter. -->
        <span class="w-22 shrink-0 truncate pl-2 text-xs text-on-surface-muted">{{ matchLabel }}</span>
        <HoverTip text="Match case" side="bottom">
          <Button variant="ghost" size="icon" :active="matchCase" @click="matchCase = !matchCase">
            <i-lucide-case-sensitive class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Whole word" side="bottom">
          <Button variant="ghost" size="icon" :active="wholeWord" @click="wholeWord = !wholeWord">
            <i-lucide-whole-word class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Regular expression" side="bottom">
          <Button variant="ghost" size="icon" :active="useRegexp" @click="useRegexp = !useRegexp">
            <i-lucide-regex class="size-4" />
          </Button>
        </HoverTip>
        <!-- Enter and Shift+Enter rather than Mod+G, which also steps: the field
             is where the hand already is when the bar is open. -->
        <HoverTip text="Previous" :hotkey="shortcut('shift', 'enter')" side="bottom" wrap>
          <Button variant="ghost" size="icon" :disabled="matchCount === 0" @click="findStep(true)">
            <i-lucide-chevron-up class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Next" :hotkey="shortcut('enter')" side="bottom" wrap>
          <Button variant="ghost" size="icon" :disabled="matchCount === 0" @click="findStep(false)">
            <i-lucide-chevron-down class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Close" :hotkey="shortcut('esc')" side="bottom">
          <Button variant="ghost" size="icon" @click="closeFind()">
            <i-lucide-x class="size-4" />
          </Button>
        </HoverTip>
      </div>

      <div v-if="!readonly" class="flex items-center gap-1">
        <TextField v-model="replaceText" size="small" placeholder="Replace" class="w-44"
          @keydown.enter.prevent="runReplace(false)" @keydown.esc.prevent.stop="closeFind()" />
        <HoverTip text="Replace" side="bottom" wrap>
          <Button variant="ghost" size="icon" :disabled="matchCount === 0" @click="runReplace(false)">
            <i-lucide-replace class="size-4" />
          </Button>
        </HoverTip>
        <HoverTip text="Replace all" side="bottom" wrap>
          <Button variant="ghost" size="icon" :disabled="matchCount === 0" @click="runReplace(true)">
            <i-lucide-replace-all class="size-4" />
          </Button>
        </HoverTip>
      </div>
    </div>
  </div>
</template>
