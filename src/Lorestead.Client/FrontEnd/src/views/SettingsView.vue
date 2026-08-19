<script setup>
import { ref, computed, nextTick, onMounted, onBeforeUnmount } from 'vue'
import { useSettingsStore, ACCENTS } from '../stores/settingsStore'
import { useSyncStore } from '../stores/syncStore'
import { useUpdatesStore } from '../stores/updatesStore'
import { formatTimestamp } from '../utils/dateFormat.js'
import { getAbout, getLog, getThirdPartyNotices } from '../services/systemService'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import { MD_TOGGLES } from '../utils/settingsIndex.js'
import { useMobilePlatform } from '../composables/usePlatform.js'
import SelectMenu from '../components/SelectMenu.vue'
import SettingRow from '../components/SettingRow.vue'
import Toggle from '../components/Toggle.vue'
import Button from '../components/Button.vue'
import TextField from '../components/TextField.vue'
import AppLogo from '../components/AppLogo.vue'
import HoverTip from '../components/HoverTip.vue'

const store = useSettingsStore()
const sync = useSyncStore()
const updates = useUpdatesStore()
// Mobile updates ship through the app stores, so the whole updates group -
// toggles included, which would write settings nothing reads there - is hidden.
// Desktop keeps the disabled-with-tooltip button on unpackaged builds.
const mobilePlatform = useMobilePlatform()

const THEME_OPTIONS = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'parchment', label: 'Parchment' },
  { value: 'dark', label: 'Dark' },
]
const ACCENT_SWATCHES = [
  { value: 'indigo', label: 'Indigo', dot: 'bg-indigo-500' },
  { value: 'violet', label: 'Violet', dot: 'bg-violet-500' },
  { value: 'blue', label: 'Blue', dot: 'bg-blue-500' },
  { value: 'cyan', label: 'Cyan', dot: 'bg-cyan-500' },
  { value: 'emerald', label: 'Emerald', dot: 'bg-emerald-500' },
  { value: 'rose', label: 'Rose', dot: 'bg-rose-500' },
  { value: 'rust', label: 'Rust', dot: 'bg-[#b05f3d]' },
  { value: 'olive', label: 'Olive', dot: 'bg-[#6f8149]' },
]
const DATE_FORMAT_OPTIONS = [
  { value: 'yyyy-MM-dd', label: '2026-07-23' },
  { value: 'MM/dd/yyyy', label: '07/23/2026' },
  { value: 'dd/MM/yyyy', label: '23/07/2026' },
  { value: 'MMM d, yyyy', label: 'Jul 23, 2026' },
]
const TIME_FORMAT_OPTIONS = [
  { value: 'HH:mm', label: '14:30' },
  { value: 'h:mm tt', label: '2:30 PM' },
]
const FOCUS_OPTIONS = [
  { value: 'title', label: 'Title' },
  { value: 'body', label: 'Body' },
]

const activeAccent = computed(() =>
  ACCENTS.includes(store.application.accentColor) ? store.application.accentColor : 'indigo')

// Free-typed inputs debounce; everything else saves on change (no Save button anywhere).
const timers = {}
function debounced(key, fn) {
  clearTimeout(timers[key])
  timers[key] = setTimeout(fn, 600)
}
onBeforeUnmount(() => Object.values(timers).forEach(clearTimeout))

function clamp(value, min, max, fallback) {
  const n = Number(value)
  if (!Number.isFinite(n)) return fallback
  return Math.min(max, Math.max(min, Math.round(n)))
}

const historyRetention = ref('')
const trashRetentionDays = ref('')
const fontSize = ref('')
const fontFamily = ref('')
const autosaveDebounceMs = ref('')

function syncInputs() {
  historyRetention.value = String(store.application.historyRetention)
  trashRetentionDays.value = String(store.application.trashRetentionDays)
  fontSize.value = String(store.editor.fontSize)
  fontFamily.value = store.editor.fontFamily
  autosaveDebounceMs.value = String(store.editor.autosaveDebounceMs)
  syncServerUrl.value = sync.status?.serverUrl ?? store.application.serverUrl ?? ''
}

