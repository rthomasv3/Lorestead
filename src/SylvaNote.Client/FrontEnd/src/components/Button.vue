<script setup>
import { computed } from 'vue'

const props = defineProps({
  variant: { type: String, default: 'outline' }, // 'primary' | 'outline' | 'destructive' | 'ghost'
  size: { type: String, default: 'md' },         // 'md' | 'icon' | 'xs'
})

const VARIANTS = {
  primary: 'bg-accent-strong text-white enabled:hover:bg-accent-strong-hover shadow-sm',
  outline: 'border border-border enabled:hover:bg-surface-alt',
  destructive: 'bg-red-600 enabled:hover:bg-red-700 text-white',
  ghost: 'text-on-surface-muted enabled:hover:bg-surface-alt enabled:hover:text-on-surface',
}

const SIZES = {
  md: 'gap-1.5 px-2.5 py-1.5 text-sm',
  icon: 'p-1.5',
  xs: 'p-1',
}

const classes = computed(() =>
  `flex items-center rounded-md disabled:opacity-40 ${VARIANTS[props.variant]} ${SIZES[props.size]}`,
)
</script>

<template>
  <button type="button" :class="classes">
    <slot />
  </button>
</template>
