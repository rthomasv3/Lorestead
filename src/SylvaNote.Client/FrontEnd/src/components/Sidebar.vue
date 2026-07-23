<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TooltipProvider, TooltipRoot, TooltipTrigger, TooltipPortal, TooltipContent } from 'reka-ui'
import IconNotebookText from '~icons/lucide/notebook-text'
import IconSquareKanban from '~icons/lucide/square-kanban'
import IconSettings from '~icons/lucide/settings'

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
  // The search dialog listens for this once it exists (Phase 3); same path as Ctrl+K.
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
        <TooltipRoot :disabled="!collapsed">
          <TooltipTrigger as-child>
            <button
              class="flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0 mb-3 border border-border bg-surface-alt text-on-surface-muted hover:text-on-surface"
              @click="openSearch"
            >
              <i-lucide-search class="size-4 shrink-0" />
              <span v-if="!collapsed" class="truncate">Search</span>
              <kbd v-if="!collapsed" class="ml-auto text-xs text-on-surface-muted">Ctrl+K</kbd>
            </button>
          </TooltipTrigger>
          <TooltipPortal>
            <TooltipContent side="right" :side-offset="6" class="z-50 rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
              Search
            </TooltipContent>
          </TooltipPortal>
        </TooltipRoot>

        <TooltipRoot v-for="item in items" :key="item.to" :disabled="!collapsed">
          <TooltipTrigger as-child>
            <button
              class="flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0"
              :class="isActive(item.to) ? 'bg-accent-soft text-on-surface' : 'text-on-surface-muted hover:bg-surface-alt'"
              @click="router.push(item.to)"
            >
              <component :is="item.icon" class="size-5 shrink-0" />
              <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
            </button>
          </TooltipTrigger>
          <TooltipPortal>
            <TooltipContent side="right" :side-offset="6" class="z-50 rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
              {{ item.label }}
            </TooltipContent>
          </TooltipPortal>
        </TooltipRoot>

        <div class="flex-1" />

        <TooltipRoot :disabled="!collapsed">
          <TooltipTrigger as-child>
            <button
              class="flex items-center gap-3 rounded-md px-2.5 h-9 text-sm shrink-0"
              :class="isActive('/settings') ? 'bg-accent-soft text-on-surface' : 'text-on-surface-muted hover:bg-surface-alt'"
              @click="router.push('/settings')"
            >
              <IconSettings class="size-5 shrink-0" />
              <span v-if="!collapsed" class="truncate">Settings</span>
            </button>
          </TooltipTrigger>
          <TooltipPortal>
            <TooltipContent side="right" :side-offset="6" class="z-50 rounded-md border border-border bg-surface-elevated px-2 py-1 text-xs shadow-md">
              Settings
            </TooltipContent>
          </TooltipPortal>
        </TooltipRoot>
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
