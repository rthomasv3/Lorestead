import { ref, onBeforeUnmount } from 'vue'

export function useResizablePanel({ defaultWidth = 280, minWidth = 180, maxWidth = 600, storageKey = null }) {
  const width = ref(loadWidth())
  const isDragging = ref(false)

  let startX = 0
  let startWidth = 0

  function loadWidth() {
    if (storageKey) {
      const saved = localStorage.getItem(storageKey)
      if (saved) {
        const parsed = parseInt(saved, 10)
        if (!isNaN(parsed) && parsed >= minWidth && parsed <= maxWidth) {
          return parsed
        }
      }
    }
    return defaultWidth
  }

  function saveWidth() {
    if (storageKey) {
      localStorage.setItem(storageKey, width.value)
    }
  }

  function onPointerDown(event) {
    isDragging.value = true
    startX = event.clientX
    startWidth = width.value
    event.preventDefault()
    document.addEventListener('pointermove', onPointerMove)
    document.addEventListener('pointerup', onPointerUp)
  }

  function onPointerMove(event) {
    const delta = startX - event.clientX
    width.value = Math.min(maxWidth, Math.max(minWidth, startWidth + delta))
  }

  function onPointerUp() {
    isDragging.value = false
    saveWidth()
    document.removeEventListener('pointermove', onPointerMove)
    document.removeEventListener('pointerup', onPointerUp)
  }

  onBeforeUnmount(() => {
    document.removeEventListener('pointermove', onPointerMove)
    document.removeEventListener('pointerup', onPointerUp)
  })

  return { width, isDragging, onPointerDown }
}
