<script setup>
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle, VisuallyHidden } from 'reka-ui'
import EmptyState from './EmptyState.vue'
import { SETTINGS_INDEX } from '../utils/settingsIndex.js'
import { useNotesStore } from '../stores/notesStore.js'
import { useBoardsStore } from '../stores/boardsStore.js'

const router = useRouter()
const notesStore = useNotesStore()
const boardsStore = useBoardsStore()

const open = ref(false)
const query = ref('')
const noteResults = ref([])
const taskResults = ref([])
const boardResults = ref([])
const selectedIndex = ref(0)
const input = ref(null)
let searchTimer = null

const settingsResults = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return []
  return SETTINGS_INDEX
    .filter((entry) => entry.label.toLowerCase().includes(q))
    .map((entry) => ({
      kind: 'settings',
      key: `settings:${entry.section}:${entry.label}`,
      // A section is its own entry where the section is the whole control (About,
      // Logs); repeating it would read "Settings > About > About".
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
  selectedIndex.value = 0
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

watch(open, async (value) => {
  if (value) {
    query.value = ''
    noteResults.value = []
    taskResults.value = []
    boardResults.value = []
    selectedIndex.value = 0
    if (!notesStore.loaded) notesStore.load()
    await nextTick()
    input.value?.focus()
  }
})

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

function move(delta) {
  const count = results.value.length
  if (count === 0) return
  selectedIndex.value = (selectedIndex.value + delta + count) % count
}

async function choose(result) {
  open.value = false
  if (!result) return
  if (result.kind === 'note') {
    await router.push('/notes')
    notesStore.reveal(result.id)
    notesStore.select(result.id)
  } else if (result.kind === 'task') {
    await boardsStore.select(result.boardId)
    boardsStore.openTaskRequest = result.id
    await router.push('/boards')
  } else if (result.kind === 'board') {
    await boardsStore.select(result.id)
    await router.push('/boards')
  } else {
    await router.push('/settings')
    setTimeout(() => {
      document.getElementById(result.anchor)?.scrollIntoView({ block: 'start' })
    }, 100)
  }
}

function onKeydown(e) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    move(1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    move(-1)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    choose(results.value[selectedIndex.value])
  }
}

function onGlobalKeydown(e) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    open.value = true
  }
}

function onSearchOpen() {
  open.value = true
}

onMounted(() => {
  window.addEventListener('keydown', onGlobalKeydown)
  window.addEventListener('search:open', onSearchOpen)
})

onUnmounted(() => {
  window.removeEventListener('keydown', onGlobalKeydown)
  window.removeEventListener('search:open', onSearchOpen)
})
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent
        class="fixed left-1/2 top-24 -translate-x-1/2 z-50 w-full max-w-xl rounded-lg border border-border bg-surface-elevated shadow-xl overflow-hidden dialog-fade"
        @keydown="onKeydown">
        <VisuallyHidden>
          <DialogTitle>Search</DialogTitle>
        </VisuallyHidden>

        <div class="flex items-center gap-2.5 px-3.5 h-11 border-b border-border">
          <i-lucide-search class="size-4 shrink-0 text-on-surface-muted" />
          <input ref="input" v-model="query" placeholder="Search notes, boards, tasks, settings..."
            class="flex-1 min-w-0 bg-transparent text-sm outline-none placeholder:text-on-surface-muted/60" />
          <button class="text-on-surface-muted hover:text-on-surface" @click="open = false">
            <i-lucide-x class="size-4" />
          </button>
        </div>

        <div v-if="results.length > 0" class="max-h-80 overflow-y-auto p-1.5">
          <!-- Selection follows the mouse, so hovering a row selects it and there
               is no separate hover state to paint. -->
          <button v-for="(result, index) in results" :key="result.key"
            class="w-full text-left rounded-md px-2.5 py-2 flex flex-col gap-0.5"
            :class="index === selectedIndex ? 'bg-accent-soft' : ''"
            @mouseenter="selectedIndex = index" @click="choose(result)">
            <span class="flex items-center gap-1 text-sm min-w-0">
              <template v-for="(part, i) in result.breadcrumb" :key="i">
                <span v-if="i > 0" class="text-on-surface-muted/50 shrink-0">›</span>
                <span v-if="i === result.breadcrumb.length - 1" class="truncate">
                  <template v-for="(piece, j) in titleParts(part)" :key="j"><span
                      :class="piece.hit ? 'text-accent font-medium' : ''">{{ piece.text }}</span></template>
                </span>
                <span v-else class="text-on-surface-muted shrink-0">{{ part }}</span>
              </template>
            </span>
            <span v-if="result.snippet" class="text-xs text-on-surface-muted truncate">
              <template v-for="(piece, j) in snippetParts(result.snippet)" :key="j"><span
                  :class="piece.hit ? 'text-accent' : ''">{{ piece.text }}</span></template>
            </span>
          </button>
        </div>
        <EmptyState v-else-if="query.trim()">
          No results
        </EmptyState>

        <div class="flex items-center gap-4 px-3.5 h-8 border-t border-border text-xs text-on-surface-muted">
          <span><kbd>↑</kbd> <kbd>↓</kbd> to navigate</span>
          <span><kbd>Enter</kbd> to select</span>
          <span><kbd>Esc</kbd> to close</span>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
