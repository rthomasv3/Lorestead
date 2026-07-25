// Dev-only in-memory stand-in for the Galdr backend, installed when the frontend
// runs in a plain browser (Vite dev without the desktop shell). Semantics mirror
// the C# commands closely enough to exercise every notes-page flow; the real
// behavior is covered by the C# integration tests.

function newId() {
  return crypto.randomUUID()
}

function nowIso() {
  return new Date().toISOString()
}

export function installMockBackend() {
  const notes = []
  const attachments = []
  const blobs = new Map()
  const thumbnails = new Map()
  const boards = []
  const columns = []
  const tasks = []

  let application = {
    historyRetention: 50, serverUrl: '', theme: 'system', accentColor: 'indigo',
    dateFormat: 'yyyy-MM-dd', timeFormat: 'HH:mm', trashRetentionDays: 30,
    autoCheckUpdates: true, autoUpdate: false, lastUpdateCheckAt: '',
    newNoteFocus: 'title', newTaskFocus: 'title',
    windowWidth: 1200, windowHeight: 800, windowState: 'normal',
  }
  let editor = {
    fontSize: 14, fontFamily: '', spellcheckEnabled: true, showLineCount: true,
    highlightActiveLine: true, autosaveDebounceMs: 1000, mdTables: true,
    mdTaskLists: true, mdStrikethrough: true, mdAutolinks: true, mdFootnotes: true,
    mdCodeHighlighting: true, mdHighlight: true,
  }

  function byId(id) {
    return notes.find((n) => n.id === id)
  }

  function childPositions(parentId) {
    return notes.filter((n) => (n.parentId ?? null) === (parentId ?? null)).map((n) => n.position)
  }

  function positionAfterLast(parentId) {
    const positions = childPositions(parentId)
    return positions.length ? Math.max(...positions) + 1 : 1
  }

  function positionBetween(parentId, previousId, nextId) {
    const prev = previousId ? byId(previousId)?.position : null
    const next = nextId ? byId(nextId)?.position : null
    if (prev != null && next != null) return (prev + next) / 2
    if (prev != null) return prev + 1
    if (next != null) return next - 1
    return positionAfterLast(parentId)
  }

  function sorted() {
    return [...notes].sort((a, b) => a.position - b.position)
  }

  function summary(note) {
    const { body, ...rest } = note
    return rest
  }

  function subtreeIds(rootId) {
    const ids = [rootId]
    for (let i = 0; i < ids.length; i++) {
      for (const n of notes) {
        if (n.parentId === ids[i]) ids.push(n.id)
      }
    }
    return ids
  }

  const commands = {
    getSettings: () => ({ application, editor }),
    saveApplicationSettings: ({ request }) => {
      application = { ...application, ...request }
      return { application, editor }
    },
    saveEditorSettings: ({ request }) => {
      editor = { ...editor, ...request }
      return { application, editor }
    },
    getAbout: () => ({ appName: 'SylvaNote', version: 'dev (mock)' }),
    getLog: () => ({ text: '[mock] no log - running against the in-browser mock backend' }),

    getNotes: () => ({ notes: sorted().map(summary) }),
    getNote: ({ request }) => ({
      note: byId(request.id) ?? null,
      attachments: attachments.filter((a) => a.noteId === request.id && !a.deleted),
    }),
    createNote: ({ request }) => {
      const note = {
        id: newId(), parentId: request.parentId ?? null, title: request.title ?? '',
        body: '', position: positionAfterLast(request.parentId ?? null),
        type: request.template ? 1 : 0, deleted: false,
        createdAt: nowIso(), updatedAt: nowIso(),
      }
      notes.push(note)
      return { note }
    },
    saveNote: ({ request }) => {
      const note = byId(request.id)
      note.body = request.body ?? ''
      note.updatedAt = nowIso()
      return { updatedAt: note.updatedAt }
    },
    renameNote: ({ request }) => {
      const note = byId(request.id)
      note.title = request.title ?? ''
      note.updatedAt = nowIso()
      return { updatedAt: note.updatedAt }
    },
    moveNote: ({ request }) => {
      const note = byId(request.id)
      note.parentId = request.parentId ?? null
      note.position = positionBetween(note.parentId, request.previousId, request.nextId)
      note.type = request.template ? 1 : 0
      note.updatedAt = nowIso()
      return { position: String(note.position) }
    },
    trashNote: ({ request }) => {
      for (const id of subtreeIds(request.id)) {
        const note = byId(id)
        note.deleted = true
        note.updatedAt = nowIso()
      }
      return { ok: true }
    },
    restoreNote: ({ request }) => {
      let rootId = request.id
      if (request.withAncestors) {
        let current = byId(request.id)
        while (current) {
          if (current.deleted) rootId = current.id
          current = current.parentId ? byId(current.parentId) : null
        }
      }
      const root = byId(rootId)
      if (root.parentId && (!byId(root.parentId) || byId(root.parentId).deleted)) {
        root.parentId = null
        root.position = positionAfterLast(null)
      }
      for (const id of subtreeIds(rootId)) {
        byId(id).deleted = false
      }
      return { ok: true }
    },
    restoreNoteAt: ({ request }) => {
      const note = byId(request.id)
      note.parentId = request.parentId ?? null
      note.position = positionBetween(note.parentId, request.previousId, request.nextId)
      for (const id of subtreeIds(request.id)) {
        byId(id).deleted = false
      }
      return { ok: true }
    },
    purgeNote: ({ request }) => {
      for (const id of subtreeIds(request.id)) {
        const index = notes.findIndex((n) => n.id === id)
        if (index >= 0) notes.splice(index, 1)
      }
      return { ok: true }
    },
    createFromTemplate: ({ request }) => {
      const ids = subtreeIds(request.templateId)
      const map = new Map(ids.map((id) => [id, newId()]))
      for (const id of ids) {
        const source = byId(id)
        const isRoot = id === request.templateId
        notes.push({
          ...source,
          id: map.get(id),
          parentId: isRoot ? (request.parentId ?? null) : map.get(source.parentId),
          title: isRoot ? request.title : source.title,
          position: isRoot ? positionAfterLast(request.parentId ?? null) : source.position,
          type: 0,
          createdAt: nowIso(),
          updatedAt: nowIso(),
        })
      }
      return { rootId: map.get(request.templateId) }
    },
    searchNotes: ({ request }) => {
      const q = (request.query ?? '').toLowerCase()
      const results = notes
        .filter((n) => (request.includeTrashed || !n.deleted)
          && (n.title.toLowerCase().includes(q) || n.body.toLowerCase().includes(q)))
        .map((n) => {
          const index = n.body.toLowerCase().indexOf(q)
          const snippet = index >= 0
            ? `...${n.body.slice(Math.max(0, index - 20), index)}[${n.body.slice(index, index + q.length)}]${n.body.slice(index + q.length, index + q.length + 30)}...`
            : ''
          return { id: n.id, title: n.title, snippet }
        })
      return { results }
    },

    getAttachments: ({ request }) => ({
      attachments: attachments.filter((a) => a.noteId === request.noteId && !a.deleted),
    }),
    addAttachment: ({ request }) => {
      const attachment = {
        id: newId(), noteId: request.noteId ?? null, taskId: request.taskId ?? null,
        filename: request.filename, mimeType: request.mimeType,
        sizeBytes: Math.floor((request.dataBase64?.length ?? 0) * 3 / 4),
        deleted: false, createdAt: nowIso(), updatedAt: nowIso(),
      }
      attachments.push(attachment)
      blobs.set(attachment.id, request.dataBase64 ?? '')
      if (request.thumbnailBase64) thumbnails.set(attachment.id, request.thumbnailBase64)
      return { attachment }
    },
    getAttachmentThumbnail: ({ request }) => ({ dataBase64: thumbnails.get(request.id) ?? '' }),
    saveAttachmentThumbnail: ({ request }) => {
      thumbnails.set(request.id, request.dataBase64 ?? '')
      return { ok: true }
    },
    downloadAttachment: () => ({ saved: false }),
    renameAttachment: ({ request }) => {
      const attachment = attachments.find((a) => a.id === request.id)
      attachment.filename = request.filename
      return { ok: true }
    },
    deleteAttachment: ({ request }) => {
      const attachment = attachments.find((a) => a.id === request.id)
      attachment.deleted = true
      return { ok: true }
    },
    getBoards: () => ({ boards: boards.filter((b) => !b.deleted).sort((a, b) => a.position - b.position) }),
    createBoard: ({ request }) => {
      const maxPos = boards.length ? Math.max(...boards.map((b) => b.position)) : 0
      const board = {
        id: newId(), name: request.name ?? '', position: maxPos + 1,
        deleted: false, createdAt: nowIso(), updatedAt: nowIso(),
      }
      boards.push(board)
      return { board }
    },
    renameBoard: ({ request }) => {
      const board = boards.find((b) => b.id === request.id)
      board.name = request.name ?? ''
      board.updatedAt = nowIso()
      return { updatedAt: board.updatedAt }
    },
    moveBoard: ({ request }) => {
      const board = boards.find((b) => b.id === request.id)
      const prev = request.previousId ? boards.find((b) => b.id === request.previousId)?.position : null
      const next = request.nextId ? boards.find((b) => b.id === request.nextId)?.position : null
      board.position = prev != null && next != null ? (prev + next) / 2
        : prev != null ? prev + 1
        : next != null ? next - 1
        : (boards.length ? Math.max(...boards.map((b) => b.position)) + 1 : 1)
      board.updatedAt = nowIso()
      return { position: String(board.position) }
    },
    deleteBoard: ({ request }) => {
      const board = boards.find((b) => b.id === request.id)
      board.deleted = true
      for (const column of columns.filter((c) => c.boardId === request.id)) {
        column.deleted = true
        for (const task of tasks.filter((t) => t.columnId === column.id)) task.deleted = true
      }
      return { ok: true }
    },
    getBoard: ({ request }) => ({
      columns: columns
        .filter((c) => c.boardId === request.id && !c.deleted)
        .sort((a, b) => a.position - b.position),
      tasks: tasks
        .filter((t) => !t.deleted && columns.some((c) => c.id === t.columnId && c.boardId === request.id && !c.deleted))
        .sort((a, b) => a.position - b.position)
        .map((t) => ({
          id: t.id, columnId: t.columnId, title: t.title, body: t.body, position: t.position,
          attachmentCount: attachments.filter((a) => a.taskId === t.id && !a.deleted).length,
          linkedNoteCount: (t.noteIds ?? []).length,
          createdAt: t.createdAt, updatedAt: t.updatedAt,
        })),
    }),
    createColumn: ({ request }) => {
      const siblings = columns.filter((c) => c.boardId === request.boardId)
      const column = {
        id: newId(), boardId: request.boardId, name: request.name ?? '',
        position: siblings.length ? Math.max(...siblings.map((c) => c.position)) + 1 : 1,
        deleted: false, createdAt: nowIso(), updatedAt: nowIso(),
      }
      columns.push(column)
      return { column }
    },
    renameColumn: ({ request }) => {
      const column = columns.find((c) => c.id === request.id)
      column.name = request.name ?? ''
      column.updatedAt = nowIso()
      return { updatedAt: column.updatedAt }
    },
    moveColumn: ({ request }) => {
      const column = columns.find((c) => c.id === request.id)
      const prev = request.previousId ? columns.find((c) => c.id === request.previousId)?.position : null
      const next = request.nextId ? columns.find((c) => c.id === request.nextId)?.position : null
      const siblings = columns.filter((c) => c.boardId === column.boardId)
      column.position = prev != null && next != null ? (prev + next) / 2
        : prev != null ? prev + 1
        : next != null ? next - 1
        : (siblings.length ? Math.max(...siblings.map((c) => c.position)) + 1 : 1)
      column.updatedAt = nowIso()
      return { position: String(column.position) }
    },
    deleteColumn: ({ request }) => {
      const column = columns.find((c) => c.id === request.id)
      column.deleted = true
      for (const task of tasks.filter((t) => t.columnId === request.id)) task.deleted = true
      return { ok: true }
    },
    createTask: ({ request }) => {
      const siblings = tasks.filter((t) => t.columnId === request.columnId)
      const task = {
        id: newId(), columnId: request.columnId, title: request.title ?? '', body: '',
        position: siblings.length ? Math.max(...siblings.map((t) => t.position)) + 1 : 1,
        noteIds: [], deleted: false, createdAt: nowIso(), updatedAt: nowIso(),
      }
      tasks.push(task)
      return { task }
    },
    getTask: ({ request }) => ({
      task: tasks.find((t) => t.id === request.id) ?? null,
      attachments: attachments.filter((a) => a.taskId === request.id && !a.deleted),
    }),
    saveTask: ({ request }) => {
      const task = tasks.find((t) => t.id === request.id)
      task.title = request.title ?? ''
      task.body = request.body ?? ''
      task.noteIds = request.noteIds ?? []
      task.updatedAt = nowIso()
      return { updatedAt: task.updatedAt }
    },
    moveTask: ({ request }) => {
      const task = tasks.find((t) => t.id === request.id)
      task.columnId = request.columnId
      const prev = request.previousId ? tasks.find((t) => t.id === request.previousId)?.position : null
      const next = request.nextId ? tasks.find((t) => t.id === request.nextId)?.position : null
      const siblings = tasks.filter((t) => t.columnId === request.columnId && t.id !== task.id)
      task.position = prev != null && next != null ? (prev + next) / 2
        : prev != null ? prev + 1
        : next != null ? next - 1
        : (siblings.length ? Math.max(...siblings.map((t) => t.position)) + 1 : 1)
      task.updatedAt = nowIso()
      return { position: String(task.position) }
    },
    deleteTask: ({ request }) => {
      const task = tasks.find((t) => t.id === request.id)
      task.deleted = true
      return { ok: true }
    },
    searchTasks: ({ request }) => {
      const q = (request.query ?? '').toLowerCase()
      const results = tasks
        .filter((t) => !t.deleted && (t.title.toLowerCase().includes(q) || t.body.toLowerCase().includes(q)))
        .map((t) => {
          const column = columns.find((c) => c.id === t.columnId)
          const board = column ? boards.find((b) => b.id === column.boardId) : null
          if (!column || column.deleted || !board || board.deleted) return null
          const index = t.body.toLowerCase().indexOf(q)
          const snippet = index >= 0
            ? `...${t.body.slice(Math.max(0, index - 20), index)}[${t.body.slice(index, index + q.length)}]${t.body.slice(index + q.length, index + q.length + 30)}...`
            : ''
          return {
            id: t.id, title: t.title, snippet,
            columnId: column.id, columnName: column.name, boardId: board.id, boardName: board.name,
          }
        })
        .filter(Boolean)
      return { results }
    },
    searchBoards: ({ request }) => {
      const q = (request.query ?? '').toLowerCase()
      return {
        results: boards
          .filter((b) => !b.deleted && b.name.toLowerCase().includes(q))
          .map((b) => ({ id: b.id, title: b.name, snippet: '' })),
      }
    },

    getAttachmentData: ({ request }) => {
      const attachment = attachments.find((a) => a.id === request.id)
      return {
        filename: attachment?.filename ?? '', mimeType: attachment?.mimeType ?? '',
        dataBase64: blobs.get(request.id) ?? '',
      }
    },
  }

  window.galdrInvoke = async (command, args) => {
    const handler = commands[command]
    if (!handler) throw new Error(`[mock] unknown command '${command}'`)
    return structuredClone(handler(args ?? {}))
  }
  console.info('[SylvaNote] mock backend installed - no desktop shell detected')
}
