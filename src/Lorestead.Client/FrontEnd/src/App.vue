<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TooltipProvider } from 'reka-ui'
import { useSettingsStore } from './stores/settingsStore'
import { useSyncStore } from './stores/syncStore'
import { useUpdatesStore } from './stores/updatesStore'
import { useNotesStore } from './stores/notesStore'
import { useBoardsStore } from './stores/boardsStore'
import Sidebar from './components/Sidebar.vue'
import BottomNav from './components/BottomNav.vue'
import SearchDialog from './components/SearchDialog.vue'

useSettingsStore().init()
useSyncStore().init()
useUpdatesStore().init()

const route = useRoute()
const router = useRouter()
const notes = useNotesStore()
const boards = useBoardsStore()

// The tab bar shows on top-level screens only - detail screens (note editor,
// single board) carry an id param and get the bottom edge for their own use.
const showTabBar = computed(() => !route.params.id)

// A note:// link clicked in any preview - notes editor or task dialog. Handled here
// rather than in MarkdownPreview because the jump crosses routes, and it is the
// same landing as a search result.
window.addEventListener('note:navigate', async (event) => {
  const id = event.detail?.id
  if (!id) return
  notes.reveal(id)
  await router.push(`/notes/${id}`)
})

// Pulled remote changes land here; only already-loaded data refreshes. The open
// note's body and the open task dialog are not touched from here - those views own
// the decision, because it depends on whether their editor is mid-edit.
//
// Content refreshes are gated on the route that shows them: an unmounted view has
// dropped its content and refetches everything on mount anyway (decisions.md), so
// refreshing it from here would just repopulate state nobody is showing. The
// summary lists (load) stay ungated - they are navigation data, kept warm so the
// tree and board list render instantly on return.
window.addEventListener('notes:changed', () => {
  if (notes.loaded) notes.load()
  if (router.currentRoute.value.name === 'notes') {
    notes.refreshAttachments()
    notes.refreshBacklinks()
  }
})
window.addEventListener('boards:changed', () => {
  if (boards.loaded) boards.load()
  if (router.currentRoute.value.name === 'boards') boards.refreshBoard()
  // A task change can alter the open note's backlinks two ways - its linked-notes
  // list (task_note) or a note:// mention in its body - and both publish
  // boards:changed, never notes:changed.
  if (router.currentRoute.value.name === 'notes') notes.refreshBacklinks()
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
    <!-- Column below md (content over the tab bar), row at md+ (sidebar beside
         content). The sidebar hides itself below md; the tab bar is md:hidden.
         The top safe-area inset is consumed once here (pt-safe) so every screen
         clears the status bar / notch; the tab bar pads its own bottom inset. -->
    <div class="h-screen flex flex-col md:flex-row bg-surface text-on-surface pt-safe">
      <Sidebar />
      <main class="flex-1 min-w-0 flex flex-col min-h-0 bg-surface [view-transition-name:main-view]">
        <router-view />
      </main>
      <BottomNav v-if="showTabBar" />
      <SearchDialog />
    </div>
  </TooltipProvider>
</template>
