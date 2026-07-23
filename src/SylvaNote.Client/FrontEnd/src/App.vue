<script setup>
import { useSettingsStore } from './stores/settingsStore'
import Sidebar from './components/Sidebar.vue'

useSettingsStore().init()
</script>

<template>
  <div class="h-screen flex bg-surface text-on-surface">
    <Sidebar />
    <main class="flex-1 min-w-0 flex flex-col min-h-0 bg-surface [view-transition-name:main-view]">
      <router-view />
    </main>
  </div>
</template>
