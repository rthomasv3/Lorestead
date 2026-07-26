<script setup>
import { computed } from 'vue'
import { CONTROL_ACTIVE, CONTROL_GHOST } from '../utils/controlStates.js'

const props = defineProps({
  variant: { type: String, default: 'outline' }, // 'primary' | 'outline' | 'destructive' | 'ghost'
  size: { type: String, default: 'md' },         // 'md' | 'sm' | 'icon'
  // Toggles (tools rail, preview split) read as pressed rather than hovered, so
  // the active tint replaces the variant instead of layering on it.
  active: { type: Boolean, default: false },
})

const ACTIVE = CONTROL_ACTIVE

const VARIANTS = {
  primary: 'bg-accent-strong text-white enabled:hover:bg-accent-strong-hover shadow-sm',
  outline: 'border border-border enabled:hover:bg-surface-alt',
  destructive: 'bg-red-600 enabled:hover:bg-red-700 text-white',
  ghost: CONTROL_GHOST,
}

// sm is the text button that fits a h-10 panel header; icon is every icon-only
// action in the app - one padding value, so headers and toolbars agree.
const SIZES = {
  md: 'gap-1.5 px-2.5 py-1.5 text-sm',
  sm: 'gap-1 px-2 py-1 text-xs',
  icon: 'p-1.5',
}

const classes = computed(() =>
  `flex items-center rounded-md disabled:opacity-40 ${props.active ? ACTIVE : VARIANTS[props.variant]} ${SIZES[props.size]}`,
)
</script>

<template>
  <button type="button" :class="classes">
    <slot />
  </button>
</template>
