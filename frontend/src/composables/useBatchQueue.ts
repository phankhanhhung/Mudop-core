import { ref, type Ref } from 'vue'
import type { BatchQueueItem } from '@/types/batch'
import type { BatchRequest, BatchResponse } from '@/types/odata'
import { odataService } from '@/services/odataService'

export interface UseBatchQueueReturn {
  items: Ref<BatchQueueItem[]>
  addItem: (item: Omit<BatchQueueItem, 'status'>) => void
  removeItem: (id: string) => void
  reorderItems: (fromIndex: number, toIndex: number) => void
  clearQueue: () => void
  execute: (module: string) => Promise<void>
  isExecuting: Ref<boolean>
  results: Ref<BatchResponse[]>
  error: Ref<string | null>
}

/**
 * Validates that there are no circular dependencies in the queue
 * and that all referenced dependency IDs exist.
 */
function validateDependencies(items: BatchQueueItem[]): string | null {
  const idSet = new Set(items.map((item) => item.id))

  // Check that all referenced IDs exist
  for (const item of items) {
    if (item.dependsOn) {
      for (const depId of item.dependsOn) {
        if (!idSet.has(depId)) {
          return `Item "${item.id}" depends on "${depId}", which does not exist in the queue.`
        }
        if (depId === item.id) {
          return `Item "${item.id}" cannot depend on itself.`
        }
      }
    }
  }

  // Check for circular dependencies using DFS
  const visited = new Set<string>()
  const inStack = new Set<string>()

  const depMap = new Map<string, string[]>()
  for (const item of items) {
    depMap.set(item.id, item.dependsOn ?? [])
  }

  function hasCycle(nodeId: string): boolean {
    if (inStack.has(nodeId)) return true
    if (visited.has(nodeId)) return false

    visited.add(nodeId)
    inStack.add(nodeId)

    for (const dep of depMap.get(nodeId) ?? []) {
      if (hasCycle(dep)) return true
    }

    inStack.delete(nodeId)
    return false
  }

  for (const item of items) {
    if (hasCycle(item.id)) {
      return `Circular dependency detected involving item "${item.id}".`
    }
  }

  return null
}

function buildBatchUrl(item: BatchQueueItem): string {
  let url = item.entitySet
  if (item.entityId) {
    url += `/${item.entityId}`
  }
  return url
}

export function useBatchQueue(): UseBatchQueueReturn {
  const items = ref<BatchQueueItem[]>([])
  const isExecuting = ref(false)
  const results = ref<BatchResponse[]>([])
  const error = ref<string | null>(null)

  function addItem(item: Omit<BatchQueueItem, 'status'>) {
    items.value.push({
      ...item,
      status: 'pending'
    })
  }

  function removeItem(id: string) {
    const index = items.value.findIndex((item) => item.id === id)
    if (index !== -1) {
      items.value.splice(index, 1)
    }
    // Also remove this ID from any dependsOn arrays
    for (const item of items.value) {
      if (item.dependsOn) {
        item.dependsOn = item.dependsOn.filter((depId) => depId !== id)
        if (item.dependsOn.length === 0) {
          item.dependsOn = undefined
        }
      }
    }
  }

  function reorderItems(fromIndex: number, toIndex: number) {
    if (
      fromIndex < 0 ||
      fromIndex >= items.value.length ||
      toIndex < 0 ||
      toIndex >= items.value.length
    ) {
      return
    }
    const [moved] = items.value.splice(fromIndex, 1)
    items.value.splice(toIndex, 0, moved)
  }

  function clearQueue() {
    items.value = []
    results.value = []
    error.value = null
  }

  async function execute(module: string) {
    error.value = null
    results.value = []

    if (items.value.length === 0) {
      error.value = 'Queue is empty. Add operations before executing.'
      return
    }

    // Validate dependencies
    const validationError = validateDependencies(items.value)
    if (validationError) {
      error.value = validationError
      return
    }

    // Reset all statuses to pending
    for (const item of items.value) {
      item.status = 'pending'
      item.response = undefined
    }

    isExecuting.value = true

    try {
      // Convert queue items to BatchRequest[]
      const requests: BatchRequest[] = items.value.map((item) => {
        const request: BatchRequest = {
          id: item.id,
          method: item.method,
          url: buildBatchUrl(item)
        }

        if (item.body && (item.method === 'POST' || item.method === 'PATCH')) {
          request.body = item.body
          request.headers = { 'Content-Type': 'application/json' }
        }

        if (item.dependsOn && item.dependsOn.length > 0) {
          // The backend expects dependsOn in the request body
          ;(request as unknown as Record<string, unknown>).dependsOn = item.dependsOn
        }

        return request
      })

      const responses = await odataService.batch(module, requests)
      results.value = responses

      // Map responses back to queue items
      for (const response of responses) {
        const item = items.value.find((i) => i.id === response.id)
        if (item) {
          item.response = response
          item.status = response.status >= 200 && response.status < 300 ? 'success' : 'error'
        }
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Batch execution failed'
      // Mark all pending items as error
      for (const item of items.value) {
        if (item.status === 'pending') {
          item.status = 'error'
        }
      }
    } finally {
      isExecuting.value = false
    }
  }

  return {
    items,
    addItem,
    removeItem,
    reorderItems,
    clearQueue,
    execute,
    isExecuting,
    results,
    error
  }
}
