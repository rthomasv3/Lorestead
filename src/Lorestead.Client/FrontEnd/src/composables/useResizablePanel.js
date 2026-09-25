import { ref, onBeforeUnmount } from 'vue'

// Widths are unscaled px. When the panel renders at width * scale() (see
// ToolPanelShell), pointer travel is divided by the same factor so the edge stays
// under the pointer, and a saved width means the same thing at any scale.
export function useResizablePanel({ defaultWidth = 280, minWidth = 180, maxWidth = 600, storageKey = null, scale = () => 1 }) {
  const width = ref(loadWidth())
  const isDragging = ref(false)

  let startX = 0
  let startWidth = 0
  let dragScale = 1

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
    dragScale = scale()
    event.preventDefault()
    document.addEventListener('pointermove', onPointerMove)
    document.addEventListener('pointerup', onPointerUp)
  }

  function onPointerMove(event) {
    const delta = (startX - event.clientX) / dragScale
    width.value = Math.round(Math.min(maxWidth, Math.max(minWidth, startWidth + delta)))
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
