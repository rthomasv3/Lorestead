import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getSettings, saveApplicationSettings, saveEditorSettings } from '../services/settingsService'
import { clearCursors } from '../utils/cursorPositions.js'

export const THEMES = ['system', 'light', 'parchment', 'dark']
export const ACCENTS = ['indigo', 'violet', 'blue', 'cyan', 'emerald', 'rose', 'rust', 'olive']

// Cached copy of the last-applied theme/accent, used only for the first synchronous paint -
// the DB is authoritative and overwrites it as soon as getSettings resolves.
const PAINT_HINT_KEY = 'Lorestead-appearance'

// Mirrors the seeded settings rows so the page still renders (and Settings still loads)
// when the DB is unreachable; failed saves land in the log, never popups.
const APPLICATION_DEFAULTS = {
  historyRetention: 50,
  serverUrl: '',
  theme: 'system',
  accentColor: '',
  dateFormat: 'yyyy-MM-dd',
  timeFormat: 'HH:mm',
  trashRetentionDays: 30,
  autoCheckUpdates: true,
  autoUpdate: false,
  lastUpdateCheckAt: '',
  newNoteFocus: 'title',
  newTaskFocus: 'title',
  uiScale: 100,
}

const EDITOR_DEFAULTS = {
  fontSize: 14,
  fontFamily: '',
  spellcheckEnabled: true,
  showLineCount: true,
  highlightActiveLine: true,
  autosaveDebounceMs: 1000,
  rememberCursorPosition: true,
  wordWrap: true,
  mdTables: true,
  mdTaskLists: true,
  mdStrikethrough: true,
  mdAutolinks: true,
  mdFootnotes: true,
  mdCodeHighlighting: true,
  mdHighlight: true,
}

function resolveTheme(theme) {
  return theme === 'system'
    ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : theme
}

// Interface scale bounds in percent - the backend clamps to the same range.
export const UI_SCALE_MIN = 85
export const UI_SCALE_MAX = 130

function clampScale(scale) {
  const n = Number(scale)
  return Number.isFinite(n) ? Math.min(UI_SCALE_MAX, Math.max(UI_SCALE_MIN, Math.round(n))) : 100
}

// The scale is a root variable, not a root font size: style.css multiplies it
// into the platform base (--ui-scale, which carries the phone bump).
function applyAppearance(theme, accent, uiScale) {
  const html = document.documentElement
  html.classList.remove('light', 'parchment', 'dark')
  html.classList.add(resolveTheme(THEMES.includes(theme) ? theme : 'system'))
  html.dataset.accent = ACCENTS.includes(accent) ? accent : 'indigo'
  const scale = clampScale(uiScale)
  html.style.setProperty('--ui-user-scale', String(scale / 100))
  try {
    localStorage.setItem(PAINT_HINT_KEY, JSON.stringify({ theme, accent, uiScale: scale }))
  } catch {
    // Paint hint is best-effort only.
  }
}

function toApplicationRequest(app) {
  return {
    historyRetention: app.historyRetention,
    theme: app.theme,
    accentColor: app.accentColor,
    dateFormat: app.dateFormat,
    timeFormat: app.timeFormat,
    trashRetentionDays: app.trashRetentionDays,
    autoCheckUpdates: app.autoCheckUpdates,
    autoUpdate: app.autoUpdate,
    newNoteFocus: app.newNoteFocus,
    newTaskFocus: app.newTaskFocus,
    uiScale: app.uiScale,
  }
}

function toEditorRequest(editor) {
  return {
    fontSize: editor.fontSize,
    fontFamily: editor.fontFamily,
    spellcheckEnabled: editor.spellcheckEnabled,
    showLineCount: editor.showLineCount,
    highlightActiveLine: editor.highlightActiveLine,
    autosaveDebounceMs: editor.autosaveDebounceMs,
    rememberCursorPosition: editor.rememberCursorPosition,
    wordWrap: editor.wordWrap,
    mdTables: editor.mdTables,
    mdTaskLists: editor.mdTaskLists,
    mdStrikethrough: editor.mdStrikethrough,
    mdAutolinks: editor.mdAutolinks,
    mdFootnotes: editor.mdFootnotes,
    mdCodeHighlighting: editor.mdCodeHighlighting,
    mdHighlight: editor.mdHighlight,
  }
}

export const useSettingsStore = defineStore('settings', () => {
  const application = ref({ ...APPLICATION_DEFAULTS })
  const editor = ref({ ...EDITOR_DEFAULTS })
  const loaded = ref(false)

  async function init() {
    try {
      const hint = JSON.parse(localStorage.getItem(PAINT_HINT_KEY))
      if (hint) applyAppearance(hint.theme, hint.accent, hint.uiScale)
    } catch {
      // No hint yet - first paint uses the defaults.
    }

    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (application.value.theme === 'system') {
        applyAppearance('system', application.value.accentColor, application.value.uiScale)
      }
    })

    try {
      const result = await getSettings()
      if (result?.application) application.value = result.application
      if (result?.editor) editor.value = result.editor
    } catch {
      // DB unavailable - keep defaults; the failure is already in the log.
    }
    loaded.value = true
    applyAppearance(application.value.theme, application.value.accentColor, application.value.uiScale)
  }

  // Optimistic: theme/accent/scale apply instantly; the response then re-syncs the whole row.
  async function saveApplication(patch) {
    application.value = { ...application.value, ...patch }
    applyAppearance(application.value.theme, application.value.accentColor, application.value.uiScale)
    try {
      const result = await saveApplicationSettings(toApplicationRequest(application.value))
      if (result?.application) application.value = result.application
    } catch {
      // Save failure is in the log; the optimistic value stays for this session.
    }
  }

  async function saveEditor(patch) {
    // Off means "don't remember", not "remember but ignore" - the positions go
    // with the setting.
    if (patch.rememberCursorPosition === false) {
      clearCursors()
    }
    editor.value = { ...editor.value, ...patch }
    try {
      const result = await saveEditorSettings(toEditorRequest(editor.value))
      if (result?.editor) editor.value = result.editor
    } catch {
      // Save failure is in the log; the optimistic value stays for this session.
    }
  }

  return { application, editor, loaded, init, saveApplication, saveEditor }
})
