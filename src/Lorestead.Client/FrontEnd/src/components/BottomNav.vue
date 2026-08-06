<script setup>
import { useRoute, useRouter } from 'vue-router'
import IconNotebookText from '~icons/lucide/notebook-text'
import IconSquareKanban from '~icons/lucide/square-kanban'
import IconSearch from '~icons/lucide/search'
import IconSettings from '~icons/lucide/settings'

const route = useRoute()
const router = useRouter()

// Tabs always target the bare section route: on mobile the Notes/Boards tab is
// the list screen, not the last-open detail (that lives one push deeper).
const tabs = [
  { name: 'notes', to: '/notes', label: 'Notes', icon: IconNotebookText },
  { name: 'boards', to: '/boards', label: 'Boards', icon: IconSquareKanban },
  { name: 'search', to: '/search', label: 'Search', icon: IconSearch },
  { name: 'settings', to: '/settings', label: 'Settings', icon: IconSettings },
]
</script>

<template>
  <!-- pb-safe on the nav itself so the bar's background extends under the
       iPhone home indicator / Android gesture area. -->
  <nav class="md:hidden shrink-0 border-t border-border bg-surface flex items-stretch pb-safe">
    <button v-for="tab in tabs" :key="tab.name" type="button"
      class="flex-1 flex flex-col items-center justify-center gap-0.5 pt-2 pb-1.5 text-[11px]"
      :class="route.name === tab.name ? 'text-accent' : 'text-on-surface-muted'"
      @click="router.push(tab.to)">
      <component :is="tab.icon" class="size-5" />
      <span>{{ tab.label }}</span>
    </button>
  </nav>
</template>
