import { computed, effectScope, ref, watch } from 'vue'
import router from '../router.js'
import { SETTINGS_INDEX } from '../utils/settingsIndex.js'
import { useMobilePlatform } from './usePlatform.js'
import { useNotesStore } from '../stores/notesStore.js'
import { useBoardsStore } from '../stores/boardsStore.js'

// The search machinery shared by the desktop Ctrl+K dialog and the mobile
// Search screen: query, debounced multi-source results, hit highlighting, and
// choose() navigation. Chrome stays with the callers - this owns no markup.
//
// Each call returns an independent instance. The reactive graph lives in a
// detached effect scope on purpose: the mobile Search screen keeps one
// instance across mounts (the tab restores your last search), so its watcher
// must survive the component that happened to create it. Instances are
// app-lifetime by design - the dialog is always mounted, the screen's is a
// module singleton - so the scope is never disposed.
export function useSearch() {
  const scope = effectScope(true)

  return scope.run(() => {
    const notesStore = useNotesStore()
    const boardsStore = useBoardsStore()
    const mobilePlatform = useMobilePlatform()

    const query = ref('')
    const noteResults = ref([])
    const taskResults = ref([])
    const boardResults = ref([])
    let searchTimer = null

    const settingsResults = computed(() => {
      const q = query.value.trim().toLowerCase()
      if (!q) return []
      return SETTINGS_INDEX
        .filter((entry) => !(entry.desktopOnly && mobilePlatform.value))
        .filter((entry) => entry.label.toLowerCase().includes(q))
        .map((entry) => ({
          kind: 'settings',
          key: `settings:${entry.section}:${entry.label}`,
          // A section is its own entry where the section is the whole control
          // (About, Logs); repeating it would read "Settings > About > About".
          breadcrumb: entry.label === entry.section
            ? ['Settings', entry.section]
            : ['Settings', entry.section, entry.label],
          label: entry.label,
          anchor: entry.anchor,
        }))
    })

    // Content hits (notes, tasks) first; board/settings name matches after
    // (features/search.md ordering).
    const results = computed(() => [
      ...noteResults.value.map((r) => ({
        kind: 'note',
        key: `note:${r.id}`,
        id: r.id,
        breadcrumb: notesStore.pathOf(r.id) ?? ['Notes', r.title || 'Untitled'],
        label: r.title || 'Untitled',
        snippet: r.snippet,
      })),
      ...taskResults.value.map((r) => ({
        kind: 'task',
        key: `task:${r.id}`,
        id: r.id,
        boardId: r.boardId,
        breadcrumb: [r.boardName || 'Untitled board', r.columnName || 'Untitled list', r.title || 'Untitled task'],
        label: r.title || 'Untitled task',
        snippet: r.snippet,
      })),
      ...boardResults.value.map((r) => ({
        kind: 'board',
        key: `board:${r.id}`,
        id: r.id,
        breadcrumb: ['Boards', r.title || 'Untitled board'],
        label: r.title || 'Untitled board',
      })),
      ...settingsResults.value,
    ])

    watch(query, (value) => {
      clearTimeout(searchTimer)
      if (!value.trim()) {
        noteResults.value = []
        taskResults.value = []
        boardResults.value = []
        return
      }
      searchTimer = setTimeout(async () => {
        const q = value.trim()
        const [notes, tasks, boards] = await Promise.all([
          notesStore.search(q, { includeTrashed: true }),
          boardsStore.searchTasks(q),
          boardsStore.searchBoards(q),
        ])
        noteResults.value = notes
        taskResults.value = tasks
        boardResults.value = boards
      }, 150)
    })

    function reset() {
      query.value = ''
      noteResults.value = []
      taskResults.value = []
      boardResults.value = []
    }

    // Breadcrumbs lean on the notes summaries (pathOf); callers invoke this when
    // their surface appears so a cold start still gets full paths.
    function ensureLoaded() {
      if (!notesStore.loaded) notesStore.load()
    }

    // Unified routing: every result kind is a route push; the views' param
    // watchers do the fetching. reveal() expands the tree down to a note hit.
    async function choose(result) {
      if (!result) return
      if (result.kind === 'note') {
        notesStore.reveal(result.id)
        await router.push(`/notes/${result.id}`)
      } else if (result.kind === 'task') {
        boardsStore.openTaskRequest = result.id
        await router.push(`/boards/${result.boardId}`)
      } else if (result.kind === 'board') {
        await router.push(`/boards/${result.id}`)
      } else {
        await router.push('/settings')
        setTimeout(() => {
          document.getElementById(result.anchor)?.scrollIntoView({ block: 'start' })
        }, 100)
      }
    }

    // Splits text into parts, marking FTS "[hit]" markers (Core snippet delimiters).
    function snippetParts(snippet) {
      const parts = []
      const pattern = /\[([^\]]*)\]/g
      let last = 0
      let match
      while ((match = pattern.exec(snippet)) !== null) {
        if (match.index > last) parts.push({ text: snippet.slice(last, match.index), hit: false })
        parts.push({ text: match[1], hit: true })
        last = match.index + match[0].length
      }
      if (last < snippet.length) parts.push({ text: snippet.slice(last), hit: false })
      return parts
    }

    function titleParts(label) {
      const q = query.value.trim().toLowerCase()
      const index = q ? label.toLowerCase().indexOf(q) : -1
      if (index < 0) return [{ text: label, hit: false }]
      return [
        { text: label.slice(0, index), hit: false },
        { text: label.slice(index, index + q.length), hit: true },
        { text: label.slice(index + q.length), hit: false },
      ].filter((p) => p.text)
    }

    return { query, results, reset, ensureLoaded, choose, snippetParts, titleParts }
  })
}
