<script setup>
import { ref, computed, nextTick } from 'vue'
import { PopoverRoot, PopoverAnchor, PopoverPortal, PopoverContent } from 'reka-ui'

// Chips plus a typing input with a suggestion list: linked notes on a task,
// labels on a task, the label filter in the board header. The control owns
// the query, the highlight and the popover; the caller owns the values.
const props = defineProps({
  // Selected values, in order. Strings or ids - whatever `suggestions` carries.
  modelValue: { type: Array, default: () => [] },
  // Every candidate as { value, label }. Filtered by the query here; values
  // already selected are left out of the list.
  suggestions: { type: Array, default: () => [] },
  // Enter on text that matches no suggestion adds the text itself.
  allowNew: { type: Boolean, default: false },
  // Characters typed before the list opens. 0 opens it on focus.
  minQuery: { type: Number, default: 1 },
  maxSuggestions: { type: Number, default: 8 },
  // Off keeps a single line: chips scroll sideways inside the field instead
  // of stacking, for the board header where the row height is fixed.
  wrap: { type: Boolean, default: true },
  size: { type: String, default: 'normal' },
  placeholder: { type: String, default: '' },
})

const emit = defineEmits(['update:modelValue'])

const input = ref(null)
const field = ref(null)
const query = ref('')
const index = ref(0)
const focused = ref(false)
// Escape closes the list until the next keystroke; blur resets it.
const dismissed = ref(false)

// Mirrors fieldSize so this lines up beside a TextField of the same size; the
// wrapping variant needs a minimum rather than a fixed height. Chips and the
// input share one fixed row height, so the field is exactly its minimum
// whether empty or holding chips - left to line boxes, a chip's glyphs and
// icon rounded it up a pixel and the field grew on the first chip.
const SIZES = {
  normal: {
    field: 'min-h-9 px-2 py-1.5 gap-1.5 text-sm',
    chip: 'flex items-center gap-1 h-6 px-1.5 rounded bg-accent-soft text-sm shrink-0',
    input: 'h-6',
    row: 'text-sm',
  },
  small: {
    field: 'min-h-7 px-2 py-1 gap-1 text-xs',
    chip: 'flex items-center gap-1 h-5 px-1.5 rounded bg-accent-soft text-xs shrink-0',
    input: 'h-5',
    row: 'text-xs',
  },
}
const sizing = computed(() => SIZES[props.size] ?? SIZES.normal)

const trimmedQuery = computed(() => query.value.trim())

const labelByValue = computed(() => {
  const map = new Map()
  for (const s of props.suggestions) map.set(s.value, s.label)
  return map
})

const filtered = computed(() => {
  const q = trimmedQuery.value.toLowerCase()
  if (q.length < props.minQuery) return []
  return props.suggestions
    .filter((s) => !props.modelValue.includes(s.value) && (!q || String(s.label).toLowerCase().includes(q)))
    .slice(0, props.maxSuggestions)
})

// The "create" row sits last so Enter prefers an existing match while typing;
// with nothing matching it is the only row, so Enter creates.
const canCreate = computed(() => {
  const q = trimmedQuery.value
  if (!props.allowNew || !q) return false
  const lower = q.toLowerCase()
  return !props.suggestions.some((s) => String(s.label).toLowerCase() === lower)
    && !props.modelValue.some((v) => String(v).toLowerCase() === lower)
})

const rows = computed(() => {
  const list = filtered.value.map((s) => ({ kind: 'pick', value: s.value, label: s.label }))
  if (canCreate.value) list.push({ kind: 'create', value: trimmedQuery.value, label: trimmedQuery.value })
  return list
})

const open = computed(() => focused.value && !dismissed.value && rows.value.length > 0)

function labelFor(value) {
  return labelByValue.value.get(value) ?? String(value)
}

function add(value) {
  if (!props.modelValue.includes(value)) {
    emit('update:modelValue', [...props.modelValue, value])
  }
  query.value = ''
  index.value = 0
  scrollToEnd()
}

