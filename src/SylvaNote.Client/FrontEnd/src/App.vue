<script setup>
import { useRouter } from 'vue-router'
import { TooltipProvider } from 'reka-ui'
import { useSettingsStore } from './stores/settingsStore'
import { useSyncStore } from './stores/syncStore'
import { useNotesStore } from './stores/notesStore'
import { useBoardsStore } from './stores/boardsStore'
import Sidebar from './components/Sidebar.vue'
import SearchDialog from './components/SearchDialog.vue'

useSettingsStore().init()
useSyncStore().init()

const router = useRouter()
const notes = useNotesStore()
const boards = useBoardsStore()

// A note:// link clicked in any preview - notes editor or task dialog. Handled here
// rather than in MarkdownPreview because the jump crosses routes, and it is the
// same landing as a search result.
window.addEventListener('note:navigate', async (event) => {
  const id = event.detail?.id
  if (!id) return
  await router.push('/notes')
  notes.reveal(id)
  notes.select(id)
})

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
  <!-- One provider for every HoverTip in the app. skip-delay-duration=0 because a
       provider shares one "recently open" flag across all its tooltips, and inside
       that window the next trigger opens with no delay at all - one provider app-wide
       is what would turn that into the second toolbar button popping instantly.
       HoverTip drives its own timer and never reads the flag, but this is not
       something to leave resting on that. -->
  <TooltipProvider :skip-delay-duration="0">
    <div class="h-screen flex bg-surface text-on-surface">
      <Sidebar />
      <main class="flex-1 min-w-0 flex flex-col min-h-0 bg-surface [view-transition-name:main-view]">
        <router-view />
      </main>
      <SearchDialog />
    </div>
  </TooltipProvider>
</template>
