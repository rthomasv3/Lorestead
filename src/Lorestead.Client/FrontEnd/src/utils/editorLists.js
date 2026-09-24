// What Enter does at the end of a list, and Backspace at a list marker.

import { EditorSelection } from '@codemirror/state'
import { syntaxTree } from '@codemirror/language'
import { insertNewlineContinueMarkupCommand } from '@codemirror/lang-markdown'
import { completionStatus } from '@codemirror/autocomplete'

// markdown()'s own Enter singles out a tight two-item list: on the blank second
// item it inserts a blank line above and pushes the marker down, on the theory
// that you are making the list loose. That costs a third Enter to get out of a
// list where every other markdown editor leaves on the second, and it leaves a
// stray `- ` behind while you decide. nonTightLists false takes the other
// branch it already has - drop the marker and end the list.
const continueMarkup = insertNewlineContinueMarkupCommand({ nonTightLists: false })

// Prec.highest to get in front of markdown()'s own Prec.high Enter - which also
// puts it in front of the completion keymap, hence the guard: while the `[[`
// popup is open, Enter belongs to the completion.
export const listKeymap = [
  {
    key: 'Enter',
    run: (view) => completionStatus(view.state) !== 'active' && continueMarkup(view),
  },
  { key: 'Backspace', run: removeListMarkers },
]

// markdown()'s Backspace just after a marker deletes it on a list's first item,
// but turns any later item into an indented continuation of the one above. Each
// cursor is judged against the list as it stood, so down a column the first line
// loses its marker and the rest are left indented under nothing. So with more
// than one cursor, and every one of them just after a marker, the markers simply
// go. A single cursor keeps markdown's behaviour.
function removeListMarkers(view) {
  const { state } = view
  if (state.readOnly || state.selection.ranges.length < 2) return false

  const markers = []
  for (const range of state.selection.ranges) {
    const marker = range.empty && markerBefore(state, range.head)
    if (!marker) return false
    markers.push(marker)
  }

  // changeByRange visits the ranges in the same order as the loop above.
  let index = 0
  view.dispatch(state.update(state.changeByRange(() => {
    const { from, to } = markers[index++]
    return { changes: { from, to }, range: EditorSelection.cursor(from) }
  }), { scrollIntoView: true, userEvent: 'delete' }))
  return true
}

// Between a marker and the cursor: spaces, and a task's box if there is one. Read
// from the text because the commonmark parser has no task node - markdown()'s
// own delete recognises the box the same way.
const AFTER_MARKER = /^\s*(?:\[[ xX]\]\s*)?$/

// The marker a cursor sits just after, as the span to delete. Indentation before
// the marker stays, so a nested item keeps its place. Null anywhere else,
// including a continuation line whose marker is further up.
function markerBefore(state, pos) {
  let item = syntaxTree(state).resolveInner(pos, -1)
  while (item && item.name !== 'ListItem') item = item.parent
  const mark = item?.firstChild
  if (!mark || mark.name !== 'ListMark') return null
  if (state.doc.lineAt(mark.from).number !== state.doc.lineAt(pos).number) return null
  if (!AFTER_MARKER.test(state.sliceDoc(mark.to, pos))) return null
  return { from: mark.from, to: pos }
}
