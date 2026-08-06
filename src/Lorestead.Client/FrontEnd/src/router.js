import { nextTick } from 'vue'
import { createWebHashHistory, createRouter } from 'vue-router'

const NotesView = () => import('./views/notes/NotesView.vue')
const BoardsView = () => import('./views/boards/BoardsView.vue')
const SearchView = () => import('./views/SearchView.vue')
const SettingsView = () => import('./views/SettingsView.vue')

const routes = [
  { path: '/', redirect: '/notes' },
  // The id param is the selection - one source of truth on desktop and mobile
  // alike. Optional so the bare section route is the nothing-selected state.
  { path: '/notes/:id?', name: 'notes', component: NotesView },
  { path: '/boards/:id?', name: 'boards', component: BoardsView },
  // Mobile's Search tab; renders as a plain page at desktop widths too.
  { path: '/search', name: 'search', component: SearchView },
  { path: '/settings', name: 'settings', component: SettingsView },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

// Crossfade the content pane on section changes via the View Transitions API (FrameDyno
// pattern): resolve the guard inside the update callback so the route commits there, and
// hand back nextTick() so the snapshot is taken after Vue flushes. Only <main> carries a
// view-transition-name, so the sidebar never animates.
router.beforeResolve((to, from) => {
  const firstLoad = from.matched.length === 0
  const sameSection = to.matched[0] && to.matched[0] === from.matched[0]
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  let navigation

  if (document.startViewTransition && !firstLoad && !sameSection && !reduceMotion) {
    navigation = new Promise((resolve) => {
      document.startViewTransition(() => {
        resolve()
        return nextTick()
      })
    })
  }

  return navigation
})

export default router
