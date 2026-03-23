/**
 * Declarative Binding Context — Vue composable for declarative OData bindings.
 *
 * Replaces imperative useOData pattern with declarative binding syntax,
 * inspired by OpenUI5's XML view bindings.
 *
 * Usage:
 *   // List binding (declarative)
 *   const { data, totalCount, isLoading } = useBindingContext('/Customers', {
 *     $expand: { Orders: { $top: 5 } },
 *     $orderby: [{ field: 'name', direction: 'asc' }],
 *     $top: 20,
 *   })
 *
 *   // Entity binding (declarative)
 *   const { entity, isDirty, setProperty } = useEntityBinding('/Customers', '123', {
 *     $expand: { Orders: true, Account: true },
 *   })
 *
 *   // Relative binding (within parent context)
 *   const { data: orders } = useRelativeBinding(customerContext, 'Orders', {
 *     $top: 10,
 *     $orderby: [{ field: 'createdAt', direction: 'desc' }],
 *   })
 */

import { ref, computed, onUnmounted, type Ref, type ComputedRef } from 'vue'
import { odataService, etagStore } from '@/services/odataService'
import type { ODataQueryOptions, FilterCondition, SortOption, ODataResponse } from '@/types/odata'
import { buildODataFilter, buildExpandString, type ExpandOptions } from '@/utils/odataQueryBuilder'
import { createRequestScope } from '@/utils/requestDedup'
import type { BindingContext, BindingOptions, TemporalOptions, ExpandBindingOptions } from './types'
import { ChangeTracker } from './ChangeTracker'

// ---------------------------------------------------------------------------
// List Binding Composable
// ---------------------------------------------------------------------------

export interface UseListBindingReturn<T> {
  data: Ref<T[]>
  totalCount: Ref<number>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  currentPage: Ref<number>
  pageSize: Ref<number>
  totalPages: ComputedRef<number>
  hasMore: ComputedRef<boolean>
  isEmpty: ComputedRef<boolean>

  // Mutations
  refresh: () => Promise<void>
  goToPage: (page: number) => Promise<void>
  setPageSize: (size: number) => Promise<void>
  sort: (field: string, direction?: 'asc' | 'desc') => Promise<void>
  filter: (filters: FilterCondition[]) => Promise<void>
  search: (query: string) => Promise<void>
  setSort: (sort: SortOption[]) => void
  setSearch: (query: string) => void
  setExpand: (expands: Record<string, ExpandBindingOptions | true>) => void
  setSelect: (fields: string[]) => void
  setTemporal: (temporal: TemporalOptions) => void

  // Context
  context: BindingContext
}

