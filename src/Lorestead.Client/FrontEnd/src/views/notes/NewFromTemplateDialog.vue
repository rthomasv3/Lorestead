<script setup>
import { ref, computed, watch } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import SelectMenu from '../../components/SelectMenu.vue'
import Button from '../../components/Button.vue'
import TextField from '../../components/TextField.vue'
import { useNotesStore } from '../../stores/notesStore.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  parentId: { type: String, default: null },
})

const emit = defineEmits(['update:open', 'created'])

const notesStore = useNotesStore()
const name = ref('')
const templateId = ref('')

const templateOptions = computed(() =>
  notesStore.templateRootSummaries.map((t) => ({ value: t.id, label: t.title || 'Untitled' })))

watch(() => props.open, (open) => {
  if (open) {
    name.value = ''
    templateId.value = templateOptions.value[0]?.value ?? ''
  }
})

const canCreate = computed(() => name.value.trim().length > 0 && templateId.value)

async function create() {
  if (!canCreate.value) return
  const rootId = await notesStore.createFromTemplate({
    templateId: templateId.value,
    title: name.value.trim(),
    parentId: props.parentId,
  })
  emit('update:open', false)
  emit('created', rootId)
}
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent
        class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-sm rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade"
        @keydown.enter="create">
        <DialogTitle class="font-semibold mb-4">New note from template</DialogTitle>

        <label class="block text-sm text-on-surface-muted mb-1">Name</label>
        <TextField v-model="name" placeholder="Note name" class="w-full mb-4"
          :ref="(el) => el && open && el.focus()" />

        <label class="block text-sm text-on-surface-muted mb-1">Template</label>
        <SelectMenu v-if="templateOptions.length > 0" v-model="templateId" :options="templateOptions"
          placeholder="Select a template" />
        <p v-else class="text-sm text-on-surface-muted">
          No templates yet - drag a note into the Templates section to create one.
        </p>

        <div class="flex justify-end gap-2 mt-5">
          <Button variant="outline" @click="emit('update:open', false)">Cancel</Button>
          <Button variant="primary" :disabled="!canCreate" @click="create">Create</Button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
