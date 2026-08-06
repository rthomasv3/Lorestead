<script setup>
import { useRouter } from 'vue-router'
import EmptyState from '../../components/EmptyState.vue'
import { useNotesStore } from '../../stores/notesStore.js'
import { useBoardsStore } from '../../stores/boardsStore.js'

const router = useRouter()
const notesStore = useNotesStore()
const boardsStore = useBoardsStore()

// Same jump as a search result: both land on the target's route (unified
// routing), tasks with the dialog request queued for BoardsView to pick up.
async function open(backlink) {
  if (backlink.noteId) {
    notesStore.reveal(backlink.noteId)
    await router.push(`/notes/${backlink.noteId}`)
  } else {
    boardsStore.openTaskRequest = backlink.taskId
    await router.push(`/boards/${backlink.boardId}`)
  }
}
</script>

<template>
  <div class="h-full flex flex-col min-h-0">
    <div class="flex items-center px-3 h-10 shrink-0 border-b border-border">
      <span class="text-sm font-medium">Backlinks</span>
    </div>

    <div class="flex-1 min-h-0 overflow-y-auto p-2 flex flex-col gap-1.5">
      <button v-for="backlink in notesStore.currentBacklinks" :key="backlink.noteId ?? backlink.taskId"
        class="text-left rounded-md border border-border bg-surface-alt/40 px-2.5 py-2 hover:border-accent hover:bg-accent-soft/40"
        @click="open(backlink)">
        <div class="flex items-center gap-1.5 min-w-0">
          <i-lucide-file-text v-if="backlink.noteId" class="size-3.5 shrink-0 text-on-surface-muted" />
          <i-lucide-square-check-big v-else class="size-3.5 shrink-0 text-on-surface-muted" />
          <span class="text-sm truncate">{{ backlink.title || 'Untitled' }}</span>
        </div>
        <div v-if="backlink.taskId" class="flex items-center gap-1.5 mt-0.5 min-w-0">
          <span class="text-[11px] text-on-surface-muted/70 truncate">
            {{ backlink.boardName }} › {{ backlink.columnName }}
          </span>
          <!-- A task can mention the note in its body AND carry it in its
               linked-notes list; the badge marks the list, the snippet the body. -->
          <span v-if="backlink.via !== 'body'"
            class="shrink-0 px-1 rounded bg-surface-alt text-[10px] leading-4 text-on-surface-muted">
            Linked
          </span>
        </div>
        <p v-if="backlink.snippet" class="text-xs text-on-surface-muted mt-1 line-clamp-3">{{ backlink.snippet }}</p>
      </button>

      <EmptyState v-if="notesStore.currentBacklinks.length === 0" class="flex-1">
        Nothing links to this note yet
      </EmptyState>
    </div>
  </div>
</template>
