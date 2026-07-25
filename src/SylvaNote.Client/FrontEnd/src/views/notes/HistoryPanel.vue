<script setup>
import { ref, computed, watch } from 'vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { useSettingsStore } from '../../stores/settingsStore.js'
import { formatTimestamp } from '../../utils/dateFormat.js'
import { unifiedDiff, changeCounts } from '../../utils/diff.js'

const props = defineProps({
  // The live editor buffer, not the stored note - the diff has to show what
  // restoring would undo, including edits still inside the save debounce.
  currentBody: { type: String, default: '' },
})

const notesStore = useNotesStore()
const settingsStore = useSettingsStore()

const selected = ref(null)
const expandedGaps = ref(new Set())

// A version belongs to one note; switching notes must drop back to the list rather
// than diffing the old note's text against the new note's editor.
watch(() => notesStore.selectedId, () => {
  selected.value = null
})

// Cards are classified here because the frontend already holds every version's
// title and body (decisions.md): body changed -> preview and counts; title only
// -> a rename card; neither -> hidden, since move, reorder, trash and restore
// have nothing to render and a restore would not undo them anyway.
const cards = computed(() => {
  const versions = notesStore.currentHistory
  const result = []
  for (let i = 0; i < versions.length; i += 1) {
    const version = versions[i]
    // The list is newest first, so a version's predecessor is the next one along.
    const previous = versions[i + 1] ?? null
    const bodyChanged = !previous || version.body !== previous.body
    const titleChanged = previous && version.title !== previous.title

    if (bodyChanged) {
      result.push({
        version,
        kind: 'edit',
        preview: preview(version.body),
        counts: previous ? changeCounts(previous.body, version.body) : null,
      })
    } else if (titleChanged) {
      result.push({ version, kind: 'rename', from: previous.title, to: version.title })
    }
  }
  return result
})

const diffRows = computed(() =>
  (selected.value ? unifiedDiff(selected.value.body, props.currentBody) : []))

function preview(body) {
  const flat = (body ?? '').replace(/\s+/g, ' ').trim()
  return flat.length > 90 ? `${flat.slice(0, 90)}...` : flat
}

function stamp(iso) {
  const app = settingsStore.application
  return formatTimestamp(iso, app.dateFormat, app.timeFormat)
}

function open(version) {
  selected.value = version
  expandedGaps.value = new Set()
}

function back() {
  selected.value = null
}

function toggleGap(index) {
  const next = new Set(expandedGaps.value)
  if (next.has(index)) next.delete(index)
  else next.add(index)
  expandedGaps.value = next
}
</script>

<template>
  <div class="h-full flex flex-col min-h-0 relative overflow-hidden">
    <!-- List -->
    <div class="absolute inset-0 flex flex-col min-h-0 transition-transform duration-250 ease-[cubic-bezier(0.32,0.72,0,1)]"
      :class="selected ? '-translate-x-full' : 'translate-x-0'">
      <div class="flex items-center px-3 h-10 shrink-0 border-b border-border">
        <span class="text-sm font-medium">History</span>
      </div>

      <div class="flex-1 min-h-0 overflow-y-auto p-2 flex flex-col gap-1.5">
        <button v-for="card in cards" :key="card.version.id"
          class="text-left rounded-md border border-border bg-surface-alt/40 px-2.5 py-2 hover:border-accent hover:bg-accent-soft/40"
          @click="open(card.version)">
          <div class="flex items-center gap-2 min-w-0">
            <span class="text-[11px] text-on-surface-muted truncate">{{ stamp(card.version.changedAt) }}</span>
            <span v-if="card.version.supersededConcurrent"
              class="shrink-0 px-1 rounded bg-surface-alt text-[10px] leading-4 text-amber-500" title="Overwrote a concurrent edit">
              Conflict
            </span>
          </div>

          <p v-if="card.kind === 'edit'" class="text-xs text-on-surface-muted mt-1 line-clamp-2">
            {{ card.preview || 'Empty note' }}
          </p>
          <p v-else class="text-xs text-on-surface-muted mt-1 truncate">
            Renamed {{ card.from || 'Untitled' }} &rarr; {{ card.to || 'Untitled' }}
          </p>

          <div v-if="card.counts" class="flex items-center gap-2 mt-1 text-[11px] font-mono">
            <span v-if="card.counts.added > 0" class="text-green-500">+{{ card.counts.added }}</span>
            <span v-if="card.counts.removed > 0" class="text-red-500">-{{ card.counts.removed }}</span>
          </div>
        </button>

        <div v-if="cards.length === 0"
          class="flex-1 flex items-center justify-center text-center text-sm text-on-surface-muted/60 rounded-md border border-dashed border-border m-1 p-4">
          No earlier versions yet
        </div>
      </div>
    </div>

    <!-- Version detail -->
    <div class="absolute inset-0 flex flex-col min-h-0 bg-surface transition-transform duration-250 ease-[cubic-bezier(0.32,0.72,0,1)]"
      :class="selected ? 'translate-x-0' : 'translate-x-full'">
      <div class="flex items-center gap-1.5 px-2 h-10 shrink-0 border-b border-border">
        <button class="p-1 rounded text-on-surface-muted hover:text-on-surface hover:bg-surface-alt" title="Back"
          @click="back">
          <i-lucide-chevron-left class="size-4" />
        </button>
        <span class="text-sm font-medium truncate">{{ selected ? stamp(selected.changedAt) : '' }}</span>
      </div>

      <div class="flex-1 min-h-0 overflow-auto p-2 font-mono text-xs leading-relaxed">
        <div v-if="selected && selected.title" class="mb-2 px-1.5 py-1 rounded bg-surface-alt/60 truncate">
          <span class="text-on-surface-muted">Title:</span> {{ selected.title }}
        </div>

        <template v-for="(row, index) in diffRows" :key="index">
          <button v-if="row.kind === 'gap' && !expandedGaps.has(index)"
            class="w-full text-left px-1.5 py-0.5 my-0.5 rounded text-on-surface-muted/70 hover:bg-surface-alt"
            @click="toggleGap(index)">
            &ctdot; {{ row.lines.length }} unchanged {{ row.lines.length === 1 ? 'line' : 'lines' }}
          </button>
          <template v-else-if="row.kind === 'gap'">
            <div v-for="(line, i) in row.lines" :key="`${index}-${i}`"
              class="px-1.5 whitespace-pre-wrap break-words text-on-surface-muted">{{ line || ' ' }}</div>
          </template>

          <div v-else-if="row.kind === 'context'"
            class="px-1.5 whitespace-pre-wrap break-words text-on-surface-muted">{{ row.text || ' ' }}</div>

          <div v-else class="px-1.5 whitespace-pre-wrap break-words"
            :class="row.kind === 'added' ? 'bg-green-500/12' : 'bg-red-500/12'">
            <span class="select-none text-on-surface-muted/60">{{ row.kind === 'added' ? '+' : '-' }} </span>
            <span v-for="(segment, i) in row.segments" :key="i"
              :class="segment.changed ? (row.kind === 'added' ? 'bg-green-500/35' : 'bg-red-500/35') : ''">{{ segment.text }}</span>
          </div>
        </template>

        <div v-if="selected && diffRows.length === 0" class="px-1.5 text-on-surface-muted/60">
          Identical to the current note
        </div>
      </div>
    </div>
  </div>
</template>
