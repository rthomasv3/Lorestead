import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as noteService from '../services/noteService.js'
import * as attachmentService from '../services/attachmentService.js'

export const TEMPLATES_ID = '__templates'
export const TRASH_ID = '__trash'

function isTemplate(summary) {
  return summary.type === 1 || summary.type === 'template' || summary.type === 'Template'
}

export const useNotesStore = defineStore('notes', () => {
  const summaries = ref([])
  const loaded = ref(false)
  const selectedId = ref(null)
  const currentNote = ref(null)
  const currentAttachments = ref([])
  const currentBacklinks = ref([])
  // Tree expansion lives here (not in the Tree component) so it survives route
  // changes - the view unmounts on navigation but the store does not.
  const expandedIds = ref(new Set())

  const blobUrls = new Map()

  const byId = computed(() => {
    const map = new Map()
    for (const summary of summaries.value) {
      map.set(summary.id, summary)
    }
    return map
  })

  const byParent = computed(() => {
    const map = new Map()
    for (const summary of summaries.value) {
      const key = summary.parentId ?? null
      if (!map.has(key)) map.set(key, [])
      map.get(key).push(summary)
    }
    return map
  })

  function childrenOf(parentId, { trashed }) {
    const children = byParent.value.get(parentId) ?? []
    return children.filter((c) => c.deleted === trashed)
  }

  function toItem(summary, { trashed }) {
    return {
      id: summary.id,
      noteId: summary.id,
      label: summary.title || 'Untitled',
      type: 'note',
      trashed,
      template: isTemplate(summary),
      children: childrenOf(summary.id, { trashed }).map((c) => toItem(c, { trashed })),
    }
  }

  const treeItems = computed(() => {
    const roots = (byParent.value.get(null) ?? [])
      .filter((s) => !s.deleted && !isTemplate(s))
      .map((s) => toItem(s, { trashed: false }))

    // Template roots are type-marked wherever they live; trash roots are tombstoned
    // notes whose parent is alive or gone (parent_id is never disturbed by deletion).
    const templateRoots = summaries.value
      .filter((s) => isTemplate(s) && !s.deleted)
      .map((s) => toItem(s, { trashed: false }))

    const trashRoots = summaries.value
      .filter((s) => {
        if (!s.deleted) return false
        const parent = s.parentId ? byId.value.get(s.parentId) : null
        return !s.parentId || !parent || !parent.deleted
      })
      .map((s) => toItem(s, { trashed: true }))

    return [
      ...roots,
      {
        id: TEMPLATES_ID,
        label: 'Templates',
        type: 'templates-root',
        selectable: false,
        expandable: true,
        emptyLabel: 'No Templates',
        children: templateRoots,
      },
      {
        id: TRASH_ID,
        label: 'Trash',
        type: 'trash-root',
        selectable: false,
        expandable: true,
        emptyLabel: 'No Trash',
        dropLabel: 'Delete Item',
        children: trashRoots,
      },
    ]
  })

  const templateRootSummaries = computed(() =>
    summaries.value.filter((s) => isTemplate(s) && !s.deleted))

  async function load() {
    const response = await noteService.getNotes()
    summaries.value = response.notes ?? []
    loaded.value = true
  }

  async function select(id) {
    selectedId.value = id
    if (!id) {
      currentNote.value = null
      currentAttachments.value = []
      currentBacklinks.value = []
    } else {
      // The previous note stays visible until the new one arrives - clearing first
      // would flash the empty state on every click, including reselects.
      const response = await noteService.getNote({ id })
      if (selectedId.value === id) {
        currentNote.value = response.note
        currentAttachments.value = response.attachments ?? []
        currentBacklinks.value = response.backlinks ?? []
      }
    }
  }

  async function create({ parentId = null, title = '', template = false } = {}) {
    const response = await noteService.createNote({ parentId, title, template })
    await load()
    return response.note
  }

  async function rename(id, title) {
    const summary = byId.value.get(id)
    if (summary) summary.title = title
    if (currentNote.value?.id === id) currentNote.value.title = title
    await noteService.renameNote({ id, title })
    await load()
  }

  async function saveBody(id, body) {
    const response = await noteService.saveNote({ id, body })
    const summary = byId.value.get(id)
    if (summary) summary.updatedAt = response.updatedAt
    return response
  }

  async function move({ id, parentId, previousId, nextId, template = false }) {
    await noteService.moveNote({ id, parentId, previousId, nextId, template })
    await load()
  }

  async function trash(id) {
    await noteService.trashNote({ id })
    await load()
    if (selectedId.value && !byId.value.get(selectedId.value)) {
      await select(null)
    } else if (selectedId.value) {
      await select(selectedId.value)
    }
  }

  async function restore(id, { withAncestors = false } = {}) {
    await noteService.restoreNote({ id, withAncestors })
    await load()
    if (selectedId.value === id) await select(id)
  }

  async function restoreAt({ id, parentId, previousId, nextId }) {
    await noteService.restoreNoteAt({ id, parentId, previousId, nextId })
    await load()
    if (selectedId.value === id) await select(id)
  }

  async function purge(id) {
    await noteService.purgeNote({ id })
    await load()
    if (selectedId.value && !byId.value.get(selectedId.value)) {
      await select(null)
    }
  }

  async function createFromTemplate({ templateId, title, parentId = null }) {
    const response = await noteService.createFromTemplate({ templateId, title, parentId })
    await load()
    return response.rootId
  }

  async function search(query, { includeTrashed = true } = {}) {
    const response = await noteService.searchNotes({ query, includeTrashed })
    return response.results ?? []
  }

  // Backlinks are derived from other items' bodies, so they change without this
  // note changing - refreshed alongside attachments on every notes:changed.
  async function refreshBacklinks() {
    if (selectedId.value) {
      const id = selectedId.value
      const response = await noteService.getNote({ id })
      if (selectedId.value === id) {
        currentBacklinks.value = response.backlinks ?? []
      }
    }
  }

  // --- Attachments (for the selected note) ---

  async function refreshAttachments() {
    if (selectedId.value) {
      const response = await attachmentService.getAttachments({ noteId: selectedId.value })
      currentAttachments.value = response.attachments ?? []
    }
  }

  async function addAttachment({ filename, mimeType, dataBase64, thumbnailBase64 = null }) {
    const response = await attachmentService.addAttachment({
      noteId: selectedId.value,
      filename,
      mimeType,
      dataBase64,
      thumbnailBase64,
    })
    await refreshAttachments()
    return response.attachment
  }

  async function renameAttachment(id, filename) {
    await attachmentService.renameAttachment({ id, filename })
    await refreshAttachments()
  }

  async function removeAttachment(id) {
    await attachmentService.deleteAttachment({ id })
    releaseBlobUrl(id)
    releaseThumbnailUrl(id)
    await refreshAttachments()
  }

  // Object URLs are cached per attachment id - blobs are immutable, so a URL stays
  // valid for the app's lifetime and both the preview and the cards reuse it.
  async function getAttachmentUrl(id) {
    if (!blobUrls.has(id)) {
      const promise = attachmentService.getAttachmentData({ id }).then((data) => {
        const bytes = Uint8Array.from(atob(data.dataBase64 || ''), (c) => c.charCodeAt(0))
        return URL.createObjectURL(new Blob([bytes], { type: data.mimeType || 'application/octet-stream' }))
      })
      blobUrls.set(id, promise)
    }
    return blobUrls.get(id)
  }

  function releaseBlobUrl(id) {
    const cached = blobUrls.get(id)
    if (cached) {
      blobUrls.delete(id)
      cached.then((url) => URL.revokeObjectURL(url)).catch(() => {})
    }
  }

  // Cards only ever pull the small thumbnail across the bridge - the full blob
  // moves on demand (preview, body embeds, download stays fully backend-side).
  const thumbnailUrls = new Map()

  // Bumped whenever a cached thumbnail url is dropped. The cache is a plain Map, so
  // cards have nothing reactive to watch otherwise - after a backfill they would keep
  // showing the type icon until the component remounted.
  const thumbnailVersion = ref(0)

  async function getAttachmentThumbnailUrl(id) {
    if (!thumbnailUrls.has(id)) {
      const promise = attachmentService.getAttachmentThumbnail({ id }).then((data) => {
        if (!data.dataBase64) return null
        const bytes = Uint8Array.from(atob(data.dataBase64), (c) => c.charCodeAt(0))
        return URL.createObjectURL(new Blob([bytes], { type: 'image/png' }))
      })
      thumbnailUrls.set(id, promise)
    }
    return thumbnailUrls.get(id)
  }

  // Lazy rebuild path: attachments that arrived without a local thumbnail (sync,
  // MCP) get one the first time this device holds the full image.
  async function storeAttachmentThumbnail(id, dataBase64) {
    await attachmentService.saveAttachmentThumbnail({ id, dataBase64 })
    releaseThumbnailUrl(id)
  }

  function releaseThumbnailUrl(id) {
    const cached = thumbnailUrls.get(id)
    if (cached) {
      thumbnailUrls.delete(id)
      cached.then((url) => url && URL.revokeObjectURL(url)).catch(() => {})
    }
    thumbnailVersion.value++
  }

  // Expands everything on the way to a note - ancestor notes plus the Templates/
  // Trash virtual containers when the note lives under one - so a jump from
  // search always lands on a visible row.
  function reveal(id) {
    const ids = new Set(expandedIds.value)
    let inTemplates = false
    let inTrash = false
    let current = byId.value.get(id)
    while (current) {
      if (isTemplate(current)) inTemplates = true
      if (current.deleted) inTrash = true
      if (current.id !== id) ids.add(current.id)
      current = current.parentId ? byId.value.get(current.parentId) : null
    }
    if (inTemplates) ids.add(TEMPLATES_ID)
    if (inTrash) ids.add(TRASH_ID)
    expandedIds.value = ids
  }

  // Breadcrumb path from the tree structure: ['Notes'|'Templates'|'Trash', ...titles].
  function pathOf(id) {
    const path = []
    let current = byId.value.get(id)
    if (!current) return null
    let trashed = false
    while (current) {
      path.unshift(current.title || 'Untitled')
      if (current.deleted) trashed = true
      current = current.parentId ? byId.value.get(current.parentId) : null
    }
    const top = byId.value.get(id)
    let section = 'Notes'
    if (trashed) section = 'Trash'
    else {
      let walk = top
      while (walk) {
        if (isTemplate(walk)) {
          section = 'Templates'
          break
        }
        walk = walk.parentId ? byId.value.get(walk.parentId) : null
      }
    }
    return [section, ...path]
  }

  return {
    summaries,
    loaded,
    selectedId,
    currentNote,
    currentAttachments,
    currentBacklinks,
    expandedIds,
    treeItems,
    templateRootSummaries,
    byId,
    load,
    select,
    create,
    rename,
    saveBody,
    move,
    trash,
    restore,
    restoreAt,
    purge,
    createFromTemplate,
    search,
    refreshAttachments,
    refreshBacklinks,
    addAttachment,
    renameAttachment,
    removeAttachment,
    getAttachmentUrl,
    getAttachmentThumbnailUrl,
    storeAttachmentThumbnail,
    thumbnailVersion,
    reveal,
    pathOf,
  }
})
