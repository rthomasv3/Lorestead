<script setup>
import { ref, onUnmounted } from 'vue'
import { TooltipRoot, TooltipTrigger, TooltipPortal, TooltipContent } from 'reka-ui'

// Hover-only tooltip: Reka's default also opens on focus, which makes tooltips
// pop after closing a dialog (focus returns to the trigger) and while tabbing.
const props = defineProps({
  text: { type: String, required: true },
  side: { type: String, default: 'right' },
  disabled: { type: Boolean, default: false },
  delay: { type: Number, default: 600 },
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
      <slot />
    </TooltipTrigger>
    <TooltipPortal>
      <TooltipContent :side="side" :side-offset="6"
        class="z-50 rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
        {{ text }}
      </TooltipContent>
    </TooltipPortal>
  </TooltipRoot>
</template>
