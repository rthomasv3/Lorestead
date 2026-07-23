<script setup>
defineProps({
  modelValue: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
})
defineEmits(['update:modelValue'])
</script>

<template>
  <button
    type="button"
    role="switch"
    :aria-checked="modelValue"
    :disabled="disabled"
    class="relative h-5 w-9 rounded-full transition-colors shrink-0 disabled:opacity-40"
    :class="modelValue ? 'bg-accent-strong' : 'bg-surface-alt border border-border'"
    @click="$emit('update:modelValue', !modelValue)"
  >
    <span
      class="absolute top-0.5 left-0.5 size-4 rounded-full bg-surface shadow transition-transform"
      :class="modelValue ? 'translate-x-4' : ''"
    />
  </button>
</template>
