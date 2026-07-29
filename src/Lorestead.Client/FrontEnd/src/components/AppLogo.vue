<script setup>
// The product mark. Two fixed brand colors baked into the artwork, so it is an
// <img> rather than a currentColor glyph - it must not retint with the accent.
// Callers set the size (`class="size-7"`); alt is empty because every call site
// puts the product name beside it.
import logoUrl from '@icon/icon.svg'
</script>

<template>
  <!-- max-w-none: preflight gives every img `max-width: 100%`, which silently
       overrode the size class and shrank the mark to its container whenever that
       was narrower - the collapsed sidebar rail. shrink-0 does not cover it;
       the cap is a max-width, not flex shrinking. -->
  <img :src="logoUrl" alt="" draggable="false" class="shrink-0 max-w-none" />
</template>
