import { invoke } from './invoke.js'

export async function getBoards() {
  return invoke('getBoards')
}

export async function createBoard(request) {
  return invoke('createBoard', { request })
}

export async function renameBoard(request) {
  return invoke('renameBoard', { request })
}

export async function moveBoard(request) {
  return invoke('moveBoard', { request })
}

export async function deleteBoard(request) {
  return invoke('deleteBoard', { request })
}

export async function getBoard(request) {
  return invoke('getBoard', { request })
}

export async function createColumn(request) {
  return invoke('createColumn', { request })
}

export async function renameColumn(request) {
  return invoke('renameColumn', { request })
}

export async function moveColumn(request) {
  return invoke('moveColumn', { request })
}

export async function deleteColumn(request) {
  return invoke('deleteColumn', { request })
}

export async function createTask(request) {
  return invoke('createTask', { request })
}

export async function getTask(request) {
  return invoke('getTask', { request })
}

export async function saveTask(request) {
  return invoke('saveTask', { request })
}

export async function moveTask(request) {
  return invoke('moveTask', { request })
}

export async function deleteTask(request) {
  return invoke('deleteTask', { request })
}

export async function searchTasks(request) {
  return invoke('searchTasks', { request })
}

export async function searchBoards(request) {
  return invoke('searchBoards', { request })
}
