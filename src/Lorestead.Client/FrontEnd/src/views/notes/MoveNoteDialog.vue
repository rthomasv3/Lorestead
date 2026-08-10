<script setup>
import { ref, computed, watch } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import SelectMenu from '../../components/SelectMenu.vue'
import Button from '../../components/Button.vue'
import TextField from '../../components/TextField.vue'
import IconSearch from '~icons/lucide/search'
import { useNotesStore } from '../../stores/notesStore.js'

// Explicit-placement counterpart to tree drag-and-drop, and the only movement
// path on touch (drag registration is gated on a fine pointer). Picks a target
// and a zone, then emits the exact { target, zone } shape the drag path
// resolves - NotesTreePanel routes both through the same onDrop, so the two
// paths cannot disagree about move semantics.
const props = defineProps({
  open: { type: Boolean, default: false },
  // The tree item being moved (not just the note id: descendant exclusion and
  // onDrop both want the item shape).
  item: { type: Object, default: null },
})

const emit = defineEmits(['update:open', 'move'])

const notesStore = useNotesStore()
const query = ref('')
const targetId = ref(null)
const zone = ref('into')

watch(() => props.open, (open) => {
  if (open) {
    query.value = ''
    targetId.value = null
    zone.value = 'into'
  }
})

function collectIds(item, into) {
  into.add(item.id)
  for (const child of item.children ?? []) collectIds(child, into)
}

// Every legal destination with its breadcrumb trail: live notes (template
// notes included - moving under Templates is how a note becomes one) plus the
// Templates root itself. The moved note and its whole subtree are excluded
// (can't move a note into itself), as is the Trash (delete is its own action).
const candidates = computed(() => {
  if (!props.item) return []
  const excluded = new Set()
  collectIds(props.item, excluded)

  const out = []
  const walk = (items, trail) => {
    for (const it of items) {
      if (it.type === 'note' && !it.trashed && !excluded.has(it.id)) {
        const label = it.label || 'Untitled'
        out.push({ id: it.id, item: it, label, path: trail })
        walk(it.children ?? [], [...trail, label])
      } else if (it.type === 'templates-root') {
        out.push({ id: it.id, item: it, label: 'Templates', path: [], templatesRoot: true })
        walk(it.children ?? [], ['Templates'])
      }
    }
  }
  walk(notesStore.treeItems, [])
  return out
})

const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return candidates.value
  return candidates.value.filter((c) =>
    c.label.toLowerCase().includes(q) || c.path.some((p) => p.toLowerCase().includes(q)))
})

const selected = computed(() => candidates.value.find((c) => c.id === targetId.value) ?? null)

// The Templates root is one whole "make template" drop area - only Inside
// makes sense there, same as the drag path's resolveDrop.
const zoneOptions = computed(() => selected.value?.templatesRoot
  ? [{ value: 'into', label: 'Inside' }]
  : [
    { value: 'into', label: 'Inside' },
    { value: 'above', label: 'Above' },
    { value: 'below', label: 'Below' },
  ])

watch(selected, (target) => {
  if (target?.templatesRoot) zone.value = 'into'
})

const canMove = computed(() => selected.value !== null)

function move() {
  if (!canMove.value) return
  emit('move', { target: selected.value.item, zone: zone.value })
  emit('update:open', false)
}
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent
        class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-sm rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade"
        @keydown.enter="move">
        <DialogTitle class="font-semibold mb-1">Move note</DialogTitle>
        <p class="text-sm text-on-surface-muted mb-4 truncate">{{ item?.label || 'Untitled' }}</p>

        <label class="block text-sm text-on-surface-muted mb-1">Destination</label>
        <TextField v-model="query" size="small" :icon="IconSearch" placeholder="Search notes" class="w-full mb-2" />
        <div class="max-h-56 overflow-y-auto rounded-md border border-border p-1 mb-4">
          <button v-for="c in filtered" :key="c.id" type="button"
            class="w-full text-left rounded-md px-2.5 py-2 md:py-1.5 flex flex-col select-none-touch"
            :class="c.id === targetId ? 'bg-accent-soft' : 'hover:bg-hover-wash'" @click="targetId = c.id">
            <span v-if="c.path.length > 0" class="text-xs text-on-surface-muted truncate">
              {{ c.path.join(' › ') }}
            </span>
            <span class="text-sm truncate">{{ c.label }}</span>
          </button>
          <p v-if="filtered.length === 0" class="px-2.5 py-2 text-sm text-on-surface-muted">
            No matching notes
          </p>
        </div>

        <label class="block text-sm text-on-surface-muted mb-1">Position</label>
        <SelectMenu v-model="zone" :options="zoneOptions" />

        <div class="flex justify-end gap-2 mt-5">
          <Button variant="outline" @click="emit('update:open', false)">Cancel</Button>
          <Button variant="primary" :disabled="!canMove" @click="move">Move</Button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
