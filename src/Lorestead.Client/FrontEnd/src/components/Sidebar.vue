<script setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotesStore } from '../stores/notesStore.js'
import { useBoardsStore } from '../stores/boardsStore.js'
import HoverTip from './HoverTip.vue'
import TextField from './TextField.vue'
import NavButton from './NavButton.vue'
import AppLogo from './AppLogo.vue'
import { shortcut } from '../utils/platform.js'
import IconNotebookText from '~icons/lucide/notebook-text'
import IconSquareKanban from '~icons/lucide/square-kanban'
import IconSettings from '~icons/lucide/settings'
import IconSearch from '~icons/lucide/search'
import IconChevronLeft from '~icons/lucide/chevron-left'
import IconChevronRight from '~icons/lucide/chevron-right'

const STORAGE_KEY = 'Lorestead-sidebar-collapsed'

const route = useRoute()
const router = useRouter()
const notesStore = useNotesStore()
const boardsStore = useBoardsStore()
const collapsed = ref(localStorage.getItem(STORAGE_KEY) === '1')

function toggle() {
  collapsed.value = !collapsed.value
  localStorage.setItem(STORAGE_KEY, collapsed.value ? '1' : '0')
}

// Selection rides in the route, so returning to a section goes back through the
// last selected item's route - the stores still hold the last id after the view
// unmounts, which is what keeps "come back and it's still open" working.
const items = computed(() => [
  {
    section: '/notes',
    to: notesStore.selectedId ? `/notes/${notesStore.selectedId}` : '/notes',
    label: 'Notes',
    icon: IconNotebookText,
  },
  {
    section: '/boards',
    to: boardsStore.selectedBoardId ? `/boards/${boardsStore.selectedBoardId}` : '/boards',
    label: 'Boards',
    icon: IconSquareKanban,
  },
])

function isActive(section) {
  return route.path === section || route.path.startsWith(section + '/')
}

function openSearch() {
  // The search dialog listens for this; same path as Ctrl+K.
  window.dispatchEvent(new CustomEvent('search:open'))
}
</script>

<template>
  <!-- Collapsed is exactly one row wide (px-2 on the column plus a 1px border
       and px-2.5 inside the row, either side of a size-4 cell, plus the nav's
       own right border = 55px), so the icon column lands centered in the rail
       without a per-state alignment class. Every label stays mounted at every
       width and never shrinks - each row clips its own - so the text slides
       out under the row's edge as the rail animates instead of disappearing
       in the frame the rail starts moving. -->
  <nav
    class="hidden md:flex shrink-0 border-r border-border flex-col bg-surface transition-[width] duration-200 ease-out overflow-hidden"
    :class="collapsed ? 'w-[3.4375rem]' : 'w-52'">
    <!-- Same geometry as a nav row - px-2 outside, px-2.5 and a 1px border
         inside - so the header sits on the sidebar's one icon column. The mark
         is deliberately larger than a nav glyph, so it is centered in a size-4
         cell and allowed to spill past it: that puts the logo on the icons'
         vertical axis and the wordmark on the labels', instead of picking one.
         The spill clears the clip, which cuts at the padding box. -->
    <div class="h-12 px-2 shrink-0 flex items-center">
      <div class="min-w-0 w-full flex items-center gap-3 px-2.5 border border-transparent overflow-hidden">
        <span class="w-4 shrink-0 flex justify-center">
          <AppLogo class="size-7" />
        </span>
        <span class="text-lg font-semibold shrink-0 whitespace-nowrap">Lorestead</span>
      </div>
    </div>

    <div class="flex-1 flex flex-col gap-1 px-2 py-2 min-h-0">
      <!-- The field box does its own clipping so the label and hotkey slide out
           under its border rather than escaping it into the rail. The tooltip
           repeats the key because collapsed is exactly the state where the
           field's own has been clipped away - it only opens collapsed. -->
      <HoverTip text="Search" :hotkey="shortcut('mod', 'K')" :disabled="!collapsed">
        <TextField as="button" :icon="IconSearch" class="w-full shrink-0 mb-3 overflow-hidden" label="Search"
          :hotkey="shortcut('mod', 'K')" @click="openSearch" />
      </HoverTip>

      <HoverTip v-for="item in items" :key="item.section" :text="item.label" :disabled="!collapsed">
        <NavButton :icon="item.icon" :label="item.label" :active="isActive(item.section)" @click="router.push(item.to)" />
      </HoverTip>

      <div class="flex-1" />

      <HoverTip text="Settings" :disabled="!collapsed">
        <NavButton :icon="IconSettings" label="Settings" :active="isActive('/settings')"
          @click="router.push('/settings')" />
      </HoverTip>
    </div>

    <!-- The collapse control is a nav row like any other, tooltip included: a
         native `title` would open a second, differently styled tooltip beside
         the Reka one. The border belongs to the band around it so it still
         spans the full width. The label reads "Collapse" in both states -
         swapping the visible word would flicker on the way out - so collapsed,
         where the rail has clipped that label, the tooltip and the accessible
         name are what carry the real action. -->
    <div class="shrink-0 border-t border-border px-2 py-1.5">
      <HoverTip text="Expand" :disabled="!collapsed">
        <NavButton :icon="collapsed ? IconChevronRight : IconChevronLeft" label="Collapse"
          :aria-label="collapsed ? 'Expand' : 'Collapse'" @click="toggle" />
      </HoverTip>
    </div>
  </nav>
</template>
