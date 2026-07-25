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
// note's body and the open task dialog are not touched from here - those views own
// the decision, because it depends on whether their editor is mid-edit.
window.addEventListener('notes:changed', () => {
  if (notes.loaded) {
    notes.load()
    notes.refreshAttachments()
    notes.refreshBacklinks()
  }
})
window.addEventListener('boards:changed', () => {
  if (boards.loaded) {
    boards.load()
    boards.refreshBoard()
  }
  // A task change can alter the open note's backlinks two ways - its linked-notes
  // list (task_note) or a note:// mention in its body - and both publish
  // boards:changed, never notes:changed.
  notes.refreshBacklinks()
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
