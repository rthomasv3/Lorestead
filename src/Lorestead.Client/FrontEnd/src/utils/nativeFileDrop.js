// Files dropped onto the window from outside the app.
//
// On WebKitGTK the page never gets them: the DOM drop event fires with every
// flavour of its DataTransfer empty, and letting the webview handle the drop opens
// the file and takes the app off screen. So the host takes the drop instead
// (FileDropWatcher) and publishes files:dropped - which says what was dropped and
// not where.
//
// Where comes from the drag itself: dragover events are untouched and still reach
// the page, so a zone claims the drag while the pointer is over it and holds the
// claim until someone else takes it. The claim is cleared at the start of every
// dragover, in the capture phase, so a pointer that has moved off every zone leaves
// nothing behind to catch the drop.
//
// Everywhere else the webview puts the files on the drop event, no files:dropped is
// ever published, and none of this runs.

import { filePathsFromUris } from './attachmentFiles.js'

let claimant = null

// Called once at startup.
export function watchNativeDrops() {
  window.addEventListener('dragover', () => { claimant = null }, true)

  // A file dropped where nothing handles it is a navigation - the webview opens the
  // file and the app is gone. These run after any zone has had the event and cancel
  // only the webview's default, so a handled drop is unaffected.
  //
  // dragenter matters as much as dragover here: WebKit will not treat the page as a
  // drop target unless the enter is cancelled too, and an uncancelled enter is a
  // drag the window refuses before any zone gets a say.
  window.addEventListener('dragenter', accept)
  window.addEventListener('dragover', accept)
  window.addEventListener('drop', (event) => event.preventDefault())

  window.addEventListener('files:dropped', (event) => {
    const take = claimant
    claimant = null

    const paths = filePathsFromUris(event.detail?.uris ?? [])
    if (take && paths.length > 0) take(paths)
  })
}

// Cancelling the event only says the page could take a drop; the effect is what
// says it will. Chromium infers a sensible one, WebKit does not - it leaves the
// drop refused, which the drag shows as the crossed-out cursor and never delivers.
function accept(event) {
  event.preventDefault()

  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'copy'
  }
}

// Call from a zone's dragover: `take` receives the paths if the drop lands here.
export function claimDrop(take) {
  claimant = take
}
