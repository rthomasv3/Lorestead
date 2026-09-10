<script setup>
import { computed } from 'vue'
import { labelStyle } from '../utils/labelColors.js'

// One coloured pill for a task label. Layout (height, padding, text size) is
// the caller's: a card wants a tiny pill, the chip inputs pass their row class.
const props = defineProps({
  label: { type: String, required: true },
  removable: { type: Boolean, default: false },
})

const emit = defineEmits(['remove'])

const style = computed(() => labelStyle(props.label))
</script>

<template>
  <span class="label-chip inline-flex items-center gap-1 rounded shrink-0 max-w-full" :style="style" :title="label">
    <span class="truncate">{{ label }}</span>
    <button v-if="removable" class="opacity-70 hover:opacity-100" aria-label="Remove label"
      @click.stop="emit('remove')">
      <i-lucide-x class="size-3" />
    </button>
  </span>
</template>
