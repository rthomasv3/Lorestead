// What Tab, Shift-Tab and Backspace do to indentation in the markdown editor.

import { indentUnit, getIndentUnit } from '@codemirror/language'
import { indentMore, indentLess } from '@codemirror/commands'

// Four spaces rather than a tab character: notes get exported, synced and read
// by other markdown tools, and a \t is rendered at whatever width each of them
// happens to pick. Also the unit Mod-] and Mod-[ shift lines by.
export const editorIndentUnit = indentUnit.of('    ')

// A list item - bullet, ordered or task. Tab anywhere in one nests it, which is
// what every other markdown editor does and what makes an outline typeable.
const LIST_ITEM = /^\s*(?:[-*+]|\d+[.)])\s/

// CodeMirror leaves Tab to the browser on purpose, so a keyboard user is never
// trapped in the editor. Escape is the way out that survives these bindings -
// it already leaves for the tree.
export const indentKeymap = [
  { key: 'Tab', run: indentOrInsertTab },
  { key: 'Shift-Tab', run: indentLess },
]

// markdown() binds Backspace at Prec.high to its own deleteMarkupBackward, which
// deletes back to the start of the enclosing construct - two columns under a
// `- ` marker, whatever the indent unit is. That disagrees with what Tab just
// inserted, so inside plain indentation this claims the key first. Everywhere
// else it declines, and markdown's markup-aware delete keeps its job: eating a
// list marker, and the space after one.
export const dedentKeymap = [
  { key: 'Backspace', run: dedentBackward },
]

function indentOrInsertTab(view) {
  const { state } = view
  if (state.readOnly) return false

  // Text is selected: the ask is to shift those lines over, not to replace them
  // with an indent.
  if (state.selection.ranges.some((range) => !range.empty)) return indentMore(view)

  const range = state.selection.main
  const line = state.doc.lineAt(range.head)
  if (LIST_ITEM.test(line.text)) return indentMore(view)
  // A cursor still in the leading whitespace is positioning the line rather than
  // writing in it.
  if (!/\S/.test(line.text.slice(0, range.head - line.from))) return indentMore(view)

  view.dispatch(state.update(state.replaceSelection(state.facet(indentUnit)), {
    scrollIntoView: true,
    userEvent: 'input',
  }))
  return true
}

function dedentBackward(view) {
  const { state } = view
  if (state.readOnly) return false

  const range = state.selection.main
  if (!range.empty) return false

  const line = state.doc.lineAt(range.head)
  const before = line.text.slice(0, range.head - line.from)
  // Spaces only: a tab is one character and deleting it is already right, and
  // anything else means the cursor is past the indentation.
  if (before.length === 0 || /[^ ]/.test(before)) return false

  // Back to the previous tab stop, so Backspace undoes a Tab exactly while the
  // indentation is on the grid, and squares it up when something else left it
  // off - a list continuation aligning under a two-column `- ` marker, say.
  const unit = getIndentUnit(state)
  const drop = before.length % unit || unit
  view.dispatch(state.update({
    changes: { from: range.head - drop, to: range.head },
    userEvent: 'delete.dedent',
  }))
  return true
}
