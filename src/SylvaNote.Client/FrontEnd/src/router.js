import { nextTick } from 'vue'
import { createWebHashHistory, createRouter } from 'vue-router'

const NotesView = () => import('./views/notes/NotesView.vue')
const BoardsView = () => import('./views/boards/BoardsView.vue')
const SettingsView = () => import('./views/SettingsView.vue')

const routes = [
  { path: '/', redirect: '/notes' },
  { path: '/notes', name: 'notes', component: NotesView },
  { path: '/boards', name: 'boards', component: BoardsView },
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
