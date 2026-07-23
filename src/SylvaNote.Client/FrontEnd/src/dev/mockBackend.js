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
    getLog: () => ({ text: '[mock] no log — running against the in-browser mock backend' }),

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
            ? `…${n.body.slice(Math.max(0, index - 20), index)}[${n.body.slice(index, index + q.length)}]${n.body.slice(index + q.length, index + q.length + 30)}…`
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
        id: newId(), noteId: request.noteId, taskId: null,
        filename: request.filename, mimeType: request.mimeType,
        sizeBytes: Math.floor((request.dataBase64?.length ?? 0) * 3 / 4),
        deleted: false, createdAt: nowIso(), updatedAt: nowIso(),
      }
      attachments.push(attachment)
      blobs.set(attachment.id, request.dataBase64 ?? '')
      return { attachment }
    },
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
  console.info('[SylvaNote] mock backend installed — no desktop shell detected')
}
