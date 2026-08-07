<script>
import { useSearch } from '../composables/useSearch.js'

// Module scope, NOT <script setup> (which runs per instance): one composable
// instance for the app's lifetime, created on first visit. Leaving the tab
// unmounts the view; coming back restores the last query and results.
let persistent = null

function getSearch() {
  persistent ??= useSearch()
  return persistent
}
</script>

<script setup>
import { ref, onMounted } from 'vue'
import EmptyState from '../components/EmptyState.vue'
import SearchResults from '../components/SearchResults.vue'

// The mobile Search tab (renders as a plain page at desktop widths too).
const { query, results, ensureLoaded, choose, snippetParts, titleParts } = getSearch()
const input = ref(null)

function clear() {
  query.value = ''
  input.value?.focus()
}

onMounted(() => {
  ensureLoaded()
  // Best effort: focuses (and opens the keyboard) on desktop. On iOS this
  // lands DOM focus only - the webview won't raise the keyboard for a
  // programmatic focus here - so the field is one tap away on mobile.
  input.value?.focus()
})
</script>

<template>
  <div class="flex-1 flex flex-col min-h-0">
    <div class="flex items-center gap-2.5 px-3.5 h-12 shrink-0 border-b border-border">
      <i-lucide-search class="size-4 shrink-0 text-on-surface-muted" />
      <input ref="input" v-model="query" placeholder="Search notes, boards, tasks, settings..."
        class="flex-1 min-w-0 bg-transparent text-sm outline-none placeholder:text-on-surface-muted/60" />
      <button v-if="query" class="text-on-surface-muted hover:text-on-surface" aria-label="Clear search"
        @click="clear">
        <i-lucide-x class="size-4" />
      </button>
    </div>

    <div class="flex-1 min-h-0 overflow-y-auto p-1.5">
      <SearchResults v-if="results.length > 0" :results="results" comfortable :title-parts="titleParts"
        :snippet-parts="snippetParts" @choose="choose" />
      <EmptyState v-else-if="query.trim()" class="pt-10">
        No results
      </EmptyState>
      <EmptyState v-else class="pt-10">
        Search notes, boards, tasks, and settings
      </EmptyState>
    </div>
  </div>
</template>
