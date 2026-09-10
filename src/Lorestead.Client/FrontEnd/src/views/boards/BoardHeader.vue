<script setup>
import { ref, computed, watch, onUnmounted } from 'vue'
import IconSearch from '~icons/lucide/search'
import TextField from '../../components/TextField.vue'
import ChipInput from '../../components/ChipInput.vue'
import LabelChip from '../../components/LabelChip.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

// Desktop header above the selected board, the same height as the board list
// header beside it: the search field and the label filter, nothing else.
const boardsStore = useBoardsStore()

// The field owns a draft and writes it to the store on a short debounce - the
// match is an in-memory scan, but settling the columns per keystroke reads as
// flicker. A store reset (board switch) clears the draft the other way.
const query = ref(boardsStore.filterQuery)
let timer = null

watch(query, (value) => {
  clearTimeout(timer)
  timer = setTimeout(() => (boardsStore.filterQuery = value), 200)
})

watch(() => boardsStore.filterQuery, (value) => {
  if (value !== query.value) {
    clearTimeout(timer)
    query.value = value
  }
})

onUnmounted(() => clearTimeout(timer))

function onKeydown(e) {
  if (e.key === 'Escape' && query.value) {
    e.stopPropagation()
    query.value = ''
  }
}

// Only labels on this board are offered - the filter can't ask for what isn't
// there. No creating from the filter, and the list is short enough to show whole.
const labelSuggestions = computed(() =>
  boardsStore.boardLabels.map((label) => ({ value: label, label })))
</script>

<template>
  <div class="flex items-center gap-2 px-2 h-page-header shrink-0 border-b border-border">
    <TextField v-model="query" size="small" :icon="IconSearch" placeholder="Filter tasks" class="w-56"
      @keydown="onKeydown" />
    <!-- Single line: chips scroll inside the field rather than stacking, so the
         header keeps its height however many labels are picked. -->
    <ChipInput :model-value="boardsStore.filterLabels" :suggestions="labelSuggestions" :min-query="0"
      :max-suggestions="50" :wrap="false" size="small" placeholder="Labels" class="w-64 bg-surface-alt"
      @update:model-value="(list) => (boardsStore.filterLabels = list)">
      <template #chip="{ value, remove, chipClass }">
        <LabelChip :label="value" removable :class="chipClass" @remove="remove()" />
      </template>
    </ChipInput>
  </div>
</template>
