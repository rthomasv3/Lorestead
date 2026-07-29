import { onBeforeUnmount } from 'vue'

// Keeping the editor and the preview looking at the same part of the document.
//
// The two panes hold the same text at different heights: three source lines of
// fence render as ten, an image is one line and half a screen. Scaling one
// scrollbar onto the other drifts the moment those diverge, so the panes are
// pinned together at every block the renderer tagged with the line that produced
// it (utils/markdownSource.js) and only the gaps between pins are interpolated.
// Top and bottom are pinned as well, so "all the way down" means the same thing
// on both sides no matter which pane is taller.

// How long after the last scroll event the gesture counts as over. Long enough to
// span the gap between wheel ticks, short enough that taking hold of the other
// pane never feels stuck.
const IDLE_MS = 200

// Pins are [editorY, previewY], strictly ascending on both axes so every segment
// has a direction to interpolate along.
function buildAnchors(scroller, geometry) {
  const previewMax = Math.max(scroller.scrollHeight - scroller.clientHeight, 0)
  const editorMax = Math.max(geometry.max, 0)
  const anchors = [[0, 0]]
  // Client rects rather than offsetTop: the scroller is not a positioned
  // ancestor, so offsetTop would be measured against something further up.
  const origin = scroller.getBoundingClientRect().top + scroller.clientTop - scroller.scrollTop

  let lastLine = -1
  for (const element of scroller.querySelectorAll('[data-line]')) {
    const line = Number(element.dataset.line)
    // A nested block repeats the line of the block containing it; the outer
    // element is already the pin for that spot.
    if (!Number.isFinite(line) || line <= lastLine) continue
    lastLine = line

    const previewY = element.getBoundingClientRect().top - origin
    const editorY = geometry.lineTop(line)
    // Nothing past the last scroll position is ever asked about, and a pin beyond
    // the end pin would break the ordering. Both axes only grow from here.
    if (editorY >= editorMax || previewY >= previewMax) break

    const previous = anchors[anchors.length - 1]
    if (editorY <= previous[0] || previewY <= previous[1]) continue
    anchors.push([editorY, previewY])
  }

  anchors.push([editorMax, previewMax])
  return anchors
}

// Linear within the bracketing pair: the pins carry the structure, so what is
// left between two of them is ordinary prose, which really does scale.
function project(anchors, y, from) {
  const to = from === 0 ? 1 : 0
  let index = 0
  while (index < anchors.length - 2 && anchors[index + 1][from] <= y) index++

  const start = anchors[index]
  const end = anchors[index + 1]
  const span = end[from] - start[from]
  const fraction = span > 0 ? (y - start[from]) / span : 0
  const target = start[to] + fraction * (end[to] - start[to])
  return Math.max(0, Math.min(anchors[anchors.length - 1][to], target))
}

export function useScrollSync(editorRef, previewRef) {
  let owner = null
  let idle = null
  let anchors = null

  function release() {
    owner = null
    anchors = null
  }

  // Whichever pane moved first owns the gesture. The scroll the other one fires
  // back arrives while it still does and is dropped, or the two chase each other.
  function claim(pane) {
    if (owner !== null && owner !== pane) return false
    owner = pane
    clearTimeout(idle)
    idle = setTimeout(release, IDLE_MS)
    return true
  }

  function sync(pane) {
    const editor = editorRef.value
    const preview = previewRef.value
    if (!editor || !preview || !claim(pane)) return

    const geometry = editor.scrollGeometry()
    if (!geometry) return
    // Built once per gesture: both heights change on every keystroke, and neither
    // changes while a scroll is in flight.
    if (!anchors) anchors = buildAnchors(preview, geometry)

    if (pane === 'editor') {
      preview.scrollTop = project(anchors, geometry.top, 0)
    } else {
      editor.scrollTo(project(anchors, preview.scrollTop, 1))
    }
  }

  onBeforeUnmount(() => clearTimeout(idle))

  return {
    onEditorScroll: () => sync('editor'),
    onPreviewScroll: () => sync('preview'),
  }
}
