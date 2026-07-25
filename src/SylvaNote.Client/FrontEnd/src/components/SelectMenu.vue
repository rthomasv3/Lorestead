<script setup>
import { computed } from 'vue'
import {
  SelectRoot,
  SelectTrigger,
  SelectValue,
  SelectIcon,
  SelectPortal,
  SelectContent,
  SelectViewport,
  SelectItem,
  SelectItemIndicator,
  SelectItemText,
} from 'reka-ui'
import { fieldSize } from '../utils/fieldSizes.js'

const props = defineProps({
  modelValue: { type: String, default: '' },
  options: { type: Array, default: () => [] }, // [{ value: string, label: string }]
  placeholder: { type: String, default: 'Select' },
  size: { type: String, default: 'normal' },
})
defineEmits(['update:modelValue'])

// Shared with TextField so a select and an input in the same row line up.
const sizing = computed(() => fieldSize(props.size))

// Render the label ourselves so the trigger always reflects the selection even before the item
// list (popper) has mounted.
const selected = computed(() => props.options.find((o) => o.value === props.modelValue))
</script>

<template>
  <SelectRoot :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)">
    <SelectTrigger
      class="flex w-full items-center justify-between rounded-md border border-border bg-surface-alt outline-none focus:border-accent data-[placeholder]:text-on-surface-muted"
      :class="sizing.field"
    >
      <SelectValue :placeholder="placeholder">
        <span v-if="selected" class="truncate">{{ selected.label }}</span>
      </SelectValue>
      <SelectIcon>
        <i-lucide-chevron-down class="size-4 text-on-surface-muted" />
      </SelectIcon>
    </SelectTrigger>

    <SelectPortal>
      <SelectContent
        :side-offset="6"
        position="popper"
        class="z-[60] min-w-[var(--reka-select-trigger-width)] overflow-hidden rounded-md border border-border bg-surface-elevated shadow-lg"
      >
        <SelectViewport class="max-h-72 overflow-y-auto p-1">
          <SelectItem
            v-for="o in options"
            :key="o.value"
            :value="o.value"
            class="flex cursor-pointer items-center justify-between gap-2 rounded px-2 py-1.5 text-sm outline-none data-highlighted:bg-accent/10 data-[state=checked]:text-accent"
          >
            <SelectItemText>{{ o.label }}</SelectItemText>
            <SelectItemIndicator>
              <i-lucide-check class="size-4" />
            </SelectItemIndicator>
          </SelectItem>
        </SelectViewport>
      </SelectContent>
    </SelectPortal>
  </SelectRoot>
</template>
