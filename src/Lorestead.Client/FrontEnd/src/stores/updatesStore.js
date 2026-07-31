import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getUpdateStatus, checkForUpdate, downloadUpdate, applyUpdateAndRestart } from '../services/updateService.js'

export const useUpdatesStore = defineStore('updates', () => {
  const status = ref(null)
  const progress = ref(0)
  // Local in-flight flags: the backend only publishes update:status when an
  // operation finishes, so its busy field never reaches the UI mid-operation.
  const checking = ref(false)
  const downloading = ref(false)

  async function init() {
    // Background auto-check and auto-download results land here; the initial
    // fetch covers the window before the first event.
    window.addEventListener('update:status', (e) => {
      status.value = e.detail
    })
    window.addEventListener('update:progress', (e) => {
      progress.value = e.detail?.percent ?? 0
    })
    try {
      status.value = await getUpdateStatus()
    } catch {
      // Backend unreachable - the section stays in its unsupported state; already logged.
    }
  }

  async function check() {
    checking.value = true
    try {
      status.value = await checkForUpdate()
    } catch {
      // Failure is in the log; the last known status stands.
    } finally {
      checking.value = false
    }
  }

  // The one-click flow (settings.md): download if needed - progress events
  // drive the bar - then restart into the new version. On success the process
  // exits inside apply; an apply refusal (an agent holding the MCP exe) comes
  // back through status.error instead.
  async function relaunch() {
    try {
      let current = status.value
      if (!current?.downloaded) {
        downloading.value = true
        progress.value = 0
        current = await downloadUpdate()
        status.value = current
      }
      if (current?.downloaded && !current.error) {
        status.value = await applyUpdateAndRestart()
      }
    } catch {
      // Failure is in the log; the last known status stands.
    } finally {
      downloading.value = false
    }
  }

  return { status, progress, checking, downloading, init, check, relaunch }
})
