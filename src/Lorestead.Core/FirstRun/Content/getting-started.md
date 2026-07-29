![Lorestead](attachment://019b76da-a800-7000-8000-0000000000a1)

Welcome to Lorestead - a self-hosted home for your notes and your work, built for you and your AI agents both.

Everything in this subtree is an ordinary note. Edit it, rename it, move it, or delete it - none of it is special. When you are done with the tour, drag the whole thing to the Trash.

## Where things are

- **Notes** is this page: the tree on the left, a markdown editor on the right, and a tool rail on the far right for attachments, backlinks, and history.
- **Boards** is kanban. The **Learn Lorestead** board is already there with a short checklist to work through.
- **Settings** holds the theme, the editor options, the sync server, and the app log.

## The tree

Hover a row for a plus icon that adds a child note. Double-click a title to rename it. Drag a note onto another to nest it, or between rows to reorder.

Right-click a note for the rest: rename, duplicate it with all of its children, export it as markdown, or send it to the Trash.

The tree works from the keyboard too: arrows move and expand, Enter opens, F2 renames.

The Trash sits at the bottom of the tree. Right-click a trashed note to restore it or delete it for good, and the Trash empties itself after a set number of days - Settings picks how many.

## The tour

- [Editor & Markdown](note://019b76da-a800-7000-8000-000000000002) - writing, formatting, preview, attachments
- [Boards & Tasks](note://019b76da-a800-7000-8000-000000000003) - kanban lists, cards, linked notes
- [Templates](note://019b76da-a800-7000-8000-000000000004) - reusable note subtrees
- [Search & Links](note://019b76da-a800-7000-8000-000000000005) - finding things, connecting notes, backlinks
- [Sync Setup](note://019b76da-a800-7000-8000-000000000006) - running your own server
- [Agents & MCP](note://019b76da-a800-7000-8000-000000000007) - giving an agent access

## Bring your notes with you

The import button above the tree reads a `.md` file, a folder of markdown, or a zip - a Joplin raw export works as is. Right-click a note first to import under it instead of at the root.

Going the other way: the button beside import exports every note, right-click exports any subtree, and the editor toolbar exports the open note.

## Try these

- [ ] Press `Ctrl+K` and search for "markdown"
- [ ] Hover this row in the tree and press the plus icon to add a child note
- [ ] Turn on the preview panel with the toolbar button on the right
- [ ] Open the Learn Lorestead board and drag a card to Done

Your notes live in a single SQLite file in the `.lorestead` folder of your home directory. Copy that folder and you have backed up everything - notes, boards, attachments, history. Nothing leaves this machine until you point the app at a server of your own.
