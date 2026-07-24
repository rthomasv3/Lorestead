<script setup>
import { useSettingsStore } from './stores/settingsStore'
import { useSyncStore } from './stores/syncStore'
import { useNotesStore } from './stores/notesStore'
import { useBoardsStore } from './stores/boardsStore'
import Sidebar from './components/Sidebar.vue'
import SearchDialog from './components/SearchDialog.vue'

useSettingsStore().init()
useSyncStore().init()

const notes = useNotesStore()
const boards = useBoardsStore()

// Pulled remote changes land here; only already-loaded data refreshes. The open
// note's body is deliberately left alone - reloading it mid-edit would clobber
// typing, and LWW means the editor's next autosave wins anyway.
window.addEventListener('notes:changed', () => {
  if (notes.loaded) {
    notes.load()
    notes.refreshAttachments()
  }
})
window.addEventListener('boards:changed', () => {
  if (boards.loaded) {
    boards.load()
    boards.refreshBoard()
  }
})
</script>

<template>
  <div class="h-screen flex bg-surface text-on-surface">
    <Sidebar />
    <main class="flex-1 min-w-0 flex flex-col min-h-0 bg-surface [view-transition-name:main-view]">
      <router-view />
    </main>
    <SearchDialog />
  </div>
</template>
