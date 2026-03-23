/**
 * ETag Manager — enhanced ETag handling with auto-refresh and conflict resolution.
 *
 * Improvements over the existing etagStore:
 * - Auto-refresh entity on 412 Conflict
 * - Configurable conflict resolution strategies
 * - Server-side diff on conflict (fetch latest → show changes)
 * - Retry with fresh ETag option
 *
 * Usage:
 *   const etags = new ETagManager('myModule')
 *   const result = await etags.updateWithRetry('Customers', '1', patchData)
 */

import { ref, type Ref } from 'vue'
import { odataService, etagStore } from '@/services/odataService'
import type { AxiosError } from 'axios'

export type ConflictStrategy = 'fail' | 'refresh-retry' | 'force-overwrite' | 'manual'

export interface ConflictInfo {
  entitySet: string
  key: string
  /** Data the user tried to save */
  clientData: Record<string, unknown>
  /** Latest data from server after refresh */
  serverData: Record<string, unknown>
  /** Fields that differ between client and server */
  conflictingFields: string[]
  /** The new ETag from the refresh */
  serverEtag: string
}

export interface ETagManagerConfig {
  /** Default strategy when a 412 conflict occurs (default: 'refresh-retry') */
  defaultStrategy?: ConflictStrategy
  /** Max auto-retry attempts (default: 1) */
  maxRetries?: number
  /** Callback when conflict detected in 'manual' mode */
  onConflict?: (info: ConflictInfo) => Promise<'retry' | 'force' | 'cancel'>
}

export class ETagManager {
  private module: string
  private config: Required<ETagManagerConfig>

  /** Reactive conflict state */
  readonly hasConflict: Ref<boolean> = ref(false)
  readonly conflictInfo: Ref<ConflictInfo | null> = ref(null)

  constructor(module: string, config?: ETagManagerConfig) {
    this.module = module
    this.config = {
      defaultStrategy: config?.defaultStrategy ?? 'refresh-retry',
      maxRetries: config?.maxRetries ?? 1,
      onConflict: config?.onConflict ?? (async () => 'cancel' as const),
    }
  }

  /**
   * Update an entity with automatic ETag conflict handling.
   *
   * Depending on the strategy:
   * - 'fail': Throws immediately on 412
   * - 'refresh-retry': Fetches latest entity, retries with fresh ETag
   * - 'force-overwrite': Uses If-Match: * to force
   * - 'manual': Calls onConflict callback for user decision
   */
  async updateWithRetry<T = Record<string, unknown>>(
    entitySet: string,
    key: string,
    data: Partial<T>,
    strategy?: ConflictStrategy
  ): Promise<T> {
    const effectiveStrategy = strategy ?? this.config.defaultStrategy
    let retries = 0

    while (retries <= this.config.maxRetries) {
      try {
        this.hasConflict.value = false
        this.conflictInfo.value = null

        const result = await odataService.update<T>(this.module, entitySet, key, data)
        return result
      } catch (e) {
        const axiosErr = e as AxiosError
        if (axiosErr.response?.status !== 412) {
          throw e
        }

        // 412 Conflict
        retries++

        switch (effectiveStrategy) {
          case 'fail':
            this.hasConflict.value = true
            throw e

          case 'force-overwrite':
            return this.forceUpdate<T>(entitySet, key, data)

          case 'refresh-retry': {
            if (retries > this.config.maxRetries) {
              this.hasConflict.value = true
              throw e
            }
            // Refresh entity to get fresh ETag
            await odataService.getById(this.module, entitySet, key)
            // Retry with new ETag (stored automatically by odataService)
            continue
          }

          case 'manual': {
            const info = await this.buildConflictInfo(entitySet, key, data as Record<string, unknown>)
            this.hasConflict.value = true
            this.conflictInfo.value = info

            const decision = await this.config.onConflict(info)

            switch (decision) {
              case 'retry':
                continue
              case 'force':
                return this.forceUpdate<T>(entitySet, key, data)
              case 'cancel':
              default:
                throw e
            }
          }
        }
      }
    }

    throw new Error('Max retries exceeded for ETag conflict resolution')
  }

  /**
   * Force-update an entity, bypassing ETag check.
   */
  async forceUpdate<T = Record<string, unknown>>(
    entitySet: string,
    key: string,
    data: Partial<T>
  ): Promise<T> {
    etagStore.remove(`${this.module}/${entitySet}/${key}`)
    return odataService.update<T>(this.module, entitySet, key, data, { ifMatch: '*' })
  }

  /**
   * Compute a diff between client changes and server state.
   */
  async getDiff(
    entitySet: string,
    key: string,
    clientData: Record<string, unknown>
  ): Promise<Array<{ field: string; clientValue: unknown; serverValue: unknown }>> {
    const serverData = await odataService.getById<Record<string, unknown>>(
      this.module,
      entitySet,
      key
    )

    const diffs: Array<{ field: string; clientValue: unknown; serverValue: unknown }> = []
    for (const field of Object.keys(clientData)) {
      if (field.startsWith('@') || field.startsWith('_')) continue
      if (clientData[field] !== serverData[field]) {
        diffs.push({
          field,
          clientValue: clientData[field],
          serverValue: serverData[field],
        })
      }
    }
    return diffs
  }

  /**
   * Clear conflict state.
   */
  clearConflict(): void {
    this.hasConflict.value = false
    this.conflictInfo.value = null
  }

  // =========================================================================
  // Private
  // =========================================================================

  private async buildConflictInfo(
    entitySet: string,
    key: string,
    clientData: Record<string, unknown>
  ): Promise<ConflictInfo> {
    // Fetch latest from server
    const serverData = await odataService.getById<Record<string, unknown>>(
      this.module,
      entitySet,
      key
    )

    const serverEtag = etagStore.get(`${this.module}/${entitySet}/${key}`) ?? ''

    // Find conflicting fields
    const conflictingFields: string[] = []
    for (const field of Object.keys(clientData)) {
      if (field.startsWith('@') || field.startsWith('_')) continue
      if (clientData[field] !== serverData[field]) {
        conflictingFields.push(field)
      }
    }

    return {
      entitySet,
      key,
      clientData,
      serverData,
      conflictingFields,
      serverEtag,
    }
  }
}