export function useBindingContext<T = Record<string, unknown>>(
  path: string,
  module: string,
  options?: BindingOptions & { autoLoad?: boolean }
): UseListBindingReturn<T> {
  const entitySet = extractEntitySet(path)
  const context: BindingContext = { module, entitySet }
  const requestScope = createRequestScope()

  // Reactive state
  const data = ref<T[]>([]) as Ref<T[]>
  const totalCount = ref(0)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const currentPage = ref(1)
  const pageSize = ref(options?.$top ?? 20)
  const currentFilters = ref<FilterCondition[]>(options?.$filter ?? [])
  const currentSort = ref<SortOption[]>(options?.$orderby ?? [])
  const currentSearch = ref(options?.$search ?? '')
  const currentExpand = ref<Record<string, ExpandBindingOptions | true>>(options?.$expand ?? {})
  const currentSelect = ref<string[]>(options?.$select ?? [])
  const currentTemporal = ref<TemporalOptions>(options?.temporal ?? {})

  const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value) || 1)
  const hasMore = computed(() => currentPage.value < totalPages.value)
  const isEmpty = computed(() => data.value.length === 0 && !isLoading.value)

  function buildQuery(): ODataQueryOptions {
    const opts: ODataQueryOptions = {
      $count: true,
      $top: pageSize.value,
      $skip: (currentPage.value - 1) * pageSize.value,
    }

    if (currentSort.value.length > 0) {
      opts.$orderby = currentSort.value.map((s: SortOption) => `${s.field} ${s.direction}`).join(',')
    }
    if (currentFilters.value.length > 0) {
      opts.$filter = buildODataFilter(currentFilters.value)
    }
    if (currentSearch.value) {
      opts.$search = currentSearch.value
    }
    if (currentSelect.value.length > 0) {
      opts.$select = currentSelect.value.join(',')
    }
    if (Object.keys(currentExpand.value).length > 0) {
      opts.$expand = buildExpandString(currentExpand.value as Record<string, ExpandOptions | true>)
    }
    if (options?.$apply) {
      opts.$apply = options.$apply
    }

    // Temporal
    const t = currentTemporal.value
    if (t.asOf) opts.asOf = t.asOf
    if (t.validAt) opts.validAt = t.validAt
    if (t.includeHistory) opts.includeHistory = true

    return opts
  }

  async function load(): Promise<void> {
    const signal = requestScope.getSignal()
    isLoading.value = true
    error.value = null

    try {
      const response: ODataResponse<T> = await odataService.query(
        module, entitySet, buildQuery(), { signal }
      )
      if (!signal.aborted) {
        data.value = response.value
        totalCount.value = response['@odata.count'] ?? response.value.length
      }
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      if (signal.aborted) return
      error.value = e instanceof Error ? e.message : 'Failed to load data'
    } finally {
      if (!signal.aborted) isLoading.value = false
    }
  }

  onUnmounted(() => requestScope.cancel())

  // Auto-load
  if (options?.autoLoad !== false) {
    load()
  }

  return {
    data,
    totalCount,
    isLoading,
    error,
    currentPage,
    pageSize,
    totalPages,
    hasMore,
    isEmpty,

    refresh: load,

    goToPage: async (page: number) => {
      if (page < 1 || page > totalPages.value) return
      currentPage.value = page
      await load()
    },

    setPageSize: async (size: number) => {
      pageSize.value = size
      currentPage.value = 1
      await load()
    },

    sort: async (field: string, direction: 'asc' | 'desc' = 'asc') => {
      currentSort.value = [{ field, direction }]
      currentPage.value = 1
      await load()
    },

    filter: async (filters: FilterCondition[]) => {
      currentFilters.value = [...filters]
      currentPage.value = 1
      await load()
    },

    search: async (query: string) => {
      currentSearch.value = query
      currentPage.value = 1
      await load()
    },

    setSort: (sort: SortOption[]) => {
      currentSort.value = [...sort]
    },

    setSearch: (query: string) => {
      currentSearch.value = query
    },

    setExpand: (expands: Record<string, ExpandBindingOptions | true>) => {
      currentExpand.value = expands
    },

    setSelect: (fields: string[]) => {
      currentSelect.value = fields
    },

    setTemporal: (temporal: TemporalOptions) => {
      currentTemporal.value = temporal
    },

    context,
  }
}

// ---------------------------------------------------------------------------
// Entity Binding Composable
// ---------------------------------------------------------------------------

export interface UseEntityBindingReturn<T> {
  entity: Ref<T | null>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  isDirty: ComputedRef<boolean>
  dirtyFields: ComputedRef<string[]>
  concurrencyError: Ref<boolean>

  refresh: () => Promise<void>
  setProperty: (field: string, value: unknown) => void
  resetChanges: () => void
  save: () => Promise<T>
  forceSave: () => Promise<T>
  remove: () => Promise<void>
  getPatch: () => Record<string, unknown> | null

  context: BindingContext
}

