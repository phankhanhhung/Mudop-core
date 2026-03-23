/**
 * Optimistic Updates — apply UI changes immediately, confirm/rollback after server response.
 *
 * Features:
 * - Update local reactive state immediately on mutation
 * - Show optimistic result in UI
 * - On server success: confirm local state
 * - On server error: rollback + show error notification
 * - Works with batch: optimistic for entire batch
 * - Operation history for debugging
 *
 * Usage:
 *   const optimistic = new OptimisticUpdateManager()
 *
 *   // Update with optimistic UI
 *   optimistic.update(
 *     dataRef,                    // Reactive data array
 *     { id: '1', name: 'New' },  // Optimistic data
 *     odataService.update(...)    // Server promise
 *   )
 */

import { ref, type Ref } from 'vue'
import type { OptimisticOperation } from './types'

let operationCounter = 0

export class OptimisticUpdateManager {
  /** Active optimistic operations */
  private operations = new Map<string, OptimisticOperation>()

  /** Operation history (for debugging) */
  readonly history: Ref<OptimisticOperation[]> = ref([])

  /** Whether any optimistic operations are pending */
  readonly hasPending: Ref<boolean> = ref(false)

  /** Number of pending operations */
  readonly pendingCount: Ref<number> = ref(0)

  /**
   * Apply an optimistic update to a list.
   * Immediately updates the reactive data, then confirms/rolls back after server response.
   */
  async optimisticUpdate<T extends Record<string, unknown>>(
    dataRef: Ref<T[]>,
    key: string,
    optimisticData: Partial<T>,
    serverPromise: Promise<T>
  ): Promise<T> {
    const opId = `op-${++operationCounter}`

    // Find the item in the list
    const index = dataRef.value.findIndex(
      (item: T) => getKey(item) === key
    )

    if (index < 0) {
      // Item not in list — just wait for server
      return serverPromise
    }

    // Save previous data for rollback
    const previousData = { ...dataRef.value[index] }

    // Apply optimistic update immediately
    const optimisticItem = { ...previousData, ...optimisticData }
    dataRef.value[index] = optimisticItem as T

    // Track operation
    const operation: OptimisticOperation<T> = {
      id: opId,
      type: 'update',
      entitySet: '',
      key,
      optimisticData: optimisticItem as T,
      previousData: previousData as T,
      serverPromise,
      status: 'pending',
      timestamp: Date.now(),
    }
    this.operations.set(opId, operation as OptimisticOperation)
    this.updateCounts()

    try {
      const result = await serverPromise

      // Confirmed — update with server response
      operation.status = 'confirmed'
      const confirmedIndex = dataRef.value.findIndex((item: T) => getKey(item) === key)
      if (confirmedIndex >= 0) {
        dataRef.value[confirmedIndex] = result
      }

      return result
    } catch (e) {
      // Rollback
      operation.status = 'rolledBack'
      const rollbackIndex = dataRef.value.findIndex((item: T) => getKey(item) === key)
      if (rollbackIndex >= 0) {
        dataRef.value[rollbackIndex] = previousData as T
      }

      throw e
    } finally {
      this.operations.delete(opId)
      this.history.value.push({ ...operation } as OptimisticOperation)
      this.updateCounts()

      // Keep history bounded
      if (this.history.value.length > 100) {
        this.history.value = this.history.value.slice(-50)
      }
    }
  }

  /**
   * Apply an optimistic create to a list.
   */
  async optimisticCreate<T extends Record<string, unknown>>(
    dataRef: Ref<T[]>,
    optimisticData: T,
    serverPromise: Promise<T>
  ): Promise<T> {
    const opId = `op-${++operationCounter}`

    // Add to list immediately
    dataRef.value = [...dataRef.value, optimisticData]

    const operation: OptimisticOperation<T> = {
      id: opId,
      type: 'create',
      entitySet: '',
      optimisticData,
      serverPromise,
      status: 'pending',
      timestamp: Date.now(),
    }
    this.operations.set(opId, operation as OptimisticOperation)
    this.updateCounts()

    try {
      const result = await serverPromise
      operation.status = 'confirmed'

      // Replace optimistic item with server response
      const tempKey = getKey(optimisticData)
      const index = dataRef.value.findIndex((item: T) => getKey(item) === tempKey)
      if (index >= 0) {
        dataRef.value[index] = result
      }

      return result
    } catch (e) {
      // Rollback — remove the optimistic item
      operation.status = 'rolledBack'
      const tempKey = getKey(optimisticData)
      dataRef.value = dataRef.value.filter((item: T) => getKey(item) !== tempKey)

      throw e
    } finally {
      this.operations.delete(opId)
      this.history.value.push({ ...operation } as OptimisticOperation)
      this.updateCounts()
    }
  }

  /**
   * Apply an optimistic delete from a list.
   */
  async optimisticDelete<T extends Record<string, unknown>>(
    dataRef: Ref<T[]>,
    key: string,
    serverPromise: Promise<void>
  ): Promise<void> {
    const opId = `op-${++operationCounter}`

    // Find and remove the item
    const index = dataRef.value.findIndex((item: T) => getKey(item) === key)
    if (index < 0) {
      return serverPromise
    }

    const previousData = dataRef.value[index]
    dataRef.value = dataRef.value.filter((item: T) => getKey(item) !== key)

    const operation: OptimisticOperation<T> = {
      id: opId,
      type: 'delete',
      entitySet: '',
      key,
      optimisticData: previousData as T,
      previousData: previousData as T,
      serverPromise: serverPromise as unknown as Promise<unknown>,
      status: 'pending',
      timestamp: Date.now(),
    }
    this.operations.set(opId, operation as OptimisticOperation)
    this.updateCounts()

    try {
      await serverPromise
      operation.status = 'confirmed'
    } catch (e) {
      // Rollback — re-add the item
      operation.status = 'rolledBack'
      dataRef.value = [...dataRef.value]
      dataRef.value.splice(index, 0, previousData)

      throw e
    } finally {
      this.operations.delete(opId)
      this.history.value.push({ ...operation } as OptimisticOperation)
      this.updateCounts()
    }
  }

  /**
   * Clear operation history.
   */
  clearHistory(): void {
    this.history.value = []
  }

  /**
   * Get all pending operations.
   */
  getPending(): OptimisticOperation[] {
    return [...this.operations.values()]
  }

  // =========================================================================
  // Private
  // =========================================================================

  private updateCounts(): void {
    this.pendingCount.value = this.operations.size
    this.hasPending.value = this.operations.size > 0
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getKey(item: Record<string, unknown>): string {
  return String(item['Id'] ?? item['ID'] ?? item['id'] ?? '')
}
