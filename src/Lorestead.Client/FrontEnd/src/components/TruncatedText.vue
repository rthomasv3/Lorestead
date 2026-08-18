<script setup>
import { ref, onUnmounted } from 'vue'
import { TooltipRoot, TooltipTrigger, TooltipPortal, TooltipContent } from 'reka-ui'

// An ellipsis-truncating span whose tooltip shows the full text, and only
// exists when the text is actually cut off. Overflow is measured at hover time
// rather than with a ResizeObserver: truncation changes with splitter drags and
// renames, and the answer only matters while the pointer is over the text.
// Hover-only and open-controlled for the same reasons as HoverTip; built on the
// raw primitives because HoverTip decides whether to open at mouseenter, before
// a wrapper could measure and flip `disabled`.
defineOptions({ inheritAttrs: false })

const props = defineProps({
  text: { type: String, required: true },
  side: { type: String, default: 'bottom' },
  delay: { type: Number, default: 600 },
})

const span = ref(null)
const open = ref(false)
let timer = null

// Function ref: the as-child clone makes a named ref oscillate on re-render
// (see BoardListRow).
function setSpan(el) {
  if (el) span.value = el
}

function onEnter() {
  clearTimeout(timer)
  if (span.value && span.value.scrollWidth > span.value.clientWidth) {
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
    <TooltipTrigger as-child>
      <span :ref="setSpan" v-bind="$attrs" class="truncate" @mouseenter="onEnter" @mouseleave="onLeave"
        @mousedown="onLeave">{{ text }}</span>
    </TooltipTrigger>
    <TooltipPortal>
      <TooltipContent :side="side" :side-offset="6"
        class="z-50 max-w-80 break-words rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
        {{ text }}
      </TooltipContent>
    </TooltipPortal>
  </TooltipRoot>
</template>
