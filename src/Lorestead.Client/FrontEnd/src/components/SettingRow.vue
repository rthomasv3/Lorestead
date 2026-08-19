<script setup>
defineProps({
  // Omitted on continuation rows (a button or status line belonging to the row
  // above): desktop keeps the label column as a spacer so the control stays
  // aligned; mobile just stacks, where a fake indent would look like a mistake.
  label: { type: String, default: '' },
  hint: { type: String, default: '' },
  // 'start' for controls taller than one line, so the desktop label tops-align.
  align: { type: String, default: 'center' },
})
</script>

<!-- One settings row: label / control(s) / hint. Below md the three stack
     (label above, hint below - a sentence beside an input does not fit a
     phone); at md+ they sit inline with the fixed label column. The slot is
     wrapped in its own flex group so multi-control rows (token buttons,
     status dot + text) stay horizontal even when the row stacks. -->
<template>
  <div class="flex flex-col gap-1.5 md:flex-row md:gap-3" :class="align === 'start' ? 'md:items-start' : 'md:items-center'">
    <span v-if="label" class="text-sm text-on-surface-muted md:w-44 md:shrink-0"
      :class="align === 'start' ? 'md:pt-0.5' : ''">{{ label }}</span>
    <span v-else class="hidden md:block w-44 shrink-0" />
    <div class="flex items-center gap-3 min-w-0">
      <slot />
    </div>
    <span v-if="hint || $slots.hint" class="text-xs text-on-surface-muted">
      <slot name="hint">{{ hint }}</slot>
    </span>
  </div>
</template>
