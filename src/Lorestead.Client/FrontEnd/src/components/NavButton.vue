<script setup>
import { CONTROL_ACTIVE, CONTROL_GHOST } from '../utils/controlStates.js'

// A sidebar nav row: icon plus label, full width, active when its route is
// current. Shares its active and hover tones with Button (controlStates.js) -
// the row is a different shape from an icon button, but the same states.
//
// The icon column matches TextField's normal size (size-4 + gap-3) so every
// label down the sidebar starts on the same vertical line; the search entry is
// a TextField and would otherwise sit 4px off from the rows under it. The
// transparent border matches that field's real one - without it every row below
// the search box sits a pixel to its left.
// The label is mounted at every sidebar width: the row clips it and it never
// shrinks, so collapsing slides the text out under the row's own edge. That
// costs truncation - a label longer than the row overflows instead of
// ellipsizing - which the sidebar's four fixed labels never reach.
defineProps({
  icon: { type: [Object, Function], default: null },
  label: { type: String, default: '' },
  active: { type: Boolean, default: false },
})
</script>

<template>
  <button type="button"
    class="w-full flex items-center gap-3 rounded-md border border-transparent px-2.5 h-9 text-sm shrink-0 overflow-hidden"
    :class="active ? CONTROL_ACTIVE : CONTROL_GHOST">
    <component :is="icon" v-if="icon" class="size-4 shrink-0" />
    <span v-if="label" class="shrink-0 whitespace-nowrap">{{ label }}</span>
  </button>
</template>
