Notes are plain markdown. What you type is what gets stored, synced, exported, and read by agents.

## Writing

The toolbar above the editor formats your selection or cursor: bold, italic, strikethrough, headings, lists, checkbox lists, links, inline code, code blocks, quotes, and tables. Its right side holds an **export** button (this note as a `.md` file) and a **preview** toggle that opens a rendered panel beside the editor.

Saves happen on their own after a short pause; `Ctrl+S` saves now. `Ctrl+F` opens find and replace within the note. The footer shows the word count on the left and the save status on the right.

## Markdown extensions

All of these start on; each has its own switch in Settings under Editor:

| Extension | Example |
|---|---|
| Tables | the table you are reading |
| Task lists | `- [ ] something to do` |
| Strikethrough | `~~struck out~~` |
| Autolinks | `https://github.com/rthomasv3/Lorestead` |
| Footnotes | `text[^1]` |
| Code highlighting | fenced blocks with a language |
| Highlight | `==marked text==` |

```csharp
// Fenced code blocks keep their syntax coloring in the preview.
public static string Greet(string name)
{
    return "Hello, " + name;
}
```

## Attachments

Open the attachments tool on the far right to attach files - use the add button or drop files into the panel. Attachments belong to their note, up to 100 MB each, and live in the database, not loose on disk.

Drag an attachment card into the editor to insert a link to it. Images linked that way render in the preview - that is how the logo at the top of [Getting Started](note://019b76da-a800-7000-8000-000000000001) gets there.

## Editor settings

The same Settings page picks the font, line numbers, the active-line highlight, spell checking, the autosave delay, and whether a note reopens where you left the cursor.

## Keyboard shortcuts

`Ctrl` is `Cmd` on a Mac. Every toolbar button also shows its keys in its tooltip.

| Keys | Action |
|---|---|
| `Ctrl+S` | Save now |
| `Ctrl+F` | Find and replace in the note |
| `Ctrl+G` / `F3` | Next match - add `Shift` for previous |
| `Ctrl+B`, `Ctrl+I`, `Ctrl+U` | Bold, italic, underline |
| `Ctrl+Shift+X` | Strikethrough |
| `Ctrl+1` - `Ctrl+6` | Heading level |
| `Ctrl+Shift+8` | Bulleted list |
| `Ctrl+Shift+7` | Numbered list |
| `Ctrl+Shift+9` | Checkbox list |
| `Ctrl+Shift+K` | Link |
| `Ctrl+E` | Inline code |
| `Ctrl+Shift+E` | Code block |
| `Ctrl+Shift+Q` | Quote |
| `Ctrl+Shift+P` | Preview on and off |
| `Ctrl+Shift+S` | Export the note |
| `Tab` | Accept an autocomplete suggestion |

## History

The history tool on the far right lists earlier versions of the open note. Each card shows how much that version added or removed; open one for a diff against what is in the editor now, and restore it if you want it back. Restoring is itself a new version, so nothing gets lost by trying it.