function remove(value) {
  emit('update:modelValue', props.modelValue.filter((v) => v !== value))
}

function choose(row) {
  add(row.value)
}

// Single-line mode: a new chip lands past the visible edge, so follow it.
async function scrollToEnd() {
  if (!props.wrap) {
    await nextTick()
    if (field.value) field.value.scrollLeft = field.value.scrollWidth
  }
}

function onInput() {
  dismissed.value = false
  index.value = 0
}

function onKeydown(e) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    dismissed.value = false
    index.value = Math.min(index.value + 1, rows.value.length - 1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    index.value = Math.max(index.value - 1, 0)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const row = open.value ? rows.value[index.value] : null
    if (row) choose(row)
  } else if (e.key === 'Escape' && open.value) {
    // Only swallowed while the list is showing - otherwise Escape still
    // reaches the dialog and closes it, as it does from any other field.
    e.stopPropagation()
    dismissed.value = true
  } else if (e.key === 'Backspace' && !query.value && props.modelValue.length > 0) {
    remove(props.modelValue[props.modelValue.length - 1])
  }
}

function onFocus() {
  focused.value = true
  dismissed.value = false
}

function onBlur() {
  focused.value = false
  dismissed.value = false
}

defineExpose({ focus: () => input.value?.focus() })
</script>

<template>
  <!-- A popover rather than an absolute dropdown: positioned inside a dialog's
       scroll area the list extended the scrollable content, so opening it
       scrolled the dialog and cut the list off. The portal takes it out of
       that flow entirely; focus stays in the input, so both auto-focus hops
       are suppressed. -->
  <PopoverRoot :open="open">
    <PopoverAnchor as-child>
      <div ref="field"
        class="flex items-center rounded-md border border-border focus-within:border-accent cursor-text"
        :class="[sizing.field, wrap ? 'flex-wrap' : 'flex-nowrap overflow-x-auto [scrollbar-width:none]']"
        @click="input?.focus()">
        <!-- The slot replaces the whole chip, so a caller can render nothing
             for a value it cannot resolve (a linked note that is not loaded)
             rather than an empty pill. -->
        <template v-for="value in modelValue" :key="value">
          <slot name="chip" :value="value" :label="labelFor(value)" :remove="() => remove(value)"
            :chip-class="sizing.chip">
            <span :class="sizing.chip">
              <span class="truncate max-w-48">{{ labelFor(value) }}</span>
              <button class="text-on-surface-muted hover:text-on-surface" aria-label="Remove" @click.stop="remove(value)">
                <i-lucide-x class="size-3" />
              </button>
            </span>
          </slot>
        </template>
        <input ref="input" v-model="query" :placeholder="placeholder"
          class="flex-1 min-w-24 bg-transparent outline-none placeholder:text-on-surface-muted/60"
          :class="sizing.input" @input="onInput" @keydown="onKeydown" @focus="onFocus" @blur="onBlur" />
      </div>
    </PopoverAnchor>
    <PopoverPortal>
      <PopoverContent side="bottom" align="start" :side-offset="4"
        class="z-[60] w-[var(--reka-popover-trigger-width)] rounded-lg border border-border bg-surface-elevated shadow-lg p-1 max-h-48 overflow-y-auto"
        @open-auto-focus.prevent @close-auto-focus.prevent>
        <!-- Selection follows the mouse, so hovering a row selects it and
             there is no separate hover state to paint. -->
        <button v-for="(row, i) in rows" :key="row.kind + String(row.value)"
          class="w-full text-left rounded-md px-2.5 py-1.5 truncate" :class="[sizing.row, i === index ? 'bg-accent-soft' : '']"
          @mouseenter="index = i" @mousedown.prevent="choose(row)">
          <template v-if="row.kind === 'create'">Create "{{ row.label }}"</template>
          <template v-else>{{ row.label }}</template>
        </button>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
