import { invoke } from './invoke.js'

export async function getNotes() {
  return invoke('getNotes')
}

export async function getNote(request) {
  return invoke('getNote', { request })
}

export async function getNoteHistory(request) {
  return invoke('getNoteHistory', { request })
}

export async function restoreNoteVersion(request) {
  return invoke('restoreNoteVersion', { request })
}

export async function createNote(request) {
  return invoke('createNote', { request })
}

export async function saveNote(request) {
  return invoke('saveNote', { request })
}

export async function renameNote(request) {
  return invoke('renameNote', { request })
}

export async function moveNote(request) {
  return invoke('moveNote', { request })
}

export async function trashNote(request) {
  return invoke('trashNote', { request })
}

export async function restoreNote(request) {
  return invoke('restoreNote', { request })
}

export async function restoreNoteAt(request) {
  return invoke('restoreNoteAt', { request })
}

export async function purgeNote(request) {
  return invoke('purgeNote', { request })
}

export async function createFromTemplate(request) {
  return invoke('createFromTemplate', { request })
}

export async function searchNotes(request) {
  return invoke('searchNotes', { request })
}
