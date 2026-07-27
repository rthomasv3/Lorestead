import Bold from '~icons/lucide/bold'
import Italic from '~icons/lucide/italic'
import Underline from '~icons/lucide/underline'
import Strikethrough from '~icons/lucide/strikethrough'
import Heading from '~icons/lucide/heading'
import List from '~icons/lucide/list'
import ListOrdered from '~icons/lucide/list-ordered'
import ListChecks from '~icons/lucide/list-checks'
import Link from '~icons/lucide/link'
import Code from '~icons/lucide/code'
import SquareCode from '~icons/lucide/square-code'
import TextQuote from '~icons/lucide/text-quote'
import Table from '~icons/lucide/table'
import { shortcut } from './platform.js'

// The notes editor and the task dialog each render their own toolbar - different
// heights, one disables on trashed notes and the other drops out of the tab order
// - but the actions are the same list, and were the same thirteen lines twice.
// `name` is the method MarkdownEditor exposes; `keys` is the shortcut in
// shortcut()'s parts, which both renders the tooltip and, transposed, binds the
// CodeMirror key - so the key shown and the key bound cannot drift apart.
const ACTIONS = [
  { name: 'bold', title: 'Bold', icon: Bold, keys: ['mod', 'B'] },
  { name: 'italic', title: 'Italic', icon: Italic, keys: ['mod', 'I'] },
  { name: 'underline', title: 'Underline', icon: Underline, keys: ['mod', 'U'] },
  { name: 'strikethrough', title: 'Strikethrough', icon: Strikethrough, keys: ['mod', 'shift', 'X'] },
  // The one button inserts an H2 while the keys pick the level, so it shows the
  // range instead of the single key that happens to match the button.
  { name: 'heading', title: 'Heading', icon: Heading, keys: null, hotkey: `${shortcut('mod', '1')}-6` },
  { name: 'bulletList', title: 'Bulleted list', icon: List, keys: ['mod', 'shift', '8'] },
  { name: 'numberedList', title: 'Numbered list', icon: ListOrdered, keys: ['mod', 'shift', '7'] },
  { name: 'checkboxList', title: 'Checkbox list', icon: ListChecks, keys: ['mod', 'shift', '9'] },
  // Shift, so the search dialog keeps plain Mod+K in every context.
  { name: 'link', title: 'Link', icon: Link, keys: ['mod', 'shift', 'K'] },
  { name: 'inlineCode', title: 'Inline code', icon: Code, keys: ['mod', 'E'] },
  { name: 'codeBlock', title: 'Code block', icon: SquareCode, keys: ['mod', 'shift', 'E'] },
  { name: 'quote', title: 'Quote', icon: TextQuote, keys: ['mod', 'shift', 'Q'] },
  // No established shortcut for table insertion anywhere, so it stays toolbar-only.
  { name: 'table', title: 'Table', icon: Table, keys: null },
]

export const TOOLBAR_ACTIONS = ACTIONS.map((action) => ({
  ...action,
  hotkey: action.hotkey ?? (action.keys ? shortcut(...action.keys) : ''),
}))

const CM_MODIFIERS = { mod: 'Mod', shift: 'Shift', alt: 'Alt' }

// CodeMirror spells its modifiers differently and wants the bare key lowercased.
// `Mod` is the same idea as shortcut()'s: Cmd on macOS, Ctrl everywhere else.
function codeMirrorKey(parts) {
  return parts.map((part) => CM_MODIFIERS[part] ?? part.toLowerCase()).join('-')
}

// Bound inside MarkdownEditor rather than by each host, so the task dialog cannot
// silently miss out. `actions` is the same method set the toolbar buttons call.
export function toolbarKeymap(actions) {
  const bindings = TOOLBAR_ACTIONS
    .filter((action) => action.keys)
    .map((action) => ({
      key: codeMirrorKey(action.keys),
      run: () => {
        actions[action.name]()
        return true
      },
    }))

  for (let level = 1; level <= 6; level++) {
    bindings.push({
      key: codeMirrorKey(['mod', String(level)]),
      run: () => {
        actions.heading(level)
        return true
      },
    })
  }

  return bindings
}
