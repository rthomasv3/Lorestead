<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useSettingsStore, ACCENTS } from '../stores/settingsStore'
import { getAbout, getLog } from '../services/systemService'
import SelectMenu from '../components/SelectMenu.vue'
import Toggle from '../components/Toggle.vue'
import Button from '../components/Button.vue'

const store = useSettingsStore()

const THEME_OPTIONS = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]
const ACCENT_SWATCHES = [
  { value: 'indigo', label: 'Indigo', dot: 'bg-indigo-500' },
  { value: 'violet', label: 'Violet', dot: 'bg-violet-500' },
  { value: 'blue', label: 'Blue', dot: 'bg-blue-500' },
  { value: 'cyan', label: 'Cyan', dot: 'bg-cyan-500' },
  { value: 'emerald', label: 'Emerald', dot: 'bg-emerald-500' },
  { value: 'rose', label: 'Rose', dot: 'bg-rose-500' },
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

const MD_TOGGLES = [
  { key: 'mdTables', label: 'Tables' },
  { key: 'mdTaskLists', label: 'Task lists' },
  { key: 'mdStrikethrough', label: 'Strikethrough' },
  { key: 'mdAutolinks', label: 'Autolinks' },
  { key: 'mdFootnotes', label: 'Footnotes' },
  { key: 'mdCodeHighlighting', label: 'Code highlighting' },
  { key: 'mdHighlight', label: 'Highlight (==mark==)' },
]

const about = ref(null)

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

const lastChecked = computed(() => {
  const value = store.application.lastUpdateCheckAt
  return value ? new Date(value).toLocaleString() : 'Never'
})

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

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Theme</span>
            <div class="w-40">
              <SelectMenu
                :model-value="store.application.theme"
                :options="THEME_OPTIONS"
                @update:model-value="store.saveApplication({ theme: $event })"
              />
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Accent</span>
            <div class="flex items-center gap-2">
              <button
                v-for="a in ACCENT_SWATCHES"
                :key="a.value"
                type="button"
                :title="a.label"
                :aria-label="a.label"
                :aria-pressed="activeAccent === a.value"
                class="size-6 rounded-full flex items-center justify-center ring-offset-2 ring-offset-surface transition"
                :class="[a.dot, activeAccent === a.value ? 'ring-2 ring-on-surface/40' : 'hover:ring-2 hover:ring-border']"
                @click="store.saveApplication({ accentColor: a.value })"
              >
                <i-lucide-check v-if="activeAccent === a.value" class="size-3.5 text-white" />
              </button>
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Date format</span>
            <div class="w-40">
              <SelectMenu
                :model-value="store.application.dateFormat"
                :options="DATE_FORMAT_OPTIONS"
                @update:model-value="store.saveApplication({ dateFormat: $event })"
              />
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Time format</span>
            <div class="w-40">
              <SelectMenu
                :model-value="store.application.timeFormat"
                :options="TIME_FORMAT_OPTIONS"
                @update:model-value="store.saveApplication({ timeFormat: $event })"
              />
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">History retention</span>
            <input
              v-model="historyRetention"
              type="number"
              min="10"
              max="100"
              class="w-24 text-sm bg-transparent border border-border rounded-md px-2 py-1.5 focus:outline-none focus:border-accent"
              @input="debounced('history', saveHistoryRetention)"
            />
            <span class="text-xs text-on-surface-muted">versions kept per item (10-100)</span>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Trash retention</span>
            <input
              v-model="trashRetentionDays"
              type="number"
              min="1"
              max="365"
              class="w-24 text-sm bg-transparent border border-border rounded-md px-2 py-1.5 focus:outline-none focus:border-accent"
              @input="debounced('trash', saveTrashRetention)"
            />
            <span class="text-xs text-on-surface-muted">days before deleted items purge</span>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">New note focus</span>
            <div class="w-40">
              <SelectMenu
                :model-value="store.application.newNoteFocus"
                :options="FOCUS_OPTIONS"
                @update:model-value="store.saveApplication({ newNoteFocus: $event })"
              />
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">New task focus</span>
            <div class="w-40">
              <SelectMenu
                :model-value="store.application.newTaskFocus"
                :options="FOCUS_OPTIONS"
                @update:model-value="store.saveApplication({ newTaskFocus: $event })"
              />
            </div>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Check for updates</span>
            <Toggle
              :model-value="store.application.autoCheckUpdates"
              @update:model-value="store.saveApplication({ autoCheckUpdates: $event })"
            />
            <span class="text-xs text-on-surface-muted">check automatically at startup</span>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Auto-update</span>
            <Toggle
              :model-value="store.application.autoUpdate"
              @update:model-value="store.saveApplication({ autoUpdate: $event })"
            />
            <span class="text-xs text-on-surface-muted">pre-download and apply on restart</span>
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0"></span>
            <Button disabled title="Available in packaged builds">
              <i-lucide-refresh-cw class="size-4" />
              Check for updates
            </Button>
            <span class="text-xs text-on-surface-muted">Last checked: {{ lastChecked }}</span>
          </div>
        </div>

        <div class="flex flex-col gap-3">
          <h2 id="settings-editor" class="text-sm font-semibold">Editor</h2>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Font size</span>
            <input
              v-model="fontSize"
              type="number"
              min="8"
              max="32"
              class="w-24 text-sm bg-transparent border border-border rounded-md px-2 py-1.5 focus:outline-none focus:border-accent"
              @input="debounced('fontSize', saveFontSize)"
            />
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Font family</span>
            <input
              v-model="fontFamily"
              type="text"
              spellcheck="false"
              placeholder="System monospace"
              class="w-64 text-sm bg-transparent border border-border rounded-md px-2 py-1.5 focus:outline-none focus:border-accent"
              @input="debounced('fontFamily', saveFontFamily)"
            />
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Spellcheck</span>
            <Toggle
              :model-value="store.editor.spellcheckEnabled"
              @update:model-value="store.saveEditor({ spellcheckEnabled: $event })"
            />
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Show line count</span>
            <Toggle
              :model-value="store.editor.showLineCount"
              @update:model-value="store.saveEditor({ showLineCount: $event })"
            />
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Highlight active line</span>
            <Toggle
              :model-value="store.editor.highlightActiveLine"
              @update:model-value="store.saveEditor({ highlightActiveLine: $event })"
            />
          </div>

          <div class="flex items-center gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0">Autosave debounce</span>
            <input
              v-model="autosaveDebounceMs"
              type="number"
              min="100"
              max="10000"
              step="100"
              class="w-24 text-sm bg-transparent border border-border rounded-md px-2 py-1.5 focus:outline-none focus:border-accent"
              @input="debounced('autosave', saveAutosaveDebounce)"
            />
            <span class="text-xs text-on-surface-muted">ms after typing stops (Ctrl+S saves immediately)</span>
          </div>

          <div class="flex items-start gap-3">
            <span class="text-sm text-on-surface-muted w-40 shrink-0 pt-0.5">Markdown extensions</span>
            <div class="flex flex-col gap-2">
              <label v-for="t in MD_TOGGLES" :key="t.key" class="flex items-center gap-2.5">
                <Toggle
                  :model-value="store.editor[t.key]"
                  @update:model-value="store.saveEditor({ [t.key]: $event })"
                />
                <span class="text-sm">{{ t.label }}</span>
              </label>
            </div>
          </div>
        </div>

        <div class="flex flex-col gap-3">
          <h2 id="settings-about" class="text-sm font-semibold">About</h2>
          <div class="flex items-center gap-3">
            <i-lucide-trees class="size-8 text-accent shrink-0" />
            <div class="flex flex-col">
              <span class="text-sm font-medium">{{ about?.appName ?? 'SylvaNote' }}</span>
              <span class="text-xs text-on-surface-muted">Version {{ about?.version ?? '...' }} - MIT License</span>
            </div>
          </div>
          <p class="text-xs text-on-surface-muted">
            Free and open source. Third-party notices ship with packaged builds.
          </p>
        </div>

        <div class="flex flex-col gap-3">
          <button
            type="button"
            class="flex items-center gap-2 text-sm font-semibold text-left"
            @click="toggleLog"
          >
            <i-lucide-chevron-right class="size-4 transition-transform" :class="logOpen ? 'rotate-90' : ''" />
            Logs
          </button>
          <div v-if="logOpen" class="flex flex-col gap-2">
            <div class="flex items-center gap-2">
              <Button size="icon" title="Refresh" @click="refreshLog">
                <i-lucide-refresh-cw class="size-4" />
              </Button>
              <span class="text-xs text-on-surface-muted">Application errors land here - there are no popups.</span>
            </div>
            <pre
              class="font-mono text-xs bg-surface-alt border border-border rounded-md p-3 max-h-96 overflow-auto whitespace-pre-wrap"
            >{{ logText || 'The log is empty.' }}</pre>
          </div>
        </div>

      </div>
    </div>
  </section>
</template>
