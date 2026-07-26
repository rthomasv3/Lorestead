<script setup>
import { CONTROL_ACTIVE, CONTROL_GHOST } from '../utils/controlStates.js'

// A sidebar nav row: icon plus label, full width, active when its route is
// current. Shares its active and hover tones with Button (controlStates.js) -
// the row is a different shape from an icon button, but the same states.
defineProps({
  icon: { type: [Object, Function], default: null },
  // Empty when the sidebar is collapsed, same convention as TextField.
  label: { type: String, default: '' },
  active: { type: Boolean, default: false },
})
</script>

<template>
  <button type="button" class="w-full flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0"
    :class="active ? CONTROL_ACTIVE : CONTROL_GHOST">
    <component :is="icon" v-if="icon" class="size-5 shrink-0" />
    <span v-if="label" class="truncate">{{ label }}</span>
  </button>
</template>