function saveHistoryRetention() {
  const value = clamp(historyRetention.value, 10, 100, store.application.historyRetention)
  historyRetention.value = String(value)
  store.saveApplication({ historyRetention: value })
}

function saveTrashRetention() {
  const value = clamp(trashRetentionDays.value, 1, 365, store.application.trashRetentionDays)
  trashRetentionDays.value = String(value)
  store.saveApplication({ trashRetentionDays: value })
}

function saveFontSize() {
  const value = clamp(fontSize.value, 8, 32, store.editor.fontSize)
  fontSize.value = String(value)
  store.saveEditor({ fontSize: value })
}

function saveFontFamily() {
  store.saveEditor({ fontFamily: fontFamily.value.trim() })
}

function saveAutosaveDebounce() {
  const value = clamp(autosaveDebounceMs.value, 100, 10000, store.editor.autosaveDebounceMs)
  autosaveDebounceMs.value = String(value)
  store.saveEditor({ autosaveDebounceMs: value })
}

// Sync inputs commit on blur/Enter, not while typing - a partial URL or token
// must never reach the engine and trigger an attempt against garbage.
const syncServerUrl = ref('')
const syncToken = ref('')
const tokenEditing = ref(false)
const tokenInputEl = ref(null)

function commitSyncServerUrl() {
  const url = syncServerUrl.value.trim()
  syncServerUrl.value = url
  if (url !== (sync.status?.serverUrl ?? '')) {
    sync.saveServerUrl(url)
  }
}

// The stored token is write-only: once set, the input collapses to a masked row
// with Replace/Remove, and an input only exists as a transient editing state.
function startTokenReplace() {
  tokenEditing.value = true
  nextTick(() => tokenInputEl.value?.focus())
}

function commitSyncToken() {
  const token = syncToken.value.trim()
  if (token) {
    sync.saveToken(token)
  }
  syncToken.value = ''
  tokenEditing.value = false
}

function cancelTokenEdit() {
  syncToken.value = ''
  tokenEditing.value = false
}

const about = ref(null)

const noticesOpen = ref(false)
const noticesText = ref('')
async function openNotices() {
  try {
    const result = await getThirdPartyNotices()
    noticesText.value = result?.text ?? ''
  } catch {
    // The dialog still opens; an empty body beats a dead link.
    noticesText.value = ''
  }
  noticesOpen.value = true
}

const logOpen = ref(false)
const logText = ref('')
async function toggleLog() {
  logOpen.value = !logOpen.value
  if (logOpen.value) await refreshLog()
}
async function refreshLog() {
  try {
    const result = await getLog()
    logText.value = result?.text ?? ''
  } catch {
    logText.value = ''
  }
}

// Status is the fresher source after a check; the settings column only covers
// the window before the first status arrives.
const lastChecked = computed(() => {
  const value = updates.status?.lastCheckedAt ?? store.application.lastUpdateCheckAt
  return value ? formatTimestamp(value, store.application.dateFormat, store.application.timeFormat) : 'Never'
})

const updatesBusy = computed(() => updates.checking || updates.downloading)

// A check that finds nothing must still say so - a clicked button that only
// bumps the timestamp reads as broken. Only claimed when a check has actually
// happened and nothing contradicts it.
const upToDate = computed(() =>
  !!updates.status?.supported && !updates.status?.updateAvailable && !updates.status?.error &&
  !!(updates.status?.lastCheckedAt ?? store.application.lastUpdateCheckAt))

// True whenever a download is staged: VelopackApp auto-applies staged updates
// at the next launch (on by default), independent of the auto-update toggle -
// the toggle only governs pre-downloading.
const autoApplyPending = computed(() => !!updates.status?.downloaded && !updates.downloading)

onMounted(async () => {
  syncInputs()
  try {
    about.value = await getAbout()
  } catch {
    // Version stays a placeholder if the backend is unreachable.
  }
})
</script>

