// Remembered caret offsets, one JSON object keyed by note id, in the webview's
// localStorage. Device-local view state, deliberately not a table or a settings
// column: that would mean a migration, repository, command, contract and tests
// for an integer, and panel widths - the closer precedent - already live here
// (decisions.md). Clearing the webview data directory loses them, which is fine
// for a convenience.
const STORAGE_KEY = 'SylvaNote-cursor-positions'

// Written on a debounce because the caret moves on every keystroke, and read
// once because localStorage is synchronous.
const WRITE_DELAY_MS = 1000

let positions = null
let writeTimer = null

function read() {
  if (positions === null) {
    try {
      positions = JSON.parse(localStorage.getItem(STORAGE_KEY)) ?? {}
    } catch {
      positions = {}
    }
  }
  return positions
}

function scheduleWrite() {
  clearTimeout(writeTimer)
  writeTimer = setTimeout(flushCursors, WRITE_DELAY_MS)
}

// Callers flush when the editor goes away, so a position set inside the debounce
// window isn't lost to a note switch or a closing window.
export function flushCursors() {
  clearTimeout(writeTimer)
  writeTimer = null
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(read()))
  } catch {
    // A convenience; a full quota is not worth surfacing.
  }
}

export function getCursor(key) {
  const value = read()[key]
  return Number.isInteger(value) ? value : null
}

export function setCursor(key, offset) {
  read()[key] = offset
  scheduleWrite()
}

// Against the loaded note index rather than an LRU guess, so an entry falls out
// at the moment the database would have cascaded the note away.
export function pruneCursors(liveKeys) {
  const live = new Set(liveKeys)
  const stored = read()
  let removed = false
  for (const key of Object.keys(stored)) {
    if (!live.has(key)) {
      delete stored[key]
      removed = true
    }
  }
  if (removed) {
    scheduleWrite()
  }
}

// Turning the setting off means "don't remember", not "remember but ignore".
export function clearCursors() {
  positions = {}
  flushCursors()
}
