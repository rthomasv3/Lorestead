<script setup>
// The result rows shared by SearchDialog and SearchView. Renders only the
// buttons (fragment root) so the parent's scroll container owns the children -
// the dialog's keyboard navigation walks them by index.
//
// Two selection styles: the dialog drives selectedIndex (selection follows the
// mouse, no separate hover state); the mobile screen passes none (-1) and rows
// fall back to plain hover/tap feedback.
defineProps({
  results: { type: Array, required: true },
  selectedIndex: { type: Number, default: -1 },
  // Taller touch-friendly rows for the mobile screen.
  comfortable: { type: Boolean, default: false },
  titleParts: { type: Function, required: true },
  snippetParts: { type: Function, required: true },
})

defineEmits(['choose', 'select'])
</script>

<template>
  <button v-for="(result, index) in results" :key="result.key"
    class="w-full text-left rounded-md flex flex-col gap-0.5 select-none-touch"
    :class="[
      comfortable ? 'px-3 py-3' : 'px-2.5 py-2',
      index === selectedIndex ? 'bg-accent-soft' : selectedIndex < 0 ? 'hover:bg-hover-wash' : '',
    ]"
    @mousemove="$emit('select', index)" @click="$emit('choose', result)">
    <span class="flex items-center gap-1 text-sm min-w-0">
      <template v-for="(part, i) in result.breadcrumb" :key="i">
        <span v-if="i > 0" class="text-on-surface-muted/50 shrink-0">›</span>
        <span v-if="i === result.breadcrumb.length - 1" class="truncate">
          <template v-for="(piece, j) in titleParts(part)" :key="j"><span
              :class="piece.hit ? 'text-accent font-medium' : ''">{{ piece.text }}</span></template>
        </span>
        <span v-else class="text-on-surface-muted shrink-0">{{ part }}</span>
      </template>
    </span>
    <span v-if="result.snippet" class="text-xs text-on-surface-muted truncate">
      <template v-for="(piece, j) in snippetParts(result.snippet)" :key="j"><span
          :class="piece.hit ? 'text-accent' : ''">{{ piece.text }}</span></template>
    </span>
  </button>
</template>
