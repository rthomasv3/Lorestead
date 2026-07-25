<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TooltipProvider } from 'reka-ui'
import HoverTip from './HoverTip.vue'
import TextField from './TextField.vue'
import IconNotebookText from '~icons/lucide/notebook-text'
import IconSquareKanban from '~icons/lucide/square-kanban'
import IconSettings from '~icons/lucide/settings'
import IconSearch from '~icons/lucide/search'

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
      :class="collapsed ? 'w-14' : 'w-52'"
    >
      <div class="flex items-center gap-2.5 h-12 px-4 shrink-0">
        <i-lucide-trees class="size-5 text-accent shrink-0" />
        <span v-if="!collapsed" class="font-semibold truncate">SylvaNote</span>
      </div>

      <div class="flex-1 flex flex-col gap-1 px-2 py-2 min-h-0">
        <HoverTip text="Search" :disabled="!collapsed">
          <TextField as="button" :icon="IconSearch" class="w-full shrink-0 mb-3"
            :label="collapsed ? '' : 'Search'" :hotkey="collapsed ? '' : 'Ctrl+K'" @click="openSearch" />
        </HoverTip>

        <HoverTip v-for="item in items" :key="item.to" :text="item.label" :disabled="!collapsed">
          <button
            class="flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0"
            :class="isActive(item.to) ? 'bg-accent-soft text-on-surface' : 'text-on-surface-muted hover:bg-surface-alt'"
            @click="router.push(item.to)"
          >
            <component :is="item.icon" class="size-5 shrink-0" />
            <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
          </button>
        </HoverTip>

        <div class="flex-1" />

        <HoverTip text="Settings" :disabled="!collapsed">
          <button
            class="flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0"
            :class="isActive('/settings') ? 'bg-accent-soft text-on-surface' : 'text-on-surface-muted hover:bg-surface-alt'"
            @click="router.push('/settings')"
          >
            <IconSettings class="size-5 shrink-0" />
            <span v-if="!collapsed" class="truncate">Settings</span>
          </button>
        </HoverTip>
      </div>

      <button
        class="flex items-center gap-3 px-4 h-10 shrink-0 text-on-surface-muted hover:text-on-surface border-t border-border"
        :title="collapsed ? 'Expand' : 'Collapse'"
        @click="toggle"
      >
        <i-lucide-chevron-right v-if="collapsed" class="size-5 shrink-0" />
        <i-lucide-chevron-left v-else class="size-5 shrink-0" />
        <span v-if="!collapsed" class="text-sm truncate">Collapse</span>
      </button>
    </nav>
  </TooltipProvider>
</template>
