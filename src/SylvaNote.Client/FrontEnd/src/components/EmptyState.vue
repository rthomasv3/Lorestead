<script setup>
// The app's one empty state: a centered message, no icon, no border. The dashed
// box is not decoration for "nothing here" - it means the surface takes a drop,
// so only the two attachment lists set it, and not for a trashed note, which
// cannot be dropped on.
//
// Height is the caller's: `flex-1` to fill a panel, `h-full` a page pane,
// nothing at all where the box is sized by its own text.
defineProps({
  dropTarget: { type: Boolean, default: false },
})
</script>

<template>
  <div class="flex items-center justify-center text-center text-sm text-on-surface-muted p-4"
    :class="dropTarget ? 'rounded-md border border-dashed border-border' : ''">
    <slot />
  </div>
</template>
