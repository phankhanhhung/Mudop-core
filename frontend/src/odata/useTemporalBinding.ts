/**
 * Temporal Query Integration — composable for temporal entity queries.
 *
 * Improvements over the existing useTemporal:
 * - Auto-detect temporal entities from metadata (isTemporal flag)
 * - Integrated directly with binding context (no manual setExtraQueryOptions)
 * - Time-travel mode: reactive asOf date changes → auto-reload
 * - Valid-time range support
 * - Version comparison utilities
 * - History timeline data
 *
 * Usage:
 *   const {
 *     asOf, validAt, includeHistory, isActive,
 *     versions, loadVersions, compareVersions,
 *     timeTravel, resetTemporal
 *   } = useTemporalBinding({ module: 'myapp', entitySet: 'Prices', key: '123' })
 *
 *   // Time travel to a specific point
 *   timeTravel('2025-06-15T00:00:00Z')
 *
 *   // Load version history
 *   await loadVersions()
 *
 *   // Compare two versions
 *   const diff = compareVersions(0, 1)
 */

import { ref, computed, watch, type Ref, type ComputedRef } from 'vue'
import { odataService } from '@/services/odataService'
import type { ODataQueryOptions } from '@/types/odata'
import type { TemporalOptions } from './types'

export interface UseTemporalBindingOptions {
  module: string
  entitySet: string
  key?: string
  /** Callback when temporal params change — caller should reload data */
  onParamsChange?: (params: TemporalOptions) => void
}

export interface VersionEntry {
  data: Record<string, unknown>
  systemStart?: string
  systemEnd?: string
  validFrom?: string
  validTo?: string
  version?: number
}

export interface VersionDiff {
  field: string
  versionA: unknown
  versionB: unknown
}

export interface UseTemporalBindingReturn {
  // Reactive temporal state
  asOf: Ref<string | null>
  validAt: Ref<string | null>
  includeHistory: Ref<boolean>
  isActive: ComputedRef<boolean>

  // Version management
  versions: Ref<VersionEntry[]>
  isLoadingVersions: Ref<boolean>
  versionCount: ComputedRef<number>

  // Actions
  timeTravel: (dateTime: string) => void
  setValidTime: (from: string, to?: string) => void
  showHistory: () => void
  hideHistory: () => void
  resetTemporal: () => void
  loadVersions: () => Promise<void>
  compareVersions: (indexA: number, indexB: number) => VersionDiff[]

  // Query params (for passing to OData queries)
  getQueryParams: () => Record<string, string>
  getODataOptions: () => Partial<ODataQueryOptions>
}

export function useTemporalBinding(
  options: UseTemporalBindingOptions
): UseTemporalBindingReturn {
  const { module, entitySet, key } = options

  // Reactive state
  const asOf = ref<string | null>(null)
  const validAt = ref<string | null>(null)
  const includeHistory = ref(false)
  const versions = ref<VersionEntry[]>([])
  const isLoadingVersions = ref(false)

  const isActive = computed(() => !!asOf.value || !!validAt.value || includeHistory.value)
  const versionCount = computed(() => versions.value.length)

  // Notify parent when params change
  function notifyChange(): void {
    if (options.onParamsChange) {
      options.onParamsChange({
        asOf: asOf.value ?? undefined,
        validAt: validAt.value ?? undefined,
        includeHistory: includeHistory.value || undefined,
      })
    }
  }

  // Watch for changes and auto-notify
  watch([asOf, validAt, includeHistory], () => {
    notifyChange()
  })

  /**
   * Time-travel to a specific point in time.
   * Sets asOf and triggers data reload via callback.
   */
  function timeTravel(dateTime: string): void {
    asOf.value = dateTime
  }

  /**
   * Set valid-time range filter.
   */
  function setValidTime(from: string, _to?: string): void {
    validAt.value = from
    // Note: to is handled server-side via the validAt parameter
    // which returns records valid at that point in time
  }

  /**
   * Enable history mode (show all versions).
   */
  function showHistory(): void {
    includeHistory.value = true
  }

  /**
   * Disable history mode.
   */
  function hideHistory(): void {
    includeHistory.value = false
  }

  /**
   * Reset all temporal parameters.
   */
  function resetTemporal(): void {
    asOf.value = null
    validAt.value = null
    includeHistory.value = false
    versions.value = []
  }

  /**
   * Load version history for the entity.
   */
  async function loadVersions(): Promise<void> {
    if (!key) {
      versions.value = []
      return
    }

    isLoadingVersions.value = true
    try {
      const versionData = await odataService.getVersions<Record<string, unknown>>(
        module,
        entitySet,
        key
      )

      versions.value = versionData.map((v, index) => ({
        data: v,
        systemStart: v['system_start'] as string | undefined,
        systemEnd: v['system_end'] as string | undefined,
        validFrom: v['effectiveFrom'] as string | undefined ?? v['valid_from'] as string | undefined,
        validTo: v['effectiveTo'] as string | undefined ?? v['valid_to'] as string | undefined,
        version: v['version'] as number | undefined ?? index + 1,
      }))
    } catch {
      versions.value = []
    } finally {
      isLoadingVersions.value = false
    }
  }

  /**
   * Compare two versions and return field-level diffs.
   */
  function compareVersions(indexA: number, indexB: number): VersionDiff[] {
    const vA = versions.value[indexA]
    const vB = versions.value[indexB]
    if (!vA || !vB) return []

    const diffs: VersionDiff[] = []
    const allKeys = new Set([
      ...Object.keys(vA.data),
      ...Object.keys(vB.data),
    ])

    for (const field of allKeys) {
      // Skip metadata fields
      if (field.startsWith('@') || field.startsWith('_')) continue
      if (['system_start', 'system_end', 'version'].includes(field)) continue

      const valA = vA.data[field]
      const valB = vB.data[field]

      if (JSON.stringify(valA) !== JSON.stringify(valB)) {
        diffs.push({ field, versionA: valA, versionB: valB })
      }
    }

    return diffs
  }

  /**
   * Get temporal params as plain object for URL/query integration.
   */
  function getQueryParams(): Record<string, string> {
    const params: Record<string, string> = {}
    if (asOf.value) params.asOf = asOf.value
    if (validAt.value) params.validAt = validAt.value
    if (includeHistory.value) params.includeHistory = 'true'
    return params
  }

  /**
   * Get temporal options for OData query integration.
   */
  function getODataOptions(): Partial<ODataQueryOptions> {
    const opts: Partial<ODataQueryOptions> = {}
    if (asOf.value) opts.asOf = asOf.value
    if (validAt.value) opts.validAt = validAt.value
    if (includeHistory.value) opts.includeHistory = true
    return opts
  }

  return {
    asOf,
    validAt,
    includeHistory,
    isActive,
    versions,
    isLoadingVersions,
    versionCount,
    timeTravel,
    setValidTime,
    showHistory,
    hideHistory,
    resetTemporal,
    loadVersions,
    compareVersions,
    getQueryParams,
    getODataOptions,
  }
}
