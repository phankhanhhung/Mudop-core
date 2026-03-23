import { ref, readonly, type Ref } from 'vue'
import { odataService } from '@/services/odataService'

export interface BulkProgress {
  current: number
  total: number
}

export interface UseBulkActionsOptions {
  module: Ref<string> | string
  entitySet: Ref<string> | string
  onSuccess?: () => void
  onError?: (error: string) => void
}

export interface UseBulkActionsReturn {
  /** Delete multiple entities by ID */
  bulkDelete: (ids: string[]) => Promise<{ succeeded: number; failed: number }>
  /** Whether a bulk operation is in progress */
  isProcessing: Readonly<Ref<boolean>>
  /** Current progress */
  progress: Readonly<Ref<BulkProgress>>
  /** Last error message */
  error: Readonly<Ref<string | null>>
}

export function useBulkActions(options: UseBulkActionsOptions): UseBulkActionsReturn {
  const isProcessing = ref(false)
  const progress = ref<BulkProgress>({ current: 0, total: 0 })
  const error = ref<string | null>(null)

  function getModule(): string {
    return typeof options.module === 'string' ? options.module : options.module.value
  }

  function getEntitySet(): string {
    return typeof options.entitySet === 'string' ? options.entitySet : options.entitySet.value
  }

  async function bulkDelete(ids: string[]): Promise<{ succeeded: number; failed: number }> {
    if (ids.length === 0) return { succeeded: 0, failed: 0 }

    isProcessing.value = true
    progress.value = { current: 0, total: ids.length }
    error.value = null

    let succeeded = 0
    let failed = 0
    const mod = getModule()
    const es = getEntitySet()

    for (const id of ids) {
      try {
        await odataService.delete(mod, es, id)
        succeeded++
      } catch (err) {
        failed++
        console.warn(`Bulk delete failed for ID ${id}:`, err)
      }
      progress.value = { current: succeeded + failed, total: ids.length }
    }

    isProcessing.value = false

    if (failed > 0) {
      error.value = `${failed} of ${ids.length} deletions failed`
      options.onError?.(error.value)
    }

    if (succeeded > 0) {
      options.onSuccess?.()
    }

    return { succeeded, failed }
  }

  return {
    bulkDelete,
    isProcessing: readonly(isProcessing),
    progress: readonly(progress),
    error: readonly(error),
  }
}
