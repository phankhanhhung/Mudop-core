import { ref } from 'vue'

export interface ConfirmDialogOptions {
  title: string
  description: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'default' | 'destructive'
}

export function useConfirmDialog() {
  const isOpen = ref(false)
  const title = ref('')
  const description = ref('')
  const confirmLabel = ref('Confirm')
  const cancelLabel = ref('Cancel')
  const variant = ref<'default' | 'destructive'>('default')

  let resolvePromise: ((value: boolean) => void) | null = null

  function confirm(options: ConfirmDialogOptions): Promise<boolean> {
    title.value = options.title
    description.value = options.description
    confirmLabel.value = options.confirmLabel ?? 'Confirm'
    cancelLabel.value = options.cancelLabel ?? 'Cancel'
    variant.value = options.variant ?? 'default'
    isOpen.value = true

    return new Promise<boolean>((resolve) => {
      resolvePromise = resolve
    })
  }

  function handleConfirm() {
    isOpen.value = false
    resolvePromise?.(true)
    resolvePromise = null
  }

  function handleCancel() {
    isOpen.value = false
    resolvePromise?.(false)
    resolvePromise = null
  }

  return {
    isOpen,
    title,
    description,
    confirmLabel,
    cancelLabel,
    variant,
    confirm,
    handleConfirm,
    handleCancel
  }
}
