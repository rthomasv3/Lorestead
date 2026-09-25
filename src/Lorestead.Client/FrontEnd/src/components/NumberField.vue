<script setup>
import { computed, ref } from 'vue'
import { NumberFieldRoot, NumberFieldInput, NumberFieldDecrement, NumberFieldIncrement } from 'reka-ui'
import { fieldSize } from '../utils/fieldSizes.js'

// A number input with - and + either side, in TextField's box and heights. Reka
// does the work: clamping and step snapping, arrows / PageUp / PageDown / Home /
// End, hold-to-repeat on the buttons, and typed text committing only on blur or
// Enter. Buttons and keys commit on every step, so callers that save debounce.
const props = defineProps({
  modelValue: { type: Number, default: null },
  min: { type: Number, default: undefined },
  max: { type: Number, default: undefined },
  step: { type: Number, default: 1 },
  // Intl.NumberFormat options, e.g. { style: 'unit', unit: 'percent' } for "100%".
  // Grouping is off unless asked for: "1,000" reads as a list in a settings field.
  formatOptions: { type: Object, default: () => ({}) },
  size: { type: String, default: 'normal' },
  disabled: { type: Boolean, default: false },
})

const emit = defineEmits(['update:modelValue'])

const sizing = computed(() => fieldSize(props.size))
const format = computed(() => ({ useGrouping: false, ...props.formatOptions }))

// Clearing the text and leaving the field commits undefined, and the input stays
// blank. None of our values can be empty, so drop it and remount to show the last
// good value again. Reka also re-commits the unchanged value on every blur (the
// buttons focus the input), which would re-save for nothing - only changes go out.
const resetKey = ref(0)
function onUpdate(value) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    resetKey.value++
  } else if (value !== props.modelValue) {
    emit('update:modelValue', value)
  }
}

const BUTTON = 'shrink-0 flex items-center justify-center px-2 text-on-surface-muted hover:text-on-surface '
  + 'hover:bg-hover-wash disabled:opacity-40 disabled:pointer-events-none transition-colors'
</script>

<template>
  <!-- step-snapping off: step is what the buttons and arrows move by, and a typed
       value is kept as typed (750 ms stays 750 on a step-100 field), only clamped. -->
  <NumberFieldRoot :key="resetKey" :model-value="modelValue" :min="min" :max="max" :step="step"
    :step-snapping="false" :format-options="format" :disabled="disabled" @update:model-value="onUpdate"
    class="min-w-0 flex items-stretch rounded-md border border-border bg-surface-alt overflow-hidden focus-within:border-accent"
    :class="sizing.box">
    <NumberFieldDecrement :class="[BUTTON, 'border-r border-border']" aria-label="Decrease">
      <i-lucide-minus :class="sizing.icon" />
    </NumberFieldDecrement>
    <NumberFieldInput
      class="flex-1 w-full min-w-0 px-1 bg-transparent text-on-surface text-center tabular-nums outline-none" />
    <NumberFieldIncrement :class="[BUTTON, 'border-l border-border']" aria-label="Increase">
      <i-lucide-plus :class="sizing.icon" />
    </NumberFieldIncrement>
  </NumberFieldRoot>
</template>
