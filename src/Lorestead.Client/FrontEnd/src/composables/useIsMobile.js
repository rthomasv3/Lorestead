import { ref } from 'vue'

// Mobile is a viewport width, not a platform (decisions.md): below Tailwind's
// `md` breakpoint (768px) the app renders its phone layout, so a shrunk desktop
// window or browser is a real test surface. Module-level singleton - one media
// query listener for the app's lifetime, shared by every caller.
const query = window.matchMedia('(max-width: 767.98px)')
const isMobile = ref(query.matches)

query.addEventListener('change', (event) => {
  isMobile.value = event.matches
})

export function useIsMobile() {
  return isMobile
}
