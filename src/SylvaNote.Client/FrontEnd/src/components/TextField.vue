<script setup>
import { computed, ref, useAttrs } from 'vue'
import { fieldSize } from '../utils/fieldSizes.js'

defineOptions({ inheritAttrs: false })

const props = defineProps({
  modelValue: { type: String, default: '' },
  placeholder: { type: String, default: '' },
  // 'input' takes typing; 'button' renders static `label` text and emits click.
  // The sidebar's search entry looks like a field but opens the dialog, and it
  // has to stay visually identical to the filter field beside it.
  as: { type: String, default: 'input' },
  label: { type: String, default: '' },
  hotkey: { type: String, default: '' },
  // A lucide icon component, or null for the plain form fields.
  icon: { type: [Object, Function], default: null },
  size: { type: String, default: 'normal' },
})

const emit = defineEmits(['update:modelValue'])
const attrs = useAttrs()
const inputEl = ref(null)

const sizing = computed(() => fieldSize(props.size))
const isButton = computed(() => props.as === 'button')

// class/style style the field box - callers set widths on it. Everything else
// (type, min, max, step, spellcheck, listeners) belongs to the control inside.
const controlAttrs = computed(() => {
  const { class: _class, style: _style, ...rest } = attrs
  return rest
})

// A component ref points at the instance, not the input, so focus is explicit.
defineExpose({ focus: () => inputEl.value?.focus() })
</script>

<template>
  <component :is="isButton ? 'button' : 'div'" :type="isButton ? 'button' : undefined"
    v-bind="isButton ? controlAttrs : {}"
    class="min-w-0 flex items-center rounded-md border border-border bg-surface-alt text-on-surface-muted"
    :class="[sizing.field, attrs.class, isButton ? 'hover:text-on-surface' : 'focus-within:border-accent']">
    <component :is="icon" v-if="icon" class="shrink-0" :class="sizing.icon" />

    <span v-if="isButton" class="truncate">{{ label }}</span>
    <input v-else ref="inputEl" v-bind="controlAttrs" :value="modelValue" :placeholder="placeholder"
      class="w-full min-w-0 bg-transparent text-on-surface outline-none placeholder:text-on-surface-muted/60"
      @input="emit('update:modelValue', $event.target.value)" />

    <kbd v-if="hotkey" class="ml-auto shrink-0" :class="sizing.hotkey">{{ hotkey }}</kbd>
  </component>
</template>