export function useEntityBinding<T = Record<string, unknown>>(
  path: string,
  module: string,
  key: string,
  options?: Pick<BindingOptions, '$select' | '$expand'> & { autoLoad?: boolean }
): UseEntityBindingReturn<T> {
  const entitySet = extractEntitySet(path)
  const context: BindingContext = { module, entitySet, key }
  const storeKey = `${entitySet}/${key}`
  const changeTracker = new ChangeTracker()

  const entity = ref<T | null>(null) as Ref<T | null>
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const concurrencyError = ref(false)

  const isDirty = computed(() => changeTracker.isDirty(storeKey))
  const dirtyFields = computed(() => changeTracker.getDirtyFields(storeKey))

  async function load(): Promise<void> {
    isLoading.value = true
    error.value = null
    concurrencyError.value = false

    try {
      const opts: Record<string, string> = {}
      if (options?.$select?.length) opts.$select = options.$select.join(',')
      if (options?.$expand) {
        opts.$expand = buildExpandString(options.$expand as Record<string, ExpandOptions | true>)
      }

      const result = await odataService.getById<T>(module, entitySet, key, opts)
      entity.value = result
      changeTracker.snapshot(storeKey, result as Record<string, unknown>)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load entity'
    } finally {
      isLoading.value = false
    }
  }

  if (options?.autoLoad !== false) {
    load()
  }

  return {
    entity,
    isLoading,
    error,
    isDirty,
    dirtyFields,
    concurrencyError,

    refresh: load,

    setProperty: (field: string, value: unknown) => {
      if (!entity.value) return
      ;(entity.value as Record<string, unknown>)[field] = value
      changeTracker.markDirty(storeKey, field)
    },

    resetChanges: () => {
      const original = changeTracker.getOriginal(storeKey)
      if (original && entity.value) {
        for (const field of changeTracker.getDirtyFields(storeKey)) {
          ;(entity.value as Record<string, unknown>)[field] = original[field]
        }
        changeTracker.clearDirty(storeKey)
      }
    },

    save: async (): Promise<T> => {
      if (!entity.value) throw new Error('No entity loaded')
      const patch = changeTracker.buildPatch(storeKey, entity.value as Record<string, unknown>)
      if (!patch) throw new Error('No changes to save')

      try {
        concurrencyError.value = false
        const result = await odataService.update<T>(module, entitySet, key, patch as Partial<T>)
        entity.value = result
        changeTracker.updateSnapshot(storeKey, result as Record<string, unknown>)
        return result
      } catch (e) {
        const axiosErr = e as { response?: { status?: number } }
        if (axiosErr.response?.status === 412) {
          concurrencyError.value = true
          error.value = 'This record was modified by another user.'
        }
        throw e
      }
    },

    forceSave: async (): Promise<T> => {
      if (!entity.value) throw new Error('No entity loaded')
      const patch = changeTracker.buildPatch(storeKey, entity.value as Record<string, unknown>)
      if (!patch) throw new Error('No changes to save')

      etagStore.remove(`${module}/${entitySet}/${key}`)
      const result = await odataService.update<T>(module, entitySet, key, patch as Partial<T>, { ifMatch: '*' })
      entity.value = result
      changeTracker.updateSnapshot(storeKey, result as Record<string, unknown>)
      concurrencyError.value = false
      return result
    },

    remove: async () => {
      try {
        concurrencyError.value = false
        await odataService.delete(module, entitySet, key)
        entity.value = null
        changeTracker.remove(storeKey)
      } catch (e) {
        const axiosErr = e as { response?: { status?: number } }
        if (axiosErr.response?.status === 412) {
          concurrencyError.value = true
          error.value = 'This record was modified by another user.'
        }
        throw e
      }
    },

    getPatch: () => {
      if (!entity.value) return null
      return changeTracker.buildPatch(storeKey, entity.value as Record<string, unknown>)
    },

    context,
  }
}

// ---------------------------------------------------------------------------
// Relative Binding (for navigation properties)
// ---------------------------------------------------------------------------

export interface UseRelativeBindingReturn<T> {
  data: Ref<T[]>
  totalCount: Ref<number>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  currentPage: Ref<number>
  pageSize: Ref<number>

  refresh: () => Promise<void>
  goToPage: (page: number) => Promise<void>
}

export function useRelativeBinding<T = Record<string, unknown>>(
  parentContext: BindingContext,
  navigationProperty: string,
  options?: { $top?: number; $filter?: FilterCondition[]; $orderby?: SortOption[]; autoLoad?: boolean }
): UseRelativeBindingReturn<T> {
  if (!parentContext.key) {
    throw new Error('Parent context must have a key for relative binding')
  }

  const requestScope = createRequestScope()
  const data = ref<T[]>([]) as Ref<T[]>
  const totalCount = ref(0)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const currentPage = ref(1)
  const pageSize = ref(options?.$top ?? 10)

  async function load(): Promise<void> {
    const signal = requestScope.getSignal()
    isLoading.value = true
    error.value = null

    try {
      const queryOptions: ODataQueryOptions = {
        $count: true,
        $top: pageSize.value,
        $skip: (currentPage.value - 1) * pageSize.value,
      }

      if (options?.$filter?.length) {
        queryOptions.$filter = buildODataFilter(options.$filter)
      }
      if (options?.$orderby?.length) {
        queryOptions.$orderby = options.$orderby.map(s => `${s.field} ${s.direction}`).join(',')
      }

      const response = await odataService.getChildren<T>(
        parentContext.module,
        parentContext.entitySet,
        parentContext.key!,
        navigationProperty,
        queryOptions
      )

      if (!signal.aborted) {
        data.value = response.value
        totalCount.value = response['@odata.count'] ?? response.value.length
      }
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      if (signal.aborted) return
      error.value = e instanceof Error ? e.message : 'Failed to load navigation data'
    } finally {
      if (!signal.aborted) isLoading.value = false
    }
  }

  onUnmounted(() => requestScope.cancel())

  if (options?.autoLoad !== false) {
    load()
  }

  return {
    data,
    totalCount,
    isLoading,
    error,
    currentPage,
    pageSize,
    refresh: load,
    goToPage: async (page: number) => {
      currentPage.value = page
      await load()
    },
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function extractEntitySet(path: string): string {
  const clean = path.startsWith('/') ? path.slice(1) : path
  const parenIndex = clean.indexOf('(')
  return parenIndex > 0 ? clean.slice(0, parenIndex) : clean
}
