/**
 * OData DevTools — request inspector and debugging tools.
 *
 * Features:
 * - Request/response logger with timeline
 * - Model state inspector (entities, dirty fields, pending changes)
 * - Cache inspector (entries, hit/miss ratio, tags)
 * - Batch operation visualizer
 * - Query builder playground
 * - Performance metrics
 *
 * Usage:
 *   const devtools = ODataDevTools.getInstance()
 *   devtools.enable()
 *
 *   // Log requests (called from pipeline middleware)
 *   devtools.logRequest({ method: 'GET', url: '/odata/Customers' })
 *   devtools.logResponse({ status: 200, ... })
 *
 *   // Get all entries
 *   const entries = devtools.getEntries()
 *   const stats = devtools.getStats()
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue'
import type { ODataDevToolsEntry } from './types'
import type { CacheStats } from './EntityCache'
import type { EntityCache } from './EntityCache'
import type { ODataModel } from './ODataModel'
import type { BatchManager } from './BatchManager'

let instance: ODataDevTools | null = null

export interface DevToolsStats {
  totalRequests: number
  totalResponses: number
  totalErrors: number
  averageResponseTime: number
  cacheHitRatio: number
  batchOperations: number
  requestsPerSecond: number
}

export class ODataDevTools {
  private entries: Ref<ODataDevToolsEntry[]> = ref([])
  private enabled = ref(false)
  private maxEntries = 500
  private startTime = Date.now()

  // References to framework components
  private model: ODataModel | null = null
  private cache: EntityCache | null = null
  private batchManager: BatchManager | null = null

  /** Reactive entry count */
  readonly entryCount: ComputedRef<number>

  /** Reactive error count */
  readonly errorCount: ComputedRef<number>

  private constructor() {
    this.entryCount = computed(() => this.entries.value.length)
    this.errorCount = computed(() =>
      this.entries.value.filter((e: ODataDevToolsEntry) => e.type === 'error').length
    )
  }

  /**
   * Get the singleton instance.
   */
  static getInstance(): ODataDevTools {
    if (!instance) {
      instance = new ODataDevTools()
    }
    return instance
  }

  /**
   * Enable devtools logging.
   */
  enable(): void {
    this.enabled.value = true
  }

  /**
   * Disable devtools logging.
   */
  disable(): void {
    this.enabled.value = false
  }

  /**
   * Check if devtools is enabled.
   */
  isEnabled(): boolean {
    return this.enabled.value
  }

  /**
   * Register framework components for inspection.
   */
  register(components: {
    model?: ODataModel
    cache?: EntityCache
    batchManager?: BatchManager
  }): void {
    if (components.model) this.model = components.model
    if (components.cache) this.cache = components.cache
    if (components.batchManager) this.batchManager = components.batchManager
  }

  // =========================================================================
  // Logging
  // =========================================================================

  /**
   * Log a request.
   */
  logRequest(info: {
    method: string
    url: string
    batchGroupId?: string
  }): string {
    if (!this.enabled.value) return ''

    const id = `req-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
    this.addEntry({
      id,
      timestamp: Date.now(),
      type: 'request',
      method: info.method,
      url: info.url,
      batchGroupId: info.batchGroupId,
    })
    return id
  }

  /**
   * Log a response.
   */
  logResponse(info: {
    requestId: string
    status: number
    duration: number
    size?: number
    data?: unknown
  }): void {
    if (!this.enabled.value) return

    this.addEntry({
      id: `res-${info.requestId}`,
      timestamp: Date.now(),
      type: 'response',
      status: info.status,
      duration: info.duration,
      size: info.size,
      data: info.data,
    })
  }

  /**
   * Log a cache hit.
   */
  logCacheHit(url: string): void {
    if (!this.enabled.value) return

    this.addEntry({
      id: `cache-hit-${Date.now()}`,
      timestamp: Date.now(),
      type: 'cache-hit',
      url,
    })
  }

  /**
   * Log a cache miss.
   */
  logCacheMiss(url: string): void {
    if (!this.enabled.value) return

    this.addEntry({
      id: `cache-miss-${Date.now()}`,
      timestamp: Date.now(),
      type: 'cache-miss',
      url,
    })
  }

  /**
   * Log a batch operation.
   */
  logBatch(info: {
    groupId: string
    requestCount: number
    duration: number
  }): void {
    if (!this.enabled.value) return

    this.addEntry({
      id: `batch-${Date.now()}`,
      timestamp: Date.now(),
      type: 'batch',
      batchGroupId: info.groupId,
      duration: info.duration,
      data: { requestCount: info.requestCount },
    })
  }

  /**
   * Log an error.
   */
  logError(info: {
    method?: string
    url?: string
    status?: number
    data?: unknown
  }): void {
    if (!this.enabled.value) return

    this.addEntry({
      id: `error-${Date.now()}`,
      timestamp: Date.now(),
      type: 'error',
      method: info.method,
      url: info.url,
      status: info.status,
      data: info.data,
    })
  }

  // =========================================================================
  // Inspection
  // =========================================================================

  /**
   * Get all log entries.
   */
  getEntries(filter?: {
    type?: ODataDevToolsEntry['type']
    limit?: number
  }): ODataDevToolsEntry[] {
    let result = this.entries.value

    if (filter?.type) {
      result = result.filter((e: ODataDevToolsEntry) => e.type === filter.type)
    }

    if (filter?.limit) {
      result = result.slice(-filter.limit)
    }

    return result
  }

  /**
   * Get aggregate statistics.
   */
  getStats(): DevToolsStats {
    const entries = this.entries.value
    const requests = entries.filter((e: ODataDevToolsEntry) => e.type === 'request')
    const responses = entries.filter((e: ODataDevToolsEntry) => e.type === 'response')
    const errors = entries.filter((e: ODataDevToolsEntry) => e.type === 'error')
    const cacheHits = entries.filter((e: ODataDevToolsEntry) => e.type === 'cache-hit')
    const cacheMisses = entries.filter((e: ODataDevToolsEntry) => e.type === 'cache-miss')
    const batches = entries.filter((e: ODataDevToolsEntry) => e.type === 'batch')

    const totalCacheOps = cacheHits.length + cacheMisses.length
    const avgResponseTime = responses.length > 0
      ? responses.reduce((sum: number, r: ODataDevToolsEntry) => sum + (r.duration ?? 0), 0) / responses.length
      : 0

    const elapsed = (Date.now() - this.startTime) / 1000
    const rps = elapsed > 0 ? requests.length / elapsed : 0

    return {
      totalRequests: requests.length,
      totalResponses: responses.length,
      totalErrors: errors.length,
      averageResponseTime: Math.round(avgResponseTime),
      cacheHitRatio: totalCacheOps > 0 ? cacheHits.length / totalCacheOps : 0,
      batchOperations: batches.length,
      requestsPerSecond: Math.round(rps * 100) / 100,
    }
  }

  /**
   * Get cache statistics (if cache is registered).
   */
  getCacheStats(): CacheStats | null {
    return this.cache?.getStats() ?? null
  }

  /**
   * Get model info (if model is registered).
   */
  getModelInfo(): {
    hasPendingChanges: boolean
    pendingChangeCount: number
  } | null {
    if (!this.model) return null
    return {
      hasPendingChanges: this.model.hasPendingChanges.value,
      pendingChangeCount: this.model.pendingChangeCount.value,
    }
  }

  /**
   * Get batch manager info (if registered).
   */
  getBatchInfo(): {
    isSubmitting: boolean
    queuedCount: number
    activeGroups: string[]
  } | null {
    if (!this.batchManager) return null
    return {
      isSubmitting: this.batchManager.isSubmitting.value,
      queuedCount: this.batchManager.queuedCount.value,
      activeGroups: this.batchManager.getActiveGroups(),
    }
  }

  /**
   * Clear all entries.
   */
  clear(): void {
    this.entries.value = []
    this.startTime = Date.now()
  }

  /**
   * Export entries as JSON (for debugging).
   */
  exportJson(): string {
    return JSON.stringify({
      entries: this.entries.value,
      stats: this.getStats(),
      cacheStats: this.getCacheStats(),
      modelInfo: this.getModelInfo(),
      batchInfo: this.getBatchInfo(),
      exportedAt: new Date().toISOString(),
    }, null, 2)
  }

  // =========================================================================
  // Private
  // =========================================================================

  private addEntry(entry: ODataDevToolsEntry): void {
    this.entries.value.push(entry)

    // Keep entries bounded
    if (this.entries.value.length > this.maxEntries) {
      this.entries.value = this.entries.value.slice(-this.maxEntries / 2)
    }
  }
}
