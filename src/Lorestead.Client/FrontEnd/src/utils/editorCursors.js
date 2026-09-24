// Multiple cursors in the markdown editor: how they are made, and the keys that
// had to move to make room for them.

import { EditorState } from '@codemirror/state'
import { EditorView, rectangularSelection } from '@codemirror/view'
import { defaultKeymap, addCursorAbove, addCursorBelow, copyLineUp, copyLineDown } from '@codemirror/commands'

export const multipleCursors = [
  // Off by default: without it every transaction collapses back to one range,
  // which is why defaultKeymap's add-cursor keys never did anything here.
  EditorState.allowMultipleSelections.of(true),
  // Alt+Click, as in VS Code, rather than CodeMirror's Ctrl+Click. Not with Shift
  // held: Shift+Alt+drag is the column select, and a press that also counted as
  // adding a range would keep the old caret alongside the new column.
  EditorView.clickAddsSelectionRange.of((event) => event.altKey && !event.shiftKey),
  // Column select on Shift+Alt+drag, VS Code's, instead of CodeMirror's plain
  // Alt+drag - which here is Alt+Click's drag, adding one more ordinary range.
  // Columns are counted in characters along each line, so a drag inside a
  // wrapped paragraph follows the line rather than the rows on screen.
  rectangularSelection({ eventFilter: (event) => event.altKey && event.shiftKey && event.button === 0 }),
]

// VS Code's Linux keys for adding cursors, used on every platform so they are
// the same everywhere. They are copy line in defaultKeymap, so copy line becomes
// a single duplicate-line key, Sublime's. Not VS Code's Linux Ctrl+Shift+Alt+
// Up/Down: stock GNOME takes those for moving windows between workspaces, and
// the editor never sees them.
export const cursorKeymap = [
  { key: 'Shift-Alt-ArrowUp', run: addCursorAbove },
  { key: 'Shift-Alt-ArrowDown', run: addCursorBelow },
  { key: 'Mod-Shift-d', run: copyLineDown },
]

// defaultKeymap without its own bindings for these commands, so each has one key
// and copy-line-up has none. Matched by command rather than key string: that
// also drops its Mod-Alt-ArrowUp/Down add-cursor keys, which would otherwise
// stay as a second way in.
const remapped = new Set([addCursorAbove, addCursorBelow, copyLineUp, copyLineDown])
export const editorDefaultKeymap = defaultKeymap.filter((binding) => !remapped.has(binding.run))
