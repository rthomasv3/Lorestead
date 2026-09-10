<script setup>
import { ref, computed, watch } from 'vue'
import IconSearch from '~icons/lucide/search'
import MobileDrawer from '../../components/MobileDrawer.vue'
import TextField from '../../components/TextField.vue'
import ChipInput from '../../components/ChipInput.vue'
import LabelChip from '../../components/LabelChip.vue'
import Button from '../../components/Button.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

// Mobile counterpart of BoardHeader: the filter edits a local draft and only
// Apply writes it to the store, so the board behind the drawer holds still
// while the user types. Clear drops both the draft and the applied filter.
const props = defineProps({
  open: { type: Boolean, default: false },
})

const emit = defineEmits(['update:open'])

const boardsStore = useBoardsStore()
const query = ref('')
const labels = ref([])

watch(() => props.open, (value) => {
  if (value) {
    query.value = boardsStore.filterQuery
    labels.value = [...boardsStore.filterLabels]
  }
})

const labelSuggestions = computed(() =>
  boardsStore.boardLabels.map((label) => ({ value: label, label })))

function apply() {
  boardsStore.filterQuery = query.value
  boardsStore.filterLabels = [...labels.value]
  emit('update:open', false)
}

function clear() {
  query.value = ''
  labels.value = []
  boardsStore.clearFilter()
  emit('update:open', false)
}
</script>

<template>
  <MobileDrawer :open="open" title="Filter tasks" @update:open="emit('update:open', $event)">
    <template #header>
      <span class="text-sm font-medium">Filter</span>
    </template>
    <div class="h-full flex flex-col">
      <div class="flex-1 min-h-0 overflow-y-auto p-3 flex flex-col gap-3">
        <TextField v-model="query" :icon="IconSearch" placeholder="Search title and description"
          @keydown.enter="apply" />
        <!-- Room to wrap here, unlike the desktop header. -->
        <ChipInput :model-value="labels" :suggestions="labelSuggestions" :min-query="0" :max-suggestions="50"
          placeholder="Labels" class="bg-surface-alt" @update:model-value="(list) => (labels = list)">
          <template #chip="{ value, remove, chipClass }">
            <LabelChip :label="value" removable :class="chipClass" @remove="remove()" />
          </template>
        </ChipInput>
      </div>
      <div class="flex items-center justify-end gap-2 p-3 shrink-0 border-t border-border">
        <Button variant="outline" @click="clear">Clear</Button>
        <Button variant="primary" @click="apply">Apply</Button>
      </div>
    </div>
  </MobileDrawer>
</template>
