// The markdown extension rows, keyed to their editor_settings columns.
export const MD_TOGGLES = [
  { key: 'mdTables', label: 'Tables' },
  { key: 'mdTaskLists', label: 'Task lists' },
  { key: 'mdStrikethrough', label: 'Strikethrough' },
  { key: 'mdAutolinks', label: 'Autolinks' },
  { key: 'mdFootnotes', label: 'Footnotes' },
  { key: 'mdCodeHighlighting', label: 'Code highlighting' },
  { key: 'mdHighlight', label: 'Highlight (==mark==)' },
]

function section(name, anchor, labels) {
  return labels.map((label) => ({ label, section: name, anchor }))
}

// What Ctrl+K finds in Settings (features/search.md): one row per control, in
// page order, with the heading each scrolls to. Labels are the page's own, and
// this list sits beside MD_TOGGLES so the toggles cannot be indexed under a name
// the page does not use - the two drifted apart once already, and the index also
// missed a whole section, which made it unfindable.
export const SETTINGS_INDEX = [
  ...section('Application', 'settings-application', [
    'Theme', 'Accent', 'Date format', 'Time format', 'History retention', 'Trash retention',
    'New note focus', 'New task focus', 'Check for updates', 'Auto-update',
  ]),
  ...section('Editor', 'settings-editor', [
    'Font size', 'Font family', 'Spellcheck', 'Show line count', 'Highlight active line',
    'Remember cursor position', 'Autosave debounce', 'Markdown extensions',
    ...MD_TOGGLES.map((toggle) => toggle.label),
  ]),
  ...section('Sync server', 'settings-sync', ['Status', 'Server URL', 'Token', 'Sync now']),
  ...section('About', 'settings-about', ['About']),
  ...section('Logs', 'settings-logs', ['Logs']),
]
