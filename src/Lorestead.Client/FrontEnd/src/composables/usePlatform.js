import { ref } from 'vue'
import { invoke } from '../services/invoke'

// The third signal beside useIsMobile (width -> layout) and useFinePointer
// (pointer -> input): platform -> capability. Gates file-system idioms -
// import, export, attachment download - which depend on where the app runs,
// not how wide it is: a narrow desktop window keeps them, a wide tablet does
// not. Fetched once in main.js before mount so no component ever renders
// with a provisional value.
const mobilePlatform = ref(false)

export async function initPlatform() {
  try {
    const result = await invoke('getPlatform')
    mobilePlatform.value = !!result?.mobile
  } catch {
    // Desktop is the harmless default; the failure is already in the log.
  }
}

export function useMobilePlatform() {
  return mobilePlatform
}
