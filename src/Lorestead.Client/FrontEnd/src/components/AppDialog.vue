<script setup>
import { ref } from 'vue'
import { DialogRoot, DialogPortal, DialogOverlay, DialogContent } from 'reka-ui'
import { useIsMobile } from '../composables/useIsMobile.js'

defineOptions({ inheritAttrs: false })

defineProps({
  open: { type: Boolean, default: false },
})

const emit = defineEmits(['update:open'])

const isMobile = useIsMobile()
const content = ref(null)

// --- Swipe-down dismissal (mobile sheet) ---
//
// The drag starts only on the handle strip, never on the sheet body - the
// interiors scroll, and reclaiming a pull-down from an inner scroller is the
// expensive native trick this deliberately skips. Pointer events, not touch
// events, so a mouse drag in a narrow desktop window exercises the same code.
let dragPointer = null
let startY = 0
let lastY = 0
let lastTime = 0
let velocity = 0

function sheetEl() {
  return content.value?.$el
}

function onHandleDown(e) {
  dragPointer = e.pointerId
  startY = e.clientY
  lastY = e.clientY
  lastTime = e.timeStamp
  velocity = 0
  e.currentTarget.setPointerCapture(e.pointerId)
}

function onHandleMove(e) {
  const el = dragPointer === e.pointerId ? sheetEl() : null
  if (el) {
    const dt = e.timeStamp - lastTime
    if (dt > 0) {
      velocity = (e.clientY - lastY) / dt
    }
    lastY = e.clientY
    lastTime = e.timeStamp
    // Downward follows the finger 1:1; upward resists - the sheet is already
    // fully open, so pulling up should feel like tension, not movement.
    const dy = e.clientY - startY
    el.style.transition = 'none'
    el.style.transform = `translateY(${dy >= 0 ? dy : dy * 0.15}px)`
  }
}

function onHandleUp(e) {
  const el = dragPointer === e.pointerId ? sheetEl() : null
  if (el) {
    dragPointer = null
    const dy = e.clientY - startY
    // Distance OR a flick: a fast short pull should close without dragging a
    // third of the screen. Velocity is px/ms.
    if (dy > el.offsetHeight * 0.28 || (velocity > 0.5 && dy > 24)) {
      // The exit keyframe only declares `to`, so the slide-out continues from
      // the dragged position instead of snapping back first.
      emit('update:open', false)
    } else {
      el.style.transition = 'transform 200ms cubic-bezier(0.16, 1, 0.3, 1)'
      el.style.transform = ''
    }
  }
}

function onHandleCancel(e) {
  const el = dragPointer === e.pointerId ? sheetEl() : null
  if (el) {
    dragPointer = null
    el.style.transition = 'transform 200ms cubic-bezier(0.16, 1, 0.3, 1)'
    el.style.transform = ''
  }
}
</script>

<!-- Shell for LARGE dialogs (task edit, notices): desktop keeps the centered
     card, below md it becomes a bottom sheet - full width, slid up from the
     bottom edge, rounded top corners with the backdrop peeking above them so
     it reads as a sheet rather than a shrunk popup. Small confirms stay on
     their own centered ConfirmDialog; alerts are centered on phones too.
     Per-dialog sizing (md:max-w-*, md:max-h-*, padding) comes in through the
     fallthrough class; keydown listeners fall through the same way. -->
<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <DialogContent ref="content" v-bind="$attrs"
        class="fixed z-50 flex flex-col bg-surface-elevated border-border shadow-xl overflow-hidden dialog-sheet
               max-md:inset-x-0 max-md:bottom-0 max-md:top-[calc(var(--galdr-inset-top,0px)+1.5rem)] max-md:rounded-t-xl max-md:border-t
               md:left-1/2 md:top-1/2 md:-translate-x-1/2 md:-translate-y-1/2 md:w-full md:rounded-lg md:border">
        <!-- Absolutely positioned so the dialogs' own top padding is the pill's
             home and no per-dialog layout changes; center-limited width keeps
             it clear of close buttons in the corners. -->
        <div v-if="isMobile"
          class="absolute top-0 left-1/2 -translate-x-1/2 z-10 w-32 h-7 flex justify-center pt-2.5 touch-none select-none-touch"
          @pointerdown="onHandleDown" @pointermove="onHandleMove" @pointerup="onHandleUp"
          @pointercancel="onHandleCancel">
          <div class="w-9 h-1 rounded-full bg-on-surface-muted/30" />
        </div>
        <slot />
        <!-- The portal escapes App.vue's inset padding, so the sheet clears the
             home indicator itself. Zero-height everywhere insets are zero. -->
        <div v-if="isMobile" class="shrink-0 h-[var(--galdr-inset-bottom,0px)]" />
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
