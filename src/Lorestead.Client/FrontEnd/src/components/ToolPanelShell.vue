<script setup>
import { ref, watch, onMounted } from 'vue'
import { useResizablePanel } from '../composables/useResizablePanel.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  // Which tool is showing. One shell holds every tool, so switching changes this
  // while `open` stays true: the panel keeps its width and the content
  // crossfades rather than collapsing and re-expanding (ui/overview.md).
  contentKey: { type: [String, Number], default: null },
  storageKey: { type: String, default: 'Lorestead-tool-panel-width' },
})

const { width, isDragging, onPointerDown } = useResizablePanel({
  defaultWidth: 280,
  minWidth: 200,
  maxWidth: 640,
  storageKey: props.storageKey,
})

// Content mounts only after the slide finishes so it never renders squished
// mid-animation (Vellerune ToolPanelShell pattern).
const contentVisible = ref(false)

watch(() => props.open, (isOpen) => {
  if (!isOpen) {
    contentVisible.value = false
  }
})

onMounted(() => {
  if (props.open) {
    contentVisible.value = true
  }
})

function onTransitionEnd(event) {
  if (event.propertyName === 'width' && props.open) {
    contentVisible.value = true
  }
}
</script>

<template>
  <div class="relative flex h-full shrink-0"
    :class="[open ? '' : 'w-0 overflow-hidden', isDragging ? '' : 'transition-[width] duration-250 ease-[cubic-bezier(0.32,0.72,0,1)]']"
    :style="open ? { width: width + 'px' } : { width: '0px' }" @transitionend="onTransitionEnd">
    <div class="absolute inset-y-0 left-0 w-2 -translate-x-1/2 z-10 cursor-ew-resize" @pointerdown="onPointerDown" />
    <div class="w-px shrink-0 h-full transition-colors" :class="isDragging ? 'bg-accent' : 'bg-border'" />
    <div class="flex flex-col h-full flex-1 min-w-0">
      <Transition name="tool-content" mode="out-in">
        <div v-if="contentVisible" :key="contentKey" class="flex flex-col h-full min-h-0">
          <slot />
        </div>
      </Transition>
    </div>
  </div>
</template>
