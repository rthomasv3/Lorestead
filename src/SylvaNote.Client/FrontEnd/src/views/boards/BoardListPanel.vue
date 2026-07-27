<script setup>
import { ref } from 'vue'
import BoardListRow from './BoardListRow.vue'
import Button from '../../components/Button.vue'
import HoverTip from '../../components/HoverTip.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useBoardsStore } from '../../stores/boardsStore.js'

const emit = defineEmits(['request-delete'])

const boardsStore = useBoardsStore()
const renamingId = ref(null)

async function addBoard() {
  const board = await boardsStore.createBoard()
  renamingId.value = board.id
}

function onRename(id, name) {
  boardsStore.renameBoard(id, name)
}

// Neighbors are derived from the rendered order at the drop edge; the backend
// turns them into a fractional position (same pattern as the notes tree).
function onDrop({ sourceId, targetId, edge }) {
  const list = boardsStore.boards.filter((b) => b.id !== sourceId)
  const targetIndex = list.findIndex((b) => b.id === targetId)
  const insertAt = edge === 'top' ? targetIndex : targetIndex + 1
  boardsStore.moveBoard({
    id: sourceId,
    previousId: insertAt > 0 ? list[insertAt - 1].id : null,
    nextId: insertAt < list.length ? list[insertAt].id : null,
  })
}

function onKeydown(e) {
  if (e.key === 'F2' && boardsStore.selectedBoardId) {
    e.preventDefault()
    renamingId.value = boardsStore.selectedBoardId
  }
}
</script>

<template>
  <div class="h-full flex flex-col min-h-0" tabindex="-1" @keydown="onKeydown">
    <div class="flex items-center justify-between pl-3 pr-2 h-10 shrink-0 border-b border-border">
      <span class="text-sm font-medium">Boards</span>
      <HoverTip text="New board" side="bottom">
        <Button variant="ghost" size="icon" @click="addBoard">
          <i-lucide-plus class="size-4" />
        </Button>
      </HoverTip>
    </div>

    <!-- No horizontal padding and no gap: the rows carry their own padding and run
         edge to edge, the same as the notes tree. -->
    <div class="flex-1 min-h-0 overflow-y-auto pb-2 flex flex-col">
      <BoardListRow v-for="board in boardsStore.boards" :key="board.id" :board="board"
        :selected="board.id === boardsStore.selectedBoardId" :renaming="renamingId === board.id"
        @select="boardsStore.select(board.id)" @rename="(name) => onRename(board.id, name)"
        @rename-done="renamingId = null" @request-rename="renamingId = board.id"
        @request-delete="emit('request-delete', board)" @drop="onDrop" />

      <EmptyState v-if="boardsStore.boards.length === 0" class="flex-1">
        No boards yet. Create one with +.
      </EmptyState>
    </div>
  </div>
</template>
