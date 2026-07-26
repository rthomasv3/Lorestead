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

// The notes editor and the task dialog each render their own toolbar - different
// heights, one disables on trashed notes and the other drops out of the tab order
// - but the actions are the same list, and were the same thirteen lines twice.
// `name` is the method exposed by MarkdownEditor.
export const TOOLBAR_ACTIONS = [
  { name: 'bold', title: 'Bold', icon: Bold },
  { name: 'italic', title: 'Italic', icon: Italic },
  { name: 'underline', title: 'Underline', icon: Underline },
  { name: 'strikethrough', title: 'Strikethrough', icon: Strikethrough },
  { name: 'heading', title: 'Heading', icon: Heading },
  { name: 'bulletList', title: 'Bulleted list', icon: List },
  { name: 'numberedList', title: 'Numbered list', icon: ListOrdered },
  { name: 'checkboxList', title: 'Checkbox list', icon: ListChecks },
  { name: 'link', title: 'Link', icon: Link },
  { name: 'inlineCode', title: 'Inline code', icon: Code },
  { name: 'codeBlock', title: 'Code block', icon: SquareCode },
  { name: 'quote', title: 'Quote', icon: TextQuote },
  { name: 'table', title: 'Table', icon: Table },
]
