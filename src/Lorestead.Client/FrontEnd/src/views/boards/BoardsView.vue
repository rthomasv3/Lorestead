<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { SplitterGroup, SplitterPanel, SplitterResizeHandle } from 'reka-ui'
import BoardListPanel from './BoardListPanel.vue'
import KanbanBoard from './KanbanBoard.vue'
import TaskEditDialog from './TaskEditDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

const boardsStore = useBoardsStore()

const pendingDeleteBoard = ref(null)
const pendingDeleteColumn = ref(null)
const pendingDeleteTask = ref(null)
const taskDialog = ref({ open: false, taskId: null, isNew: false })

function openTask(task) {
  taskDialog.value = { open: true, taskId: task.id, isNew: !!task.isNew }
}

function columnTaskCount(column) {
  return (boardsStore.tasksByColumn.get(column?.id) ?? []).length
}

// Search jumps set the request before navigating here; immediate covers the
// case where the view is already mounted as well as a fresh mount.
watch(() => boardsStore.openTaskRequest, (taskId) => {
  if (taskId) {
    boardsStore.openTaskRequest = null
    openTask({ id: taskId })
  }
}, { immediate: true })

onMounted(() => {
  if (!boardsStore.loaded) boardsStore.load()
  // Columns and tasks are fetched fresh on every mount - the store carries no
  // content across routes (decisions.md); refreshBoard no-ops with no selection.
  boardsStore.refreshBoard()
})

onUnmounted(() => boardsStore.clearContent())
</script>

<template>
  <div class="flex-1 flex min-h-0">
    <SplitterGroup direction="horizontal" auto-save-id="lorestead-boards-layout">
      <SplitterPanel :default-size="18" :min-size="12" class="border-r border-border bg-surface">
        <BoardListPanel @request-delete="(board) => (pendingDeleteBoard = board)" />
      </SplitterPanel>
      <SplitterResizeHandle class="w-px bg-transparent hover:bg-accent/50 transition-colors" />

      <SplitterPanel :min-size="30">
        <EmptyState v-if="!boardsStore.selectedBoardId" class="h-full mt-5">
          {{ boardsStore.boards.length === 0
            ? 'Create a board with the + button to get started.'
            : 'Select a board from the list.' }}
        </EmptyState>
        <KanbanBoard v-else @open-task="openTask" @request-delete-column="(column) => (pendingDeleteColumn = column)"
          @request-delete-task="(task) => (pendingDeleteTask = task)" />
      </SplitterPanel>
    </SplitterGroup>

    <ConfirmDialog :open="pendingDeleteBoard !== null" title="Delete board?"
      :message="`&quot;${pendingDeleteBoard?.name || 'Untitled board'}&quot; and all of its lists and tasks will be deleted.`"
      confirm-label="Delete" @update:open="(v) => { if (!v) pendingDeleteBoard = null }"
      @confirm="boardsStore.deleteBoard(pendingDeleteBoard.id); pendingDeleteBoard = null" />

    <ConfirmDialog :open="pendingDeleteColumn !== null" title="Delete list?" :message="columnTaskCount(pendingDeleteColumn) > 0
      ? `&quot;${pendingDeleteColumn?.name || 'Untitled list'}&quot; and its ${columnTaskCount(pendingDeleteColumn)} ${columnTaskCount(pendingDeleteColumn) === 1 ? 'task' : 'tasks'} will be deleted.`
      : `&quot;${pendingDeleteColumn?.name || 'Untitled list'}&quot; will be deleted.`" confirm-label="Delete"
      @update:open="(v) => { if (!v) pendingDeleteColumn = null }"
      @confirm="boardsStore.deleteColumn(pendingDeleteColumn.id); pendingDeleteColumn = null" />

    <ConfirmDialog :open="pendingDeleteTask !== null" title="Delete task?"
      :message="`&quot;${pendingDeleteTask?.title || 'Untitled task'}&quot; will be deleted.`" confirm-label="Delete"
      @update:open="(v) => { if (!v) pendingDeleteTask = null }"
      @confirm="boardsStore.deleteTask(pendingDeleteTask.id); pendingDeleteTask = null" />

    <TaskEditDialog :open="taskDialog.open" :task-id="taskDialog.taskId" :is-new="taskDialog.isNew"
      @update:open="(v) => (taskDialog = { ...taskDialog, open: v })" />
  </div>
</template>
