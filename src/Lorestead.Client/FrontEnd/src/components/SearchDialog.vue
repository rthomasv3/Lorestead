<script setup>
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle, VisuallyHidden } from 'reka-ui'
import EmptyState from './EmptyState.vue'
import SearchResults from './SearchResults.vue'
import { useSearch } from '../composables/useSearch.js'

// The desktop Ctrl+K surface: dialog chrome + keyboard navigation around the
// shared search machinery. The mobile Search screen (SearchView) wraps the same
// composable in a full-screen shell.
const { query, results, reset, ensureLoaded, choose: navigate, snippetParts, titleParts } = useSearch()

const open = ref(false)
const selectedIndex = ref(0)
const input = ref(null)
const list = ref(null)

watch(query, () => {
  selectedIndex.value = 0
})

watch(open, async (value) => {
  if (value) {
    reset()
    selectedIndex.value = 0
    ensureLoaded()
    await nextTick()
    input.value?.focus()
  }
})

function move(delta) {
  const count = results.value.length
  if (count === 0) return
  selectedIndex.value = (selectedIndex.value + delta + count) % count
  nextTick(() => {
    list.value?.children[selectedIndex.value]?.scrollIntoView({ block: 'nearest' })
  })
}

async function choose(result) {
  open.value = false
  if (!result) return
  await navigate(result)
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

        <!-- Selection follows the mouse (mousemove -> select), so hovering a row
             selects it and there is no separate hover state to paint. -->
        <div v-if="results.length > 0" ref="list" class="max-h-80 overflow-y-auto p-1.5">
          <SearchResults :results="results" :selected-index="selectedIndex" :title-parts="titleParts"
            :snippet-parts="snippetParts" @select="(i) => (selectedIndex = i)" @choose="choose" />
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
