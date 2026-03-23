import { ref, computed, watch, nextTick, onUnmounted, type Ref } from 'vue'
import { odataService, etagStore } from '@/services'
import type { ODataQueryOptions, ODataResponse, SortOption, FilterCondition } from '@/types/odata'
import { buildODataFilter, buildExpandString, type ExpandOptions } from '@/utils/odataQueryBuilder'
import { createRequestScope } from '@/utils/requestDedup'
import type { AxiosError } from 'axios'

export interface ODataInitialState {
  page?: number
  pageSize?: number
  sort?: SortOption[]
  search?: string
  filters?: FilterCondition[]
}

export interface UseODataOptions {
  module: string
  entitySet: string
  initialPageSize?: number
  autoLoad?: boolean
  initialState?: ODataInitialState
  onStateChange?: (state: {
    page: number
    pageSize: number
    sort: SortOption[]
    search: string
    filters: FilterCondition[]
  }) => void
}

export interface UseODataReturn<T> {
  data: Ref<T[]>
  totalCount: Ref<number>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  concurrencyError: Ref<boolean>
  currentPage: Ref<number>
  pageSize: Ref<number>
  totalPages: Ref<number>
  sortOptions: Ref<SortOption[]>
  filters: Ref<FilterCondition[]>
  search: Ref<string>
  selectedFields: Ref<string[]>
  expandFields: Ref<string[]>
  expandOptions: Ref<Record<string, ExpandOptions | true>>
  load: () => Promise<void>
  refresh: () => Promise<void>
  goToPage: (page: number) => Promise<void>
  setPageSize: (size: number) => Promise<void>
  setSort: (field: string, direction?: 'asc' | 'desc') => Promise<void>
  addFilter: (filter: FilterCondition) => void
  removeFilter: (field: string) => void
  clearFilters: () => void
  setFilters: (newFilters: FilterCondition[]) => Promise<void>
  setSearch: (query: string) => Promise<void>
  setSelect: (fields: string[]) => void
  setExpand: (fields: string[]) => void
  setExpandOptions: (options: Record<string, ExpandOptions | true>) => void
  setExtraQueryOptions: (opts: Partial<ODataQueryOptions>) => void
  getById: (id: string) => Promise<T>
  create: (data: Partial<T>) => Promise<T>
  update: (id: string, data: Partial<T>) => Promise<T>
  remove: (id: string) => Promise<void>
  updateWithoutEtag: (id: string, data: Partial<T>) => Promise<T>
}

