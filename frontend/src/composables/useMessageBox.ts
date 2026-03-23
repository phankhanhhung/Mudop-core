import { ref, type Ref } from 'vue'

export type MessageBoxType = 'info' | 'warning' | 'error' | 'success' | 'confirm'

export interface MessageBoxAction {
  label: string
  key: string
  variant?: 'default' | 'destructive' | 'outline' | 'secondary' | 'ghost'
  autoFocus?: boolean
}

export interface MessageBoxOptions {
  type?: MessageBoxType
  title: string
  message: string
  details?: string
  actions?: MessageBoxAction[]
  showCloseButton?: boolean
  onAction?: (actionKey: string) => void
}

export const MessageBoxActions = {
  OK: { label: 'OK', key: 'ok', variant: 'default' as const, autoFocus: true },
  CANCEL: { label: 'Cancel', key: 'cancel', variant: 'outline' as const },
  YES: { label: 'Yes', key: 'yes', variant: 'default' as const, autoFocus: true },
  NO: { label: 'No', key: 'no', variant: 'outline' as const },
  DELETE: { label: 'Delete', key: 'delete', variant: 'destructive' as const },
  RETRY: { label: 'Retry', key: 'retry', variant: 'default' as const },
  CLOSE: { label: 'Close', key: 'close', variant: 'outline' as const }
} as const

const defaultActionsByType: Record<MessageBoxType, MessageBoxAction[]> = {
  info: [MessageBoxActions.OK],
  warning: [MessageBoxActions.OK, MessageBoxActions.CLOSE],
  error: [MessageBoxActions.OK, MessageBoxActions.CLOSE],
  success: [MessageBoxActions.OK],
  confirm: [MessageBoxActions.YES, MessageBoxActions.NO]
}

export interface UseMessageBoxReturn {
  isOpen: Ref<boolean>
  currentOptions: Ref<MessageBoxOptions | null>
  show: (options: MessageBoxOptions) => Promise<string>
  info: (title: string, message: string, details?: string) => Promise<string>
  warning: (title: string, message: string, details?: string) => Promise<string>
  error: (title: string, message: string, details?: string) => Promise<string>
  success: (title: string, message: string, details?: string) => Promise<string>
  confirm: (title: string, message: string) => Promise<string>
  close: () => void
}

export function useMessageBox(): UseMessageBoxReturn {
  const isOpen = ref(false)
  const currentOptions = ref<MessageBoxOptions | null>(null)
  let resolvePromise: ((actionKey: string) => void) | null = null

  function show(options: MessageBoxOptions): Promise<string> {
    // If already open, resolve previous promise with empty string (dismissed)
    if (resolvePromise) {
      resolvePromise('')
      resolvePromise = null
    }

    currentOptions.value = options
    isOpen.value = true

    return new Promise<string>((resolve) => {
      resolvePromise = resolve
    })
  }

  function handleAction(actionKey: string) {
    isOpen.value = false
    currentOptions.value?.onAction?.(actionKey)
    resolvePromise?.(actionKey)
    resolvePromise = null
  }

  function close() {
    handleAction('')
  }

  function info(title: string, message: string, details?: string): Promise<string> {
    return show({ type: 'info', title, message, details })
  }

  function warning(title: string, message: string, details?: string): Promise<string> {
    return show({ type: 'warning', title, message, details })
  }

  function error(title: string, message: string, details?: string): Promise<string> {
    return show({ type: 'error', title, message, details })
  }

  function success(title: string, message: string, details?: string): Promise<string> {
    return show({ type: 'success', title, message, details })
  }

  function confirm(title: string, message: string): Promise<string> {
    return show({ type: 'confirm', title, message })
  }

  return {
    isOpen,
    currentOptions,
    show,
    info,
    warning,
    error,
    success,
    confirm,
    close,
    // Expose handleAction for the component to call
    _handleAction: handleAction
  } as UseMessageBoxReturn & { _handleAction: (key: string) => void }
}

export function getDefaultActions(type: MessageBoxType): MessageBoxAction[] {
  return defaultActionsByType[type] ?? [MessageBoxActions.OK]
}
