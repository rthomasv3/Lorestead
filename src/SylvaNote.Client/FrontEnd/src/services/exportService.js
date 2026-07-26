import { invoke } from './invoke.js'

// Mirrors SylvaNote.Core.Export.ExportScope.
const SCOPE_NOTE = 0
const SCOPE_SUBTREE = 1
const SCOPE_ALL = 2

export async function exportNote(noteId) {
  return invoke('exportNotes', { request: { noteId, scope: SCOPE_NOTE } })
}

export async function exportSubtree(noteId) {
  return invoke('exportNotes', { request: { noteId, scope: SCOPE_SUBTREE } })
}

export async function exportAll() {
  return invoke('exportNotes', { request: { noteId: '', scope: SCOPE_ALL } })
}
