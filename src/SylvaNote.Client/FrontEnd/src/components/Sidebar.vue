<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TooltipProvider } from 'reka-ui'
import HoverTip from './HoverTip.vue'
import TextField from './TextField.vue'
import NavButton from './NavButton.vue'
import AppLogo from './AppLogo.vue'
import IconNotebookText from '~icons/lucide/notebook-text'
import IconSquareKanban from '~icons/lucide/square-kanban'
import IconSettings from '~icons/lucide/settings'
import IconSearch from '~icons/lucide/search'
import IconChevronLeft from '~icons/lucide/chevron-left'
import IconChevronRight from '~icons/lucide/chevron-right'

const STORAGE_KEY = 'SylvaNote-sidebar-collapsed'

const route = useRoute()
const router = useRouter()
const collapsed = ref(localStorage.getItem(STORAGE_KEY) === '1')

function toggle() {
  collapsed.value = !collapsed.value
  localStorage.setItem(STORAGE_KEY, collapsed.value ? '1' : '0')
}

const items = [
  { to: '/notes', label: 'Notes', icon: IconNotebookText },
  { to: '/boards', label: 'Boards', icon: IconSquareKanban },
]

function isActive(to) {
  return route.path === to || route.path.startsWith(to + '/')
}

function openSearch() {
  // The search dialog listens for this; same path as Ctrl+K.
  window.dispatchEvent(new CustomEvent('search:open'))
}
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <nav
      class="shrink-0 border-r border-border flex flex-col bg-surface transition-[width] duration-200 ease-out overflow-hidden"
      :class="collapsed ? 'w-14' : 'w-52'">
      <!-- Same geometry as a nav row - px-2 outside, px-2.5 and a 1px border
           inside - so the header sits on the sidebar's one icon column. The mark
           is deliberately larger than a nav glyph, so it is centered in a size-4
           cell and allowed to spill past it: that puts the logo on the icons'
           vertical axis and the wordmark on the labels', instead of picking one. -->
      <div class="h-12 px-2 shrink-0 flex items-center">
        <div class="min-w-0 w-full flex items-center gap-3 px-2.5 border border-transparent"
          :class="collapsed ? 'justify-center' : ''">
          <span class="w-4 shrink-0 flex justify-center">
            <AppLogo class="size-7" />
          </span>
          <span v-if="!collapsed" class="font-semibold truncate">SylvaNote</span>
        </div>
      </div>

      <div class="flex-1 flex flex-col gap-1 px-2 py-2 min-h-0">
        <HoverTip text="Search" :disabled="!collapsed">
          <TextField as="button" :icon="IconSearch" class="w-full shrink-0 mb-3" :label="collapsed ? '' : 'Search'"
            :hotkey="collapsed ? '' : 'Ctrl+K'" @click="openSearch" />
        </HoverTip>

        <HoverTip v-for="item in items" :key="item.to" :text="item.label" :disabled="!collapsed">
          <NavButton :icon="item.icon" :label="collapsed ? '' : item.label" :active="isActive(item.to)"
            @click="router.push(item.to)" />
        </HoverTip>

        <div class="flex-1" />

        <HoverTip text="Settings" :disabled="!collapsed">
          <NavButton :icon="IconSettings" :label="collapsed ? '' : 'Settings'" :active="isActive('/settings')"
            @click="router.push('/settings')" />
        </HoverTip>
      </div>

      <!-- The collapse control is a nav row like any other; the border belongs to
           the band around it so it still spans the full width. -->
      <div class="shrink-0 border-t border-border px-2 py-1.5">
        <NavButton :icon="collapsed ? IconChevronRight : IconChevronLeft" :label="collapsed ? '' : 'Collapse'"
          :title="collapsed ? 'Expand' : 'Collapse'" @click="toggle" />
      </div>
    </nav>
  </TooltipProvider>
</template>
