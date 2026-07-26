There are no tags in SylvaNote. That is a deliberate trade: tags decay into a filing system you have to maintain, so search and links carry the weight instead.

## Search

`Ctrl+K` opens search from anywhere. It covers note titles and bodies, task titles and bodies, board and list names, and settings entries. Each result shows where it lives - `Notes > Projects > Ideas`, `Board > List > Task` - with the matched text highlighted. Trashed and template notes are included, and their breadcrumb says so.

The box above the note tree is a narrower tool: it filters the tree in place, by title and by body text, so you can see matches in context instead of as a list.

## Links

Type `[[` in any note or task body. A list of note titles appears, along with the current item's attachments. Pick a note and you get a link; pick an attachment and you get an embed. Dragging a note out of the tree and into the editor inserts the same link.

Links are stored by id, not by title:

```markdown
[Editor & Markdown](note://019b76da-a800-7000-8000-000000000002)
```

Which means renaming a note never breaks a link to it. The visible text is just text - if you rename the target, the link still works and you can update the wording whenever you feel like it.

## Backlinks

The backlinks tool on the far right lists everything pointing at the open note: other notes that mention it, and tasks that carry it in their linked notes field. Each card shows the surrounding sentence, so you can tell why the link is there before you follow it.

Open [Editor & Markdown](note://019b76da-a800-7000-8000-000000000002) and check its backlinks - this note and [Getting Started](note://019b76da-a800-7000-8000-000000000001) both link to it, and so do a couple of cards on the Learn SylvaNote board.
