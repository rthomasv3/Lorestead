import { ref } from 'vue'

// True when the primary input is a mouse/trackpad: (hover: hover) and
// (pointer: fine). Input capability, not viewport - a shrunk desktop window
// still matches, a phone never does. Complements useIsMobile (which drives
// layout): this drives input behavior, currently whether drag-and-drop
// registers at all (touch long-press drag races the context menu and lost -
// mobile moves notes/boards through the Move dialog instead). Module
// singleton; live-updates if the environment changes (e.g. a tablet gaining
// a mouse).
const query = window.matchMedia('(hover: hover) and (pointer: fine)')
const hasFinePointer = ref(query.matches)

query.addEventListener('change', (event) => {
  hasFinePointer.value = event.matches
})

export function useFinePointer() {
  return hasFinePointer
}
