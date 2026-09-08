// What Enter does at the end of a list.

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
]
