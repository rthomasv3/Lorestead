<script setup>
import { ref } from 'vue'
import KanbanColumn from './KanbanColumn.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

const emit = defineEmits(['open-task', 'request-delete-column', 'request-delete-task'])

const boardsStore = useBoardsStore()
const renamingColumnId = ref(null)

async function addColumn() {
  const column = await boardsStore.createColumn()
  renamingColumnId.value = column.id
}

async function addTask(columnId) {
  const task = await boardsStore.createTask(columnId)
  emit('open-task', { id: task.id, isNew: true })
}

function onColumnDrop({ columnId, targetColumnId, edge }) {
  const list = boardsStore.columns.filter((c) => c.id !== columnId)
  const targetIndex = list.findIndex((c) => c.id === targetColumnId)
  const insertAt = edge === 'left' ? targetIndex : targetIndex + 1
  boardsStore.moveColumn({
    id: columnId,
    previousId: insertAt > 0 ? list[insertAt - 1].id : null,
    nextId: insertAt < list.length ? list[insertAt].id : null,
  })
}

// targetTaskId null = dropped on the open area below the cards → append to end.
function onTaskDrop({ taskId, targetTaskId, edge, columnId }) {
  const list = (boardsStore.tasksByColumn.get(columnId) ?? []).filter((t) => t.id !== taskId)
  let previousId = null
  let nextId = null
  if (targetTaskId === null) {
    previousId = list.length > 0 ? list[list.length - 1].id : null
  } else {
    const targetIndex = list.findIndex((t) => t.id === targetTaskId)
    const insertAt = edge === 'top' ? targetIndex : targetIndex + 1
    previousId = insertAt > 0 ? list[insertAt - 1].id : null
    nextId = insertAt < list.length ? list[insertAt].id : null
  }
  boardsStore.moveTask({ id: taskId, columnId, previousId, nextId })
}
</script>

<template>
  <div class="h-full overflow-x-auto overflow-y-hidden">
    <!-- w-max: without it this div is viewport-wide and the lanes overflow past
         its right padding, cramming the add-list button against the edge. -->
    <div class="h-full w-max min-w-full flex items-start gap-3 p-3">
      <KanbanColumn v-for="column in boardsStore.columns" :key="column.id" :column="column"
        :tasks="boardsStore.tasksByColumn.get(column.id) ?? []" :renaming="renamingColumnId === column.id"
        @rename="(name) => boardsStore.renameColumn(column.id, name)" @rename-done="renamingColumnId = null"
        @request-rename="renamingColumnId = column.id" @request-delete="emit('request-delete-column', column)"
        @add-task="addTask(column.id)" @open-task="(task) => emit('open-task', task)"
        @request-delete-task="(task) => emit('request-delete-task', task)" @task-drop="onTaskDrop"
        @column-drop="onColumnDrop" />

      <button
        class="w-64 shrink-0 flex items-center justify-center gap-1.5 h-9 rounded-lg border border-border text-sm text-on-surface-muted hover:text-on-surface hover:bg-hover-wash"
        @click="addColumn">
        <i-lucide-plus class="size-4 -ml-3" />
        Add list
      </button>
    </div>
  </div>
</template>
