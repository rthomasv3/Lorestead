<script setup>
import { AlertDialogRoot, AlertDialogPortal, AlertDialogOverlay, AlertDialogContent, AlertDialogTitle, AlertDialogDescription, AlertDialogCancel } from 'reka-ui'
import Button from './Button.vue'

defineProps({
  open: { type: Boolean, default: false },
  title: { type: String, required: true },
  message: { type: String, default: '' },
  confirmLabel: { type: String, default: 'Delete' },
  danger: { type: Boolean, default: true },
})

const emit = defineEmits(['update:open', 'confirm'])

// Not AlertDialogAction: its auto-close can fire update:open before the confirm
// listener runs, so parents that clear their pending state on close would read
// null. Emitting in explicit order makes confirm-then-close deterministic.
function confirm() {
  emit('confirm')
  emit('update:open', false)
}
</script>

<template>
  <AlertDialogRoot :open="open" @update:open="emit('update:open', $event)">
    <AlertDialogPortal>
      <AlertDialogOverlay class="fixed inset-0 bg-black/40 z-40 dialog-fade" />
      <AlertDialogContent
        class="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-50 w-full max-w-sm rounded-lg border border-border bg-surface-elevated p-5 shadow-xl dialog-fade">
        <AlertDialogTitle class="font-semibold mb-2">{{ title }}</AlertDialogTitle>
        <AlertDialogDescription class="text-sm text-on-surface-muted mb-5">{{ message }}</AlertDialogDescription>
        <div class="flex justify-end gap-2">
          <AlertDialogCancel as-child>
            <Button variant="outline">Cancel</Button>
          </AlertDialogCancel>
          <Button :variant="danger ? 'destructive' : 'primary'" @click="confirm">{{ confirmLabel }}</Button>
        </div>
      </AlertDialogContent>
    </AlertDialogPortal>
  </AlertDialogRoot>
</template>
