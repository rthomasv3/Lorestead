<script setup>
import { ref, computed, watch } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import SelectMenu from '../../components/SelectMenu.vue'
import Button from '../../components/Button.vue'
import { pickImportFile, pickImportFolder, previewImport, runImport } from '../../services/importService.js'
import { useNotesStore } from '../../stores/notesStore.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  // Preselects the destination - the tree context menu imports into the
  // right-clicked note.
  parentId: { type: String, default: null },
})

const emit = defineEmits(['update:open'])

// Reka's SelectItem rejects an empty-string value (reserved for clearing), so
// root gets a sentinel like the store's __templates/__trash rows.
const ROOT_ID = '__root'

const notesStore = useNotesStore()
const destinationId = ref(ROOT_ID)
const preflight = ref(null)
const report = ref(null)
const picking = ref(false)
const importing = ref(false)
const error = ref('')

watch(() => props.open, (open) => {
  if (open) {
    destinationId.value = props.parentId ?? ROOT_ID
    preflight.value = null
    report.value = null
    error.value = ''
  }
})

function isTemplate(summary) {
  return summary.type === 1 || summary.type === 'template' || summary.type === 'Template'
}

// The live note tree flattened to indented titles; merged notes stay put and
// templates land in the template section, so the destination is for new notes.
const destinationOptions = computed(() => {
  const options = [{ value: ROOT_ID, label: 'Notes (root)' }]
  const byParent = new Map()
  for (const summary of notesStore.summaries) {
    if (summary.deleted || isTemplate(summary)) continue
    const key = summary.parentId ?? null
    if (!byParent.has(key)) byParent.set(key, [])
    byParent.get(key).push(summary)
  }
  const walk = (parentId, depth) => {
    for (const summary of byParent.get(parentId) ?? []) {
      options.push({ value: summary.id, label: summary.title || 'Untitled', depth })
      walk(summary.id, depth + 1)
    }
  }
  walk(null, 1)
  return options
})

function plural(count, word) {
  return `${count} ${word}${count === 1 ? '' : 's'}`
}

const preflightText = computed(() => {
  if (!preflight.value) return ''
  const p = preflight.value
  let text = `Found ${plural(p.noteCount, 'note')}, ${plural(p.attachmentCount, 'attachment')}`
  if (p.templateCount > 0) text += `, ${plural(p.templateCount, 'template')}`
  return text + '.'
})

// The part that answers "what will Import actually do here" - it follows the
// destination, since the merge scope does.
const actionText = computed(() => {
  if (!preflight.value) return ''
  const p = preflight.value
  const parts = []
  if (p.createdCount > 0) parts.push(`${p.createdCount} new`)
  if (p.mergedCount > 0) parts.push(`${p.mergedCount} updating existing notes`)
  if (p.skippedCount > 0) parts.push(`${p.skippedCount} unchanged`)
  return parts.join(', ')
})

// Destination changes recompute the preflight; the token drops stale replies
// if the select is changed faster than the backend answers.
let previewToken = 0
watch(destinationId, async () => {
  if (preflight.value?.selected && !importing.value) {
    const token = ++previewToken
    const response = await previewImport(destinationId.value === ROOT_ID ? null : destinationId.value)
    if (token === previewToken && response.selected) {
      preflight.value = response
    }
  }
})

const reportText = computed(() => {
  if (!report.value) return ''
  const r = report.value
  let text = `Created ${plural(r.created, 'note')}, merged ${r.merged}, skipped ${r.skipped} unchanged.`
  const extras = []
  if (r.attachmentCount > 0) extras.push(plural(r.attachmentCount, 'attachment'))
  if (r.templateCount > 0) extras.push(plural(r.templateCount, 'template'))
  if (extras.length > 0) text += ` Imported ${extras.join(' and ')}.`
  return text
})

async function choose(pick) {
  picking.value = true
  error.value = ''
  try {
    const response = await pick(destinationId.value === ROOT_ID ? null : destinationId.value)
    if (response.selected) {
      preflight.value = response
    }
  } catch {
    error.value = 'Could not read the selection. See Settings > Logs.'
  } finally {
    picking.value = false
  }
}

async function run() {
  importing.value = true
  error.value = ''
  try {
    report.value = await runImport(destinationId.value === ROOT_ID ? null : destinationId.value)
    await notesStore.load()
  } catch {
    // The transaction rolled back; the log has the details.
    error.value = 'Import failed and nothing was changed. See Settings > Logs.'
  } finally {
    importing.value = false
  }
}

function onOpenChange(open) {
  if (!importing.value) {
    emit('update:open', open)
  }
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent
        class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-md rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade">
        <DialogTitle class="font-semibold mb-4">Import notes</DialogTitle>

        <template v-if="!report">
          <div class="flex gap-2 mb-1">
            <Button variant="outline" class="flex-1" :disabled="picking || importing"
              @click="choose(pickImportFile)">
              Choose file...
            </Button>
            <Button variant="outline" class="flex-1" :disabled="picking || importing"
              @click="choose(pickImportFolder)">
              Choose folder...
            </Button>
          </div>
          <p class="text-xs text-on-surface-muted mb-3 truncate" :title="preflight?.path">
            {{ preflight ? preflight.path : 'A .zip or .md file, or a folder of markdown.' }}
          </p>

          <template v-if="preflight">
            <p class="text-sm mb-1">{{ preflightText }}</p>
            <p class="text-sm text-on-surface-muted mb-3">{{ actionText }}</p>
          </template>

          <label class="block text-sm text-on-surface-muted mb-1">Import into</label>
          <SelectMenu v-model="destinationId" :options="destinationOptions" class="mb-3" />

          <p v-if="error" class="text-sm text-red-500 mb-2">{{ error }}</p>

          <div class="flex justify-end gap-2 mt-4">
            <Button variant="outline" :disabled="importing" @click="emit('update:open', false)">Cancel</Button>
            <Button variant="primary" :disabled="!preflight || picking || importing" @click="run">
              {{ importing ? 'Importing...' : 'Import' }}
            </Button>
          </div>
        </template>

        <template v-else>
          <p class="text-sm mb-3">{{ reportText }}</p>

          <template v-if="report.warnings?.length">
            <p class="text-sm text-on-surface-muted mb-1">{{ plural(report.warnings.length, 'warning') }}:</p>
            <ul class="text-xs text-on-surface-muted border border-border rounded-md p-2 mb-3 max-h-40 overflow-y-auto space-y-1">
              <li v-for="(warning, index) in report.warnings" :key="index">{{ warning }}</li>
            </ul>
          </template>

          <div class="flex justify-end mt-4">
            <Button variant="primary" @click="emit('update:open', false)">Done</Button>
          </div>
        </template>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
