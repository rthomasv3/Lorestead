Notes are plain markdown. What you type is what gets stored, synced, exported, and read by agents.

## Writing

The toolbar above the editor applies the usual formatting to your selection or cursor: bold, italic, strikethrough, headings, lists, checkbox lists, links, inline code, code blocks, quotes, and tables. On the right side of the toolbar are an **export** button (this note as a `.md` file) and a **preview** toggle, which opens a rendered panel beside the editor.

Saving is automatic on a short delay. `Ctrl+S` flushes immediately. The footer shows the word count on the left and the save status on the right.

## Markdown extensions

All of these are on by default and can be turned off individually in Settings under Editor:

| Extension | Example |
|---|---|
| Tables | the table you are reading |
| Task lists | `- [ ] something to do` |
| Strikethrough | `~~struck out~~` |
| Autolinks | `https://lorestead.dev` |
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

Open the attachments tool on the far right to attach files to a note - use the add button or drop files straight into the panel. Attachments belong to the note they are attached to, up to 100 MB each, and are stored in the database rather than loose on disk.

Drag an attachment card into the editor to insert a link to it. Images inserted that way render inline in the preview, which is how the logo at the top of [Getting Started](note://019b76da-a800-7000-8000-000000000001) gets there.

## Editor settings

Settings under Editor also controls the font, line numbers, the active-line highlight, spell checking, the autosave delay, and whether reopening a note returns you to where the cursor was.

## History

The history tool on the far right lists earlier versions of the open note. Each card shows how much was added or removed; opening one shows a diff against what is currently in the editor, and you can restore any version. Restoring is itself a new version, so nothing gets lost by trying it.
