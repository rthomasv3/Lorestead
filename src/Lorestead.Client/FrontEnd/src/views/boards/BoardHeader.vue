<script setup>
import { ref, watch, onUnmounted } from 'vue'
import IconSearch from '~icons/lucide/search'
import TextField from '../../components/TextField.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

// Desktop header above the selected board, the same height as the board list
// header beside it. Holds the search field now; the label filter joins it later.
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
</script>

<template>
  <!-- Controls only: the board list beside this header already shows which
       board is selected. Search sits at the left, the label filter joins it. -->
  <div class="flex items-center gap-2 px-2 h-page-header shrink-0 border-b border-border">
    <TextField v-model="query" size="small" :icon="IconSearch" placeholder="Filter tasks" class="w-56"
      @keydown="onKeydown" />
  </div>
</template>
