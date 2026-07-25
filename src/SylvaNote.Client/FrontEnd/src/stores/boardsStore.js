import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as boardService from '../services/boardService.js'

export const useBoardsStore = defineStore('boards', () => {
  const boards = ref([])
  const loaded = ref(false)
  // Selection lives here (not in the view) so it survives route changes, same as
  // the notes tree.
  const selectedBoardId = ref(null)
  const columns = ref([])
  const tasks = ref([])
  // Set by the search dialog before navigating here; BoardsView watches it and
  // opens the task edit dialog (the dialog itself lives in the view).
  const openTaskRequest = ref(null)

  const selectedBoard = computed(() =>
    boards.value.find((b) => b.id === selectedBoardId.value) ?? null)

  const tasksByColumn = computed(() => {
    const map = new Map()
    for (const task of tasks.value) {
      if (!map.has(task.columnId)) map.set(task.columnId, [])
      map.get(task.columnId).push(task)
    }
    return map
  })

  async function load() {
    const response = await boardService.getBoards()
    boards.value = response.boards ?? []
    loaded.value = true
    if (selectedBoardId.value && !boards.value.some((b) => b.id === selectedBoardId.value)) {
      selectedBoardId.value = null
      columns.value = []
      tasks.value = []
    }
  }

  async function refreshBoard() {
    if (selectedBoardId.value) {
      const id = selectedBoardId.value
      const response = await boardService.getBoard({ id })
      if (selectedBoardId.value === id) {
        columns.value = response.columns ?? []
        tasks.value = response.tasks ?? []
      }
    }
  }

  async function select(id) {
    selectedBoardId.value = id
    if (!id) {
      columns.value = []
      tasks.value = []
    } else {
      // Previous board stays visible until the new one arrives (no empty flash).
      await refreshBoard()
    }
  }

  async function createBoard(name = '') {
    const response = await boardService.createBoard({ name })
    await load()
    await select(response.board.id)
    return response.board
  }

  async function renameBoard(id, name) {
    const board = boards.value.find((b) => b.id === id)
    if (board) board.name = name
    await boardService.renameBoard({ id, name })
    await load()
  }

  // Local reorder applied before the backend round-trip so drops render
  // immediately; the follow-up reload trues up positions.
  function reorderLocally(list, id, previousId, nextId, mutate) {
    const moved = list.find((item) => item.id === id)
    let result = list
    if (moved) {
      const rest = list.filter((item) => item.id !== id)
      let insertAt = rest.length
      if (previousId) {
        insertAt = rest.findIndex((item) => item.id === previousId) + 1
      } else if (nextId) {
        insertAt = rest.findIndex((item) => item.id === nextId)
      }
      if (insertAt < 0) insertAt = rest.length
      const copy = { ...moved }
      if (mutate) mutate(copy)
      rest.splice(insertAt, 0, copy)
      result = rest
    }
    return result
  }

  async function moveBoard({ id, previousId, nextId }) {
    boards.value = reorderLocally(boards.value, id, previousId, nextId)
    await boardService.moveBoard({ id, previousId, nextId })
    await load()
  }

  async function deleteBoard(id) {
    await boardService.deleteBoard({ id })
    await load()
  }

  async function createColumn(name = '') {
    const response = await boardService.createColumn({ boardId: selectedBoardId.value, name })
    await refreshBoard()
    return response.column
  }

  async function renameColumn(id, name) {
    const column = columns.value.find((c) => c.id === id)
    if (column) column.name = name
    await boardService.renameColumn({ id, name })
    await refreshBoard()
  }

  async function moveColumn({ id, previousId, nextId }) {
    columns.value = reorderLocally(columns.value, id, previousId, nextId)
    await boardService.moveColumn({ id, previousId, nextId })
    await refreshBoard()
  }

  async function deleteColumn(id) {
    await boardService.deleteColumn({ id })
    await refreshBoard()
  }

  async function createTask(columnId, title = '') {
    const response = await boardService.createTask({ columnId, title })
    await refreshBoard()
    return response.task
  }

  async function getTask(id) {
    return boardService.getTask({ id })
  }

  async function saveTask({ id, title, body, noteIds }) {
    const response = await boardService.saveTask({ id, title, body, noteIds })
    const summary = tasks.value.find((t) => t.id === id)
    if (summary) {
      summary.title = title
      summary.body = body
      summary.updatedAt = response.updatedAt
      summary.linkedNoteCount = (noteIds ?? []).length
    }
    return response
  }

  async function moveTask({ id, columnId, previousId, nextId }) {
    tasks.value = reorderLocally(tasks.value, id, previousId, nextId, (task) => (task.columnId = columnId))
    await boardService.moveTask({ id, columnId, previousId, nextId })
    await refreshBoard()
  }

  async function deleteTask(id) {
    await boardService.deleteTask({ id })
    await refreshBoard()
  }

  async function refreshTaskAttachmentCount(id, count) {
    const summary = tasks.value.find((t) => t.id === id)
    if (summary) summary.attachmentCount = count
  }

  async function searchTasks(query) {
    const response = await boardService.searchTasks({ query })
    return response.results ?? []
  }

  async function searchBoards(query) {
    const response = await boardService.searchBoards({ query })
    return response.results ?? []
  }

  return {
    boards,
    loaded,
    selectedBoardId,
    selectedBoard,
    columns,
    tasks,
    tasksByColumn,
    openTaskRequest,
    load,
    refreshBoard,
    select,
    createBoard,
    renameBoard,
    moveBoard,
    deleteBoard,
    createColumn,
    renameColumn,
    moveColumn,
    deleteColumn,
    createTask,
    getTask,
    saveTask,
    moveTask,
    deleteTask,
    refreshTaskAttachmentCount,
    searchTasks,
    searchBoards,
  }
})
