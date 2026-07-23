<script setup>
import { inject, computed } from 'vue'
import { ContextMenuRoot, ContextMenuTrigger, ContextMenuPortal, ContextMenuContent } from 'reka-ui'

const props = defineProps({
  item: { type: Object, required: true },
  depth: { type: Number, default: 0 },
  parentId: { type: [String, Number, null], default: null },
})

const tree = inject('tree')

const childrenKey = tree.childrenKey
const children = computed(() => props.item[childrenKey])
const hasChildren = computed(() => Array.isArray(children.value) && children.value.length > 0)
const isExpandable = computed(() => hasChildren.value || !!props.item.expandable)
const expanded = computed(() => tree.expanded.value.has(props.item.id))
const selected = computed(() => tree.selectedId.value === props.item.id)
const editing = computed(() => tree.editingId.value === props.item.id)
const dropTarget = computed(() => tree.dropTargetId.value?.targetId === props.item.id)
const focused = computed(() => tree.focusedId.value === props.item.id)
const contextOpen = computed(() => tree.contextMenuItemId.value === props.item.id)
const dragged = computed(() => tree.draggedId.value === props.item.id)
const hasContext = computed(() => tree.hasContextMenu(props.item))

const indent = computed(() => `${0.75 + props.depth * 1}rem`)

function setRef(el) {
  tree.setRowRef(props.item.id, el)
}

function onClick() {
  tree.click(props.item)
}

function onDblClick() {
  tree.startEditing(props.item)
}

function onMousedown(e) {
  tree.onRowMousedown(props.item, e)
}

function onChevronClick(e) {
  tree.onChevronClick(props.item, e)
}

function onContextOpenChange(open) {
  tree.onContextMenuOpenChange(props.item.id, open)
}
</script>

<template>
  <div role="treeitem" :aria-expanded="isExpandable ? expanded : undefined" :aria-selected="selected" class="relative">
    <ContextMenuRoot v-if="hasContext" @update:open="onContextOpenChange">
      <ContextMenuTrigger as-child>
        <button :ref="setRef" :tabindex="focused ? 0 : -1" @mousedown="onMousedown" @click="onClick" @dblclick="onDblClick"
          class="group w-full text-left py-1.5 pr-3 transition-colors flex items-center gap-2" :class="[
            contextOpen ? 'bg-on-surface/5' : dropTarget ? 'bg-accent/10' : selected ? 'bg-accent/10' : 'hover:bg-surface-alt',
            dragged ? 'opacity-30' : '',
          ]" :style="{ paddingLeft: indent }">
          <i-lucide-chevron-right v-if="isExpandable" aria-hidden="true" @click="onChevronClick"
            class="w-3 h-3 shrink-0 transition-transform text-on-surface-muted" :class="expanded ? 'rotate-90' : ''" />
          <span v-else class="w-3 h-3 shrink-0" aria-hidden="true" />
          <slot name="item" :item="item" :depth="depth" :expanded="expanded" :selected="selected" :editing="editing"
            :commit-edit="tree.commitEdit" :cancel-edit="tree.cancelEdit" :on-edit-blur="tree.onEditBlur" />
          <span v-if="dropTarget && item.dropLabel" class="ml-auto text-xs text-accent shrink-0">{{ item.dropLabel }}</span>
        </button>
      </ContextMenuTrigger>
      <ContextMenuPortal>
        <ContextMenuContent
          class="bg-surface-elevated border border-border text-on-surface rounded-lg shadow-lg p-1 min-w-40 z-50"
          @closeAutoFocus.prevent>
          <slot name="context-menu" :item="item" :depth="depth" />
        </ContextMenuContent>
      </ContextMenuPortal>
    </ContextMenuRoot>
    <button v-else :ref="setRef" :tabindex="focused ? 0 : -1" @mousedown="onMousedown" @click="onClick" @dblclick="onDblClick"
      class="group w-full text-left py-1.5 pr-3 transition-colors flex items-center gap-2" :class="[
        dropTarget ? 'bg-accent/10' : selected ? 'bg-accent/10' : 'hover:bg-surface-alt',
        dragged ? 'opacity-30' : '',
      ]" :style="{ paddingLeft: indent }">
      <i-lucide-chevron-right v-if="isExpandable" aria-hidden="true" @click="onChevronClick"
        class="w-3 h-3 shrink-0 transition-transform text-on-surface-muted" :class="expanded ? 'rotate-90' : ''" />
      <span v-else class="w-3 h-3 shrink-0" aria-hidden="true" />
      <slot name="item" :item="item" :depth="depth" :expanded="expanded" :selected="selected" :editing="editing"
        :commit-edit="tree.commitEdit" :cancel-edit="tree.cancelEdit" :on-edit-blur="tree.onEditBlur" />
      <span v-if="dropTarget && item.dropLabel" class="ml-auto text-xs text-accent shrink-0">{{ item.dropLabel }}</span>
    </button>
    <div v-if="tree.lineIndicator.value?.itemId === item.id"
      class="absolute left-0 right-0 h-0.5 bg-accent z-10 pointer-events-none"
      :class="tree.lineIndicator.value.edge === 'top' ? '-top-px' : '-bottom-px'" />
  </div>

  <!-- Children -->
  <div v-if="expanded && (hasChildren || item.emptyLabel)" role="group">
    <TreeNode v-for="child in children" :key="child.id" :item="child" :depth="depth + 1" :parent-id="item.id">
      <template #item="slotProps">
        <slot name="item" v-bind="slotProps" />
      </template>
      <template #context-menu="slotProps">
        <slot name="context-menu" v-bind="slotProps" />
      </template>
    </TreeNode>
    <div v-if="!hasChildren && item.emptyLabel" class="py-1.5 pr-3 text-sm text-on-surface-muted/60"
      :style="{ paddingLeft: `${0.75 + (depth + 1) * 1}rem` }">
      {{ item.emptyLabel }}
    </div>
  </div>
</template>
