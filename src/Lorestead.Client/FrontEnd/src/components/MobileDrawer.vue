<script setup>
import { ref } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent, DialogTitle } from 'reka-ui'
import Button from './Button.vue'

// Right-side drawer for mobile chrome (notes tools, board filters). The header
// slot fills the row beside the close X; the default slot is the body.
const props = defineProps({
  open: { type: Boolean, default: false },
  // Screen-reader name only - the visible header is whatever the slot renders.
  title: { type: String, required: true },
})

const emit = defineEmits(['update:open'])

function close() {
  emit('update:open', false)
}

// Swipe-to-close, on the whole drawer surface: its interiors scroll vertically,
// so horizontal intent cannot collide with them - whichever axis wins the
// first 8px owns the gesture (touch-pan-y leaves vertical with the browser).
// This is why the drawer needs no grab handle while the bottom sheets do:
// their interiors scroll in the dismiss axis.
const content = ref(null)

let pointer = null
let dragging = false
let swallowClick = false
let startX = 0
let startY = 0
let lastX = 0
let lastTime = 0
let velocity = 0

function contentEl() {
  return content.value?.$el
}

function onPointerDown(e) {
  pointer = e.pointerId
  dragging = false
  swallowClick = false
  startX = e.clientX
  startY = e.clientY
  lastX = e.clientX
  lastTime = e.timeStamp
  velocity = 0
}

function onPointerMove(e) {
  const el = e.pointerId === pointer ? contentEl() : null
  if (el) {
    const dx = e.clientX - startX
    const dy = e.clientY - startY
    if (!dragging) {
      if (Math.abs(dx) > 8 && Math.abs(dx) > Math.abs(dy)) {
        dragging = true
        // The tap that would land on whatever the finger started on must not
        // fire after a drag.
        swallowClick = true
        el.setPointerCapture(e.pointerId)
      } else if (Math.abs(dy) > 8) {
        pointer = null
      }
    }
    if (dragging) {
      const dt = e.timeStamp - lastTime
      if (dt > 0) {
        velocity = (e.clientX - lastX) / dt
      }
      lastX = e.clientX
      lastTime = e.timeStamp
      // Rightward follows the finger; leftward resists - the drawer is already
      // fully open.
      el.style.transition = 'none'
      el.style.transform = `translateX(${dx >= 0 ? dx : dx * 0.15}px)`
    }
  }
}

function onPointerUp(e) {
  const el = e.pointerId === pointer ? contentEl() : null
  pointer = null
  if (el && dragging) {
    dragging = false
    const dx = e.clientX - startX
    // Distance OR a flick; the exit keyframe only declares `to`, so the
    // slide-out continues from the dragged position.
    if (dx > el.offsetWidth * 0.28 || (velocity > 0.5 && dx > 24)) {
      close()
    } else {
      el.style.transition = 'transform 200ms cubic-bezier(0.16, 1, 0.3, 1)'
      el.style.transform = ''
    }
  }
}

function onPointerCancel(e) {
  const el = e.pointerId === pointer ? contentEl() : null
  pointer = null
  if (el && dragging) {
    dragging = false
    el.style.transition = 'transform 200ms cubic-bezier(0.16, 1, 0.3, 1)'
    el.style.transform = ''
  }
}

function onClickCapture(e) {
  if (swallowClick) {
    swallowClick = false
    e.preventDefault()
    e.stopPropagation()
  }
}
</script>

<template>
  <!-- Portaled to the body, so it consumes the safe-area insets itself -
       App.vue's pt-safe does not reach it. The top offset is the raw inset
       variable: on a phone the drawer starts below the status bar, on desktop
       the variable is 0 and it runs full height. -->
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent ref="content"
        class="fixed right-0 bottom-0 top-[var(--galdr-inset-top,0px)] z-50 w-[85%] max-w-sm bg-surface border-l border-t border-border flex flex-col pb-safe outline-none drawer-slide touch-pan-y"
        @pointerdown="onPointerDown" @pointermove="onPointerMove" @pointerup="onPointerUp"
        @pointercancel="onPointerCancel" @click.capture="onClickCapture">
        <DialogTitle class="sr-only">{{ title }}</DialogTitle>
        <!-- justify-between with the same px-3 as the tool panels' own headers:
             the first icon and the close X line up with the panel title and its
             + button on the row below. -->
        <div class="flex items-center justify-between px-3 h-page-header shrink-0 border-b border-border">
          <slot name="header" />
          <Button variant="ghost" size="icon" aria-label="Close" @click="close">
            <i-lucide-x class="size-4" />
          </Button>
        </div>
        <div class="flex-1 min-h-0">
          <slot />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
