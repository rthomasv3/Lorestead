<script setup>
import { ref, onUnmounted } from 'vue'
import { TooltipRoot, TooltipTrigger, TooltipPortal, TooltipContent } from 'reka-ui'

// Hover-only tooltip: Reka's default also opens on focus, which makes tooltips
// pop after closing a dialog (focus returns to the trigger) and while tabbing.
// Driving `open` from here rather than letting Reka's trigger do it is what buys
// that - and is also why the provider's delay props never apply to these.
const props = defineProps({
  text: { type: String, required: true },
  hotkey: { type: String, default: '' },
  side: { type: String, default: 'right' },
  disabled: { type: Boolean, default: false },
  delay: { type: Number, default: 600 },
  // A disabled control swallows its own mouse events, so a trigger bound
  // straight onto one never hears mouseenter - which is exactly where a tooltip
  // is needed, since it is what explains the greying out. `wrap` moves the
  // listeners to a span around it. Not the default: `as-child` leaves layout
  // alone, and a span in a flex row would take the utilities meant for the
  // control it wraps.
  wrap: { type: Boolean, default: false },
})

const open = ref(false)
let timer = null

function onEnter() {
  clearTimeout(timer)
  if (!props.disabled) {
    timer = setTimeout(() => {
      open.value = true
    }, props.delay)
  }
}

function onLeave() {
  clearTimeout(timer)
  open.value = false
}

onUnmounted(() => clearTimeout(timer))
</script>

<template>
  <TooltipRoot :open="open">
    <TooltipTrigger as-child @mouseenter="onEnter" @mouseleave="onLeave" @mousedown="onLeave">
      <!-- shrink-0: the span becomes the flex item in place of the control, and a
           control that was sized to hold its own text should not start losing to
           whatever it shares the row with because a tooltip was added. -->
      <span v-if="wrap" class="inline-flex shrink-0">
        <slot />
      </span>
      <slot v-else />
    </TooltipTrigger>
    <TooltipPortal>
      <TooltipContent :side="side" :side-offset="6"
        class="z-50 flex items-center gap-2 rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
        {{ text }}
        <kbd v-if="hotkey" class="text-on-surface-muted">{{ hotkey }}</kbd>
      </TooltipContent>
    </TooltipPortal>
  </TooltipRoot>
</template>
