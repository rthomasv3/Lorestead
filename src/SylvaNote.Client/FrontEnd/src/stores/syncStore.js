import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { getSyncStatus, saveSyncServerUrl, saveSyncToken, syncNow } from '../services/syncService.js'
import { useSettingsStore } from './settingsStore.js'
import { formatTimestamp } from '../utils/dateFormat.js'

export const useSyncStore = defineStore('sync', () => {
  const settings = useSettingsStore()
  const status = ref(null)
  const syncing = ref(false)

  const connected = computed(() => !!status.value?.connected && !status.value?.error)

  // The one place sync state surfaces (settings.md): error text beats everything,
  // then the setup states (muted, not failures), then the in-flight state, then
  // what the live connection knows. Every message is a short sentence-case state;
  // raw error detail lives in the log, never here.
  const label = computed(() => {
    const s = status.value
    let text = 'Not connected'
    if (s?.error) {
      text = s.error
    } else if (!s?.configured) {
      text = 'No server configured'
    } else if (!s?.tokenSet) {
      text = 'Token needed'
    } else if (s?.syncing) {
      text = 'Syncing...'
    } else if (connected.value && s?.lastSyncAt) {
      const app = settings.application
      text = `Last sync ${formatTimestamp(s.lastSyncAt, app.dateFormat, app.timeFormat)}`
    } else if (connected.value) {
      text = 'Not synced yet'
    }
    return text
  })

  async function init() {
    // The engine pushes status after every cycle; the initial fetch covers the
    // window before the first one.
    window.addEventListener('sync:status', (e) => {
      status.value = e.detail
    })
    try {
      status.value = await getSyncStatus()
    } catch {
      // DB unavailable - the label falls back to "not connected"; already logged.
    }
  }

  async function saveServerUrl(serverUrl) {
    try {
      status.value = await saveSyncServerUrl({ serverUrl })
    } catch {
      // Failure is in the log; the status label reflects the next cycle.
    }
  }

  async function saveToken(token) {
    try {
      status.value = await saveSyncToken({ token })
    } catch {
      // Failure is in the log; the status label reflects the next cycle.
    }
  }

  // An empty token removes the stored secret from the OS credential store.
  async function removeToken() {
    await saveToken('')
  }

  async function sync() {
    syncing.value = true
    try {
      status.value = await syncNow()
    } catch {
      // Failure is in the log; the status label reflects the next cycle.
    } finally {
      syncing.value = false
    }
  }

  return { status, syncing, connected, label, init, saveServerUrl, saveToken, removeToken, sync }
})
