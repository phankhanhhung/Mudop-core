/**
 * Auto-Batching Engine — groups multiple OData requests into single $batch calls.
 *
 * Inspired by OpenUI5 V4's batch group concept:
 * - '$auto': Requests are collected and flushed at the end of the current microtask
 * - '$direct': Requests are sent immediately (no batching)
 * - Custom group IDs: Requests collected until explicit submitBatch(groupId)
 *
 * Usage:
 *   const batch = new BatchManager('myModule')
 *
 *   // Auto-batched (grouped in same tick)
 *   batch.enqueue({ method: 'GET', url: 'Customers' })
 *   batch.enqueue({ method: 'GET', url: 'Orders' })
 *   // → Single $batch request sent at end of microtask
 *
 *   // Direct (immediate)
 *   batch.enqueue({ method: 'POST', url: 'Customers', body: {...} }, '$direct')
 *
 *   // Custom group (manual flush)
 *   batch.enqueue({ method: 'PATCH', url: 'Customers/1', body: {...} }, 'saveAll')
 *   batch.enqueue({ method: 'PATCH', url: 'Orders/2', body: {...} }, 'saveAll')
 *   await batch.submitBatch('saveAll')
 */

import { ref, type Ref } from 'vue'
import { odataService } from '@/services/odataService'
import type { BatchRequest, BatchResponse } from '@/types/odata'
import type { BatchGroupId, PendingBatchRequest, BatchGroup } from './types'

let requestIdCounter = 0

export class BatchManager {
  private module: string
  private groups = new Map<string, BatchGroup>()
  private autoFlushScheduled = false

  /** Whether a batch submission is in progress */
  readonly isSubmitting: Ref<boolean> = ref(false)

  /** Last batch error */
  readonly error: Ref<string | null> = ref(null)

  /** Total requests queued across all groups */
  readonly queuedCount: Ref<number> = ref(0)

  constructor(module: string) {
    this.module = module
  }

  /**
   * Enqueue a request into a batch group.
   *
   * Returns a promise that resolves with the individual response
   * once the batch is submitted and the response is received.
   */
  enqueue(
    request: Omit<BatchRequest, 'id'>,
    groupId: BatchGroupId = '$auto'
  ): Promise<BatchResponse> {
    // Direct mode — send immediately without batching
    if (groupId === '$direct') {
      return this.sendDirect(request)
    }

    return new Promise<BatchResponse>((resolve, reject) => {
      const id = `req-${++requestIdCounter}`
      const pending: PendingBatchRequest = {
        id,
        request: { ...request, id },
        resolve,
        reject,
        groupId,
        createdAt: Date.now(),
      }

      // Get or create group
      let group = this.groups.get(groupId)
      if (!group) {
        group = { id: groupId, requests: [] }
        this.groups.set(groupId, group)
      }

      group.requests.push(pending)
      this.queuedCount.value++

      // Schedule auto-flush for '$auto' group
      if (groupId === '$auto' && !this.autoFlushScheduled) {
        this.autoFlushScheduled = true
        // Use queueMicrotask to flush at end of current microtask
        queueMicrotask(() => {
          this.autoFlushScheduled = false
          this.submitBatch('$auto')
        })
      }
    })
  }

  /**
   * Submit all pending requests in a specific batch group.
   */
  async submitBatch(groupId: string): Promise<BatchResponse[]> {
    const group = this.groups.get(groupId)
    if (!group || group.requests.length === 0) {
      return []
    }

    // Take snapshot and clear group
    const pending = [...group.requests]
    group.requests = []
    this.queuedCount.value -= pending.length

    if (pending.length === 0) return []

    this.isSubmitting.value = true
    this.error.value = null

    try {
      const batchRequests = pending.map(p => p.request)
      const responses = await odataService.batch(this.module, batchRequests)

      // Map responses back to pending promises
      for (const response of responses) {
        const pendingReq = pending.find(p => p.id === response.id)
        if (pendingReq) {
          if (response.status >= 200 && response.status < 300) {
            pendingReq.resolve(response)
          } else {
            pendingReq.reject(
              new Error(`Batch request ${response.id} failed: status ${response.status}`)
            )
          }
        }
      }

      return responses
    } catch (e) {
      const errorMsg = e instanceof Error ? e.message : 'Batch submission failed'
      this.error.value = errorMsg

      // Reject all pending promises
      for (const p of pending) {
        p.reject(new Error(errorMsg))
      }

      throw e
    } finally {
      this.isSubmitting.value = false

      // Clean up empty group
      if (group.requests.length === 0) {
        this.groups.delete(groupId)
      }
    }
  }

  /**
   * Submit all pending batch groups.
   */
  async submitAll(): Promise<void> {
    const groupIds = [...this.groups.keys()]
    await Promise.all(groupIds.map(id => this.submitBatch(id)))
  }

  /**
   * Get the number of pending requests in a group.
   */
  getGroupSize(groupId: string): number {
    return this.groups.get(groupId)?.requests.length ?? 0
  }

  /**
   * Get all group IDs with pending requests.
   */
  getActiveGroups(): string[] {
    return [...this.groups.keys()].filter(id => (this.groups.get(id)?.requests.length ?? 0) > 0)
  }

  /**
   * Cancel all pending requests in a group.
   */
  cancelGroup(groupId: string): void {
    const group = this.groups.get(groupId)
    if (!group) return

    for (const p of group.requests) {
      p.reject(new Error('Batch group cancelled'))
    }

    this.queuedCount.value -= group.requests.length
    group.requests = []
    this.groups.delete(groupId)
  }

  /**
   * Cancel all pending requests across all groups.
   */
  cancelAll(): void {
    for (const [groupId] of this.groups) {
      this.cancelGroup(groupId)
    }
  }

  /**
   * Send a single request directly without batching.
   */
  private async sendDirect(request: Omit<BatchRequest, 'id'>): Promise<BatchResponse> {
    const id = `direct-${++requestIdCounter}`
    const responses = await odataService.batch(this.module, [{ ...request, id }])
    return responses[0] ?? { id, status: 500, body: { error: 'No response' } }
  }
}