export function useOData<T = Record<string, unknown>>(
  options: UseODataOptions
): UseODataReturn<T> {
  const { module, entitySet, initialPageSize = 20, autoLoad = true } = options

  // Resolve initial values: initialState takes precedence over defaults
  const initPage = options.initialState?.page ?? 1
  const initPageSize = options.initialState?.pageSize ?? initialPageSize
  const initSort = options.initialState?.sort ?? []
  const initSearch = options.initialState?.search ?? ''
  const initFilters = options.initialState?.filters ?? []

  // State
  const data = ref<T[]>([]) as Ref<T[]>
  const totalCount = ref(0)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const concurrencyError = ref(false)
  const currentPage = ref(initPage)
  const pageSize = ref(initPageSize)
  const sortOptions = ref<SortOption[]>(initSort)
  const filters = ref<FilterCondition[]>(initFilters)
  const search = ref(initSearch)
  const selectedFields = ref<string[]>([])
  const expandFields = ref<string[]>([])
  const expandOptions = ref<Record<string, ExpandOptions | true>>({})
  const extraQueryOptions = ref<Partial<ODataQueryOptions>>({})

  // Request cancellation scope — cancels previous in-flight request on new load
  const requestScope = createRequestScope()

  // Cancel in-flight requests on component unmount
  onUnmounted(() => {
    requestScope.cancel()
  })

  // Computed
  const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value) || 1)

  // Notify state change callback
  function notifyStateChange(): void {
    if (options.onStateChange) {
      options.onStateChange({
        page: currentPage.value,
        pageSize: pageSize.value,
        sort: sortOptions.value,
        search: search.value,
        filters: filters.value
      })
    }
  }

  // Build query options
  function buildQueryOptions(): ODataQueryOptions {
    const opts: ODataQueryOptions = {
      $count: true,
      $top: pageSize.value,
      $skip: (currentPage.value - 1) * pageSize.value
    }

    // Add sorting
    if (sortOptions.value.length > 0) {
      opts.$orderby = sortOptions.value
        .map((s) => `${s.field} ${s.direction}`)
        .join(',')
    }

    // Add filters
    if (filters.value.length > 0) {
      opts.$filter = buildODataFilter(filters.value)
    }

    // Add search
    if (search.value) {
      opts.$search = search.value
    }

    // Add select
    if (selectedFields.value.length > 0) {
      opts.$select = selectedFields.value.join(',')
    }

    // Add expand
    if (Object.keys(expandOptions.value).length > 0) {
      opts.$expand = buildExpandString(expandOptions.value)
    } else if (expandFields.value.length > 0) {
      opts.$expand = expandFields.value.join(',')
    }

    // Merge extra options (e.g. temporal params)
    Object.assign(opts, extraQueryOptions.value)

    return opts
  }

  // Load data — cancels any previous in-flight request
  async function load(): Promise<void> {
    const signal = requestScope.getSignal()
    isLoading.value = true
    error.value = null

    try {
      const queryOptions = buildQueryOptions()
      const response: ODataResponse<T> = await odataService.query(
        module, entitySet, queryOptions, { signal }
      )
      // Only update state if this request wasn't cancelled
      if (!signal.aborted) {
        data.value = response.value
        totalCount.value = response['@odata.count'] ?? response.value.length
      }
    } catch (e) {
      // Ignore aborted requests (user navigated away or new request started)
      if (e instanceof DOMException && e.name === 'AbortError') return
      if (signal.aborted) return
      error.value = e instanceof Error ? e.message : 'Failed to load data'
      throw e
    } finally {
      if (!signal.aborted) {
        isLoading.value = false
      }
    }
  }

  async function refresh(): Promise<void> {
    await load()
  }

  async function goToPage(page: number): Promise<void> {
    if (page < 1 || page > totalPages.value) return
    currentPage.value = page
    notifyStateChange()
    await load()
  }

  async function setPageSize(size: number): Promise<void> {
    pageSize.value = size
    currentPage.value = 1
    notifyStateChange()
    await load()
  }

  async function setSort(field: string, direction: 'asc' | 'desc' = 'asc'): Promise<void> {
    const existingIndex = sortOptions.value.findIndex((s) => s.field === field)
    if (existingIndex >= 0) {
      // Toggle or remove
      const current = sortOptions.value[existingIndex]
      if (current.direction === direction) {
        // Remove
        sortOptions.value.splice(existingIndex, 1)
      } else {
        // Toggle direction
        sortOptions.value[existingIndex] = { field, direction }
      }
    } else {
      // Add new sort (replace existing for single sort)
      sortOptions.value = [{ field, direction }]
    }
    currentPage.value = 1
    notifyStateChange()
    await load()
  }

  function addFilter(filter: FilterCondition): void {
    const existingIndex = filters.value.findIndex((f) => f.field === filter.field)
    if (existingIndex >= 0) {
      filters.value[existingIndex] = filter
    } else {
      filters.value.push(filter)
    }
    // Note: notifyStateChange is called by the filter watcher below
  }

  function removeFilter(field: string): void {
    filters.value = filters.value.filter((f) => f.field !== field)
    // Note: notifyStateChange is called by the filter watcher below
  }

  function clearFilters(): void {
    filters.value = []
    // Note: notifyStateChange is called by the filter watcher below
  }

  // Batch-replace all filters in one operation (single load, no cascade)
  let suppressFilterWatch = false

  async function setFilters(newFilters: FilterCondition[]): Promise<void> {
    suppressFilterWatch = true
    filters.value = [...newFilters]
    currentPage.value = 1
    // Wait for Vue to flush the filter watcher (which will see suppressFilterWatch=true
    // and skip the reload), then reset the flag and do a single load
    await nextTick()
    suppressFilterWatch = false
    notifyStateChange()
    await load()
  }

  async function setSearch(query: string): Promise<void> {
    search.value = query
    currentPage.value = 1
    notifyStateChange()
    await load()
  }

  function setSelect(fields: string[]): void {
    selectedFields.value = fields
  }

  function setExpand(fields: string[]): void {
    expandFields.value = fields
  }

  function setExpandOptions(options: Record<string, ExpandOptions | true>): void {
    expandOptions.value = options
  }

  function setExtraQueryOptions(opts: Partial<ODataQueryOptions>): void {
    extraQueryOptions.value = opts
  }

  async function getById(id: string): Promise<T> {
    const opts: Pick<ODataQueryOptions, '$select' | '$expand'> = {}
    if (selectedFields.value.length > 0) {
      opts.$select = selectedFields.value.join(',')
    }
    if (Object.keys(expandOptions.value).length > 0) {
      opts.$expand = buildExpandString(expandOptions.value)
    } else if (expandFields.value.length > 0) {
      opts.$expand = expandFields.value.join(',')
    }
    return odataService.getById<T>(module, entitySet, id, opts)
  }

  async function create(entityData: Partial<T>): Promise<T> {
    const result = await odataService.create<T>(module, entitySet, entityData)
    // Refresh list after create
    await load()
    return result
  }

  async function update(id: string, entityData: Partial<T>): Promise<T> {
    try {
      concurrencyError.value = false
      const result = await odataService.update<T>(module, entitySet, id, entityData)
      // Refresh list after update
      await load()
      return result
    } catch (e) {
      const axiosErr = e as AxiosError
      if (axiosErr.response?.status === 412) {
        concurrencyError.value = true
        error.value = 'This record was modified by another user.'
      }
      throw e
    }
  }

  async function updateWithoutEtag(id: string, entityData: Partial<T>): Promise<T> {
    concurrencyError.value = false
    // Remove stored ETag so no If-Match is sent, and pass '*' to force overwrite
    etagStore.remove(`${module}/${entitySet}/${id}`)
    const result = await odataService.update<T>(module, entitySet, id, entityData, {
      ifMatch: '*'
    })
    await load()
    return result
  }

  async function remove(id: string): Promise<void> {
    try {
      concurrencyError.value = false
      await odataService.delete(module, entitySet, id)
      // Refresh list after delete
      await load()
    } catch (e) {
      const axiosErr = e as AxiosError
      if (axiosErr.response?.status === 412) {
        concurrencyError.value = true
        error.value = 'This record was modified by another user.'
      }
      throw e
    }
  }

  // Watch for filter changes and reload (skipped during batch setFilters)
  watch(
    filters,
    async () => {
      if (suppressFilterWatch) return
      currentPage.value = 1
      notifyStateChange()
      await load()
    },
    { deep: true }
  )

  // Auto-load on mount
  if (autoLoad) {
    load().catch(err => {
      console.error('Auto-load failed:', err)
    })
  }

  return {
    data,
    totalCount,
    isLoading,
    error,
    concurrencyError,
    currentPage,
    pageSize,
    totalPages,
    sortOptions,
    filters,
    search,
    selectedFields,
    expandFields,
    expandOptions,
    load,
    refresh,
    goToPage,
    setPageSize,
    setSort,
    addFilter,
    removeFilter,
    clearFilters,
    setFilters,
    setSearch,
    setSelect,
    setExpand,
    setExpandOptions,
    setExtraQueryOptions,
    getById,
    create,
    update,
    remove,
    updateWithoutEtag
  }
}