<template>
  <section class="flex flex-col h-full min-h-0">
    <header class="flex items-center gap-3 px-5 h-12 border-b border-border shrink-0">
      <h1 class="font-semibold">Settings</h1>
    </header>

    <div class="overflow-y-auto p-5 min-h-0 flex-1">
      <div class="max-w-3xl mx-auto flex flex-col gap-8 pb-8">

        <div class="flex flex-col gap-3">
          <h2 id="settings-application" class="text-sm font-semibold">Application</h2>

          <SettingRow label="Theme">
            <div class="w-44">
              <SelectMenu :model-value="store.application.theme" :options="THEME_OPTIONS"
                @update:model-value="store.saveApplication({ theme: $event })" />
            </div>
          </SettingRow>

          <SettingRow label="Accent">
            <div class="flex items-center gap-2">
              <HoverTip v-for="a in ACCENT_SWATCHES" :key="a.value" :text="a.label" side="bottom">
                <button type="button" :aria-label="a.label" :aria-pressed="activeAccent === a.value"
                  class="size-6 rounded-full flex items-center justify-center ring-offset-2 ring-offset-surface transition"
                  :class="[a.dot, activeAccent === a.value ? 'ring-2 ring-on-surface/40' : 'hover:ring-2 hover:ring-border']"
                  @click="store.saveApplication({ accentColor: a.value })">
                  <i-lucide-check v-if="activeAccent === a.value" class="size-3.5 text-white" />
                </button>
              </HoverTip>
            </div>
          </SettingRow>

          <SettingRow label="Date format">
            <div class="w-44">
              <SelectMenu :model-value="store.application.dateFormat" :options="DATE_FORMAT_OPTIONS"
                @update:model-value="store.saveApplication({ dateFormat: $event })" />
            </div>
          </SettingRow>

          <SettingRow label="Time format">
            <div class="w-44">
              <SelectMenu :model-value="store.application.timeFormat" :options="TIME_FORMAT_OPTIONS"
                @update:model-value="store.saveApplication({ timeFormat: $event })" />
            </div>
          </SettingRow>

          <SettingRow label="History retention" hint="Versions kept per item (10-100)">
            <TextField v-model="historyRetention" type="number" min="10" max="100" class="w-24"
              @input="debounced('history', saveHistoryRetention)" />
          </SettingRow>

          <SettingRow label="Trash retention" hint="Days before deleted items purge">
            <TextField v-model="trashRetentionDays" type="number" min="1" max="365" class="w-24"
              @input="debounced('trash', saveTrashRetention)" />
          </SettingRow>

          <SettingRow label="New note focus">
            <div class="w-44">
              <SelectMenu :model-value="store.application.newNoteFocus" :options="FOCUS_OPTIONS"
                @update:model-value="store.saveApplication({ newNoteFocus: $event })" />
            </div>
          </SettingRow>

          <SettingRow label="New task focus">
            <div class="w-44">
              <SelectMenu :model-value="store.application.newTaskFocus" :options="FOCUS_OPTIONS"
                @update:model-value="store.saveApplication({ newTaskFocus: $event })" />
            </div>
          </SettingRow>

          <template v-if="!mobilePlatform">
            <SettingRow label="Check for updates" hint="Check automatically at startup">
              <Toggle :model-value="store.application.autoCheckUpdates"
                @update:model-value="store.saveApplication({ autoCheckUpdates: $event })" />
            </SettingRow>

            <SettingRow label="Auto-update" hint="Pre-download and apply on restart">
              <Toggle :model-value="store.application.autoUpdate"
                @update:model-value="store.saveApplication({ autoUpdate: $event })" />
            </SettingRow>

            <SettingRow>
              <Button v-if="updates.status?.supported" :disabled="updatesBusy" @click="updates.check()">
                <i-lucide-refresh-cw class="size-4" :class="updates.checking ? 'animate-spin' : ''" />
                Check for updates
              </Button>
              <HoverTip v-else text="Available in packaged builds" side="bottom" wrap>
                <Button disabled>
                  <i-lucide-refresh-cw class="size-4" />
                  Check for updates
                </Button>
              </HoverTip>
              <template #hint>Last checked: {{ lastChecked }}<template v-if="upToDate"> - You're on the latest version</template></template>
            </SettingRow>

            <SettingRow v-if="updates.status?.updateAvailable" :hint="`Version ${updates.status.version} available`">
              <Button :disabled="updatesBusy" @click="updates.relaunch()">
                <i-lucide-rotate-ccw class="size-4" />
                Relaunch to Update
              </Button>
            </SettingRow>

            <SettingRow v-if="updates.downloading">
              <div class="w-64 h-1.5 rounded-full bg-surface-alt border border-border overflow-hidden">
                <div class="h-full bg-accent-strong transition-[width] duration-200" :style="{ width: `${updates.progress}%` }" />
              </div>
              <span class="text-xs text-on-surface-muted tabular-nums">{{ updates.progress }}%</span>
            </SettingRow>

            <SettingRow v-if="autoApplyPending">
              <span class="text-xs text-on-surface-muted">The update will apply automatically on the next restart</span>
            </SettingRow>

            <SettingRow v-if="updates.status?.error">
              <span class="text-xs text-rose-500">{{ updates.status.error }}</span>
            </SettingRow>
          </template>
        </div>

        <div class="flex flex-col gap-3">
          <h2 id="settings-editor" class="text-sm font-semibold">Editor</h2>

          <SettingRow label="Font size">
            <TextField v-model="fontSize" type="number" min="8" max="32" class="w-24"
              @input="debounced('fontSize', saveFontSize)" />
          </SettingRow>

          <SettingRow label="Font family">
            <TextField v-model="fontFamily" type="text" spellcheck="false" placeholder="System monospace" class="w-64"
              @input="debounced('fontFamily', saveFontFamily)" />
          </SettingRow>

          <SettingRow label="Spellcheck">
            <Toggle :model-value="store.editor.spellcheckEnabled"
              @update:model-value="store.saveEditor({ spellcheckEnabled: $event })" />
          </SettingRow>

          <SettingRow label="Show line count">
            <Toggle :model-value="store.editor.showLineCount"
              @update:model-value="store.saveEditor({ showLineCount: $event })" />
          </SettingRow>

          <SettingRow label="Highlight active line">
            <Toggle :model-value="store.editor.highlightActiveLine"
              @update:model-value="store.saveEditor({ highlightActiveLine: $event })" />
          </SettingRow>

          <SettingRow label="Remember cursor position" hint="Reopen a note where you left off">
            <Toggle :model-value="store.editor.rememberCursorPosition"
              @update:model-value="store.saveEditor({ rememberCursorPosition: $event })" />
          </SettingRow>

          <SettingRow label="Autosave debounce" hint="Milliseconds after typing stops (Ctrl+S saves immediately)">
            <TextField v-model="autosaveDebounceMs" type="number" min="100" max="10000" step="100" class="w-24"
              @input="debounced('autosave', saveAutosaveDebounce)" />
          </SettingRow>

          <SettingRow label="Markdown extensions" align="start">
            <div class="flex flex-col gap-2">
              <label v-for="t in MD_TOGGLES" :key="t.key" class="flex items-center gap-2.5">
                <Toggle :model-value="store.editor[t.key]"
                  @update:model-value="store.saveEditor({ [t.key]: $event })" />
                <span class="text-sm">{{ t.label }}</span>
              </label>
            </div>
          </SettingRow>
        </div>

        <div class="flex flex-col gap-3">
          <h2 id="settings-sync" class="text-sm font-semibold">Sync server</h2>

          <SettingRow label="Status">
            <span class="size-2.5 rounded-full shrink-0" :class="sync.connected ? 'bg-emerald-500' : 'bg-rose-500'" />
            <span class="text-sm text-on-surface-muted min-w-0 break-words">{{ sync.label }}</span>
          </SettingRow>

          <SettingRow label="Server URL">
            <TextField v-model="syncServerUrl" type="text" spellcheck="false" placeholder="https://sync.example.com"
              class="w-80 max-w-full" @blur="commitSyncServerUrl" @keydown.enter.prevent="$event.target.blur()" />
          </SettingRow>

          <SettingRow label="Token">
            <template v-if="sync.status?.tokenSet && !tokenEditing">
              <span class="text-sm text-on-surface-muted w-39">••••••••</span>
              <Button @click="startTokenReplace">Replace</Button>
              <Button @click="sync.removeToken()">Remove</Button>
            </template>
            <TextField v-else ref="tokenInputEl" v-model="syncToken" type="password" spellcheck="false"
              placeholder="Paste the server token" class="w-80 max-w-full" @blur="commitSyncToken"
              @keydown.enter.prevent="$event.target.blur()" @keydown.esc="cancelTokenEdit" />
          </SettingRow>

          <SettingRow>
            <Button :disabled="!sync.status?.configured || !sync.status?.tokenSet || sync.syncing" @click="sync.sync()">
              <i-lucide-refresh-cw class="size-4" :class="sync.syncing ? 'animate-spin' : ''" />
              Sync now
            </Button>
          </SettingRow>
        </div>

        <div class="flex flex-col gap-3">
          <h2 id="settings-about" class="text-sm font-semibold">About</h2>
          <div class="flex items-center gap-3">
            <AppLogo class="size-10" />
            <div class="flex flex-col">
              <span class="text-sm font-medium">{{ about?.appName ?? 'Lorestead' }}</span>
              <span class="text-xs text-on-surface-muted">Version {{ about?.version ?? '...' }} - MIT License</span>
            </div>
          </div>
          <p class="text-xs text-on-surface-muted">
            Free and open source. View the
            <button type="button" class="underline hover:text-on-surface" @click="openNotices">third-party notices</button>
            for bundled software.
          </p>
        </div>

        <DialogRoot :open="noticesOpen" @update:open="noticesOpen = $event">
          <DialogPortal>
            <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
            <DialogContent
              class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-2xl max-h-[80vh] flex flex-col rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade">
              <div class="flex items-center gap-2 mb-3">
                <DialogTitle class="flex-1 font-semibold">Third-party notices</DialogTitle>
                <HoverTip text="Close" side="bottom">
                  <button type="button" class="p-2 rounded-md hover:bg-surface-alt" @click="noticesOpen = false">
                    <i-lucide-x class="size-4" />
                  </button>
                </HoverTip>
              </div>
              <pre
                class="flex-1 min-h-0 font-mono text-xs bg-surface-alt border border-border rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ noticesText || 'THIRD-PARTY-NOTICES.txt could not be read.' }}</pre>
            </DialogContent>
          </DialogPortal>
        </DialogRoot>

        <div class="flex flex-col gap-3">
          <button id="settings-logs" type="button" class="flex items-center gap-2 text-sm font-semibold text-left"
            @click="toggleLog">
            <i-lucide-chevron-right class="size-4 transition-transform" :class="logOpen ? 'rotate-90' : ''" />
            Logs
          </button>
          <div v-if="logOpen" class="flex flex-col gap-2">
            <div class="flex items-center gap-2">
              <span class="text-xs text-on-surface-muted">System logs and errors</span>
              <HoverTip text="Refresh" side="bottom">
                <Button size="icon" @click="refreshLog">
                  <i-lucide-refresh-cw class="size-4" />
                </Button>
              </HoverTip>
            </div>
            <pre
              class="font-mono text-xs bg-surface-alt border border-border rounded-md p-3 max-h-96 overflow-auto whitespace-pre-wrap">
                {{ logText || 'The log is empty.' }}
            </pre>
          </div>
        </div>

      </div>
    </div>
  </section>
</template>
