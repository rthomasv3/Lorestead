import { invoke } from '../services/invoke.js'
import { useMobilePlatform } from '../composables/usePlatform.js'

// iOS floats its form-assistant pill above the keyboard, where it overlaps any
// keyboard-docked editor UI; the shell hides it via __setKeyboardAccessoryVisible
// (a no-op on Android and desktop). The decision is made per focusin from the
// focused element - never as hide/blur pairs, because a missed blur (editor torn
// down mid-focus, focus moves CodeMirror does not report) would leave the pill
// hidden app-wide. Recomputing on every focus self-heals: the next tap into a
// plain input restores it.
let lastSent = null

function onFocusIn(event) {
  const visible = !event.target?.closest?.('.cm-content')
  if (visible !== lastSent) {
    lastSent = visible
    invoke('__setKeyboardAccessoryVisible', { visible }).catch(() => {})
  }
}

// Called from main.js after initPlatform, so the platform flag is already real.
export function installAccessoryBarScoping() {
  if (useMobilePlatform().value) {
    document.addEventListener('focusin', onFocusIn)
  }
}
