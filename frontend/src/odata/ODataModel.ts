/**
 * Reactive OData Model — framework-level data management layer.
 *
 * Inspired by OpenUI5's ODataModel, this provides:
 * - Reactive entity store (Vue reactivity under the hood)
 * - Property and list bindings
 * - Automatic change tracking (dirty fields)
 * - Pending change management (create/update/delete)
 * - Integration with BatchManager for auto-batching
 * - ETag-based optimistic concurrency
 *
 * Usage:
 *   const model = new ODataModel({ module: 'myapp' })
 *   const list = model.bindList('/Customers', { $top: 20 })
 *   const prop = model.bindProperty('/Customers(1)/name')
 *   model.submitChanges()
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue'
import { odataService, etagStore } from '@/services/odataService'
import type {
  BoundEntity,
  BindingContext,
  BindingOptions,
  PendingChange,
} from './types'
import { buildODataFilter, buildExpandString, type ExpandOptions } from '@/utils/odataQueryBuilder'
import { ChangeTracker } from './ChangeTracker'
import { BatchManager } from './BatchManager'
import { EntityCache } from './EntityCache'

// ---------------------------------------------------------------------------
// ODataModel Configuration
// ---------------------------------------------------------------------------

export interface ODataModelConfig {
  /** Module name for all operations */
  module: string
  /** Default batch group ID (default: '$auto') */
  defaultBatchGroup?: string
  /** Whether to enable auto-batching (default: true) */
  autoBatch?: boolean
  /** Default page size for list bindings (default: 20) */
  defaultPageSize?: number
  /** Entity cache TTL in ms (default: 60000) */
  cacheTtl?: number
}

// ---------------------------------------------------------------------------
// List Binding Result
// ---------------------------------------------------------------------------

export interface ReactiveListBinding<T = Record<string, unknown>> {
  /** Reactive data array */
  data: Ref<T[]>
  /** Reactive total count */
  totalCount: Ref<number>
  /** Whether data is loading */
  isLoading: Ref<boolean>
  /** Error message */
  error: Ref<string | null>
  /** Current page (1-based) */
  currentPage: Ref<number>
  /** Page size */
  pageSize: Ref<number>
  /** Total pages */
  totalPages: ComputedRef<number>
  /** Whether more data is available */
  hasMore: ComputedRef<boolean>
  /** Refresh the current binding */
  refresh: () => Promise<void>
  /** Go to page */
  goToPage: (page: number) => Promise<void>
  /** Sort by field */
  sort: (field: string, direction?: 'asc' | 'desc') => Promise<void>
  /** Apply filters */
  filter: (filters: import('@/types/odata').FilterCondition[]) => Promise<void>
  /** Set search query */
  search: (query: string) => Promise<void>
  /** Request next page (append) */
  requestMore: () => Promise<void>
  /** Get binding context */
  getContext: () => BindingContext
  /** Destroy this binding and release resources */
  destroy: () => void
}

// ---------------------------------------------------------------------------
// Property Binding Result
// ---------------------------------------------------------------------------

export interface ReactivePropertyBinding<T = unknown> {
  /** Reactive value */
  value: Ref<T | undefined>
  /** Whether loading */
  isLoading: Ref<boolean>
  /** Error message */
  error: Ref<string | null>
  /** Refresh the value */
  refresh: () => Promise<void>
  /** Set the value (marks entity as dirty) */
  setValue: (newValue: T) => void
  /** Get the binding context */
  getContext: () => BindingContext
}

// ---------------------------------------------------------------------------
// Entity Context Result
// ---------------------------------------------------------------------------

export interface ReactiveEntityContext<T = Record<string, unknown>> {
  /** Reactive entity data */
  data: Ref<T | null>
  /** Whether loading */
  isLoading: Ref<boolean>
  /** Error message */
  error: Ref<string | null>
  /** Whether the entity has pending changes */
  isDirty: ComputedRef<boolean>
  /** Set of dirty field names */
  dirtyFields: ComputedRef<string[]>
  /** Concurrency conflict detected */
  concurrencyError: Ref<boolean>
  /** Refresh entity from server */
  refresh: () => Promise<void>
  /** Update a field value */
  setProperty: (field: string, value: unknown) => void
  /** Reset changes to original values */
  resetChanges: () => void
  /** Delete this entity */
  delete: () => Promise<void>
  /** Get the binding context */
  getContext: () => BindingContext
}

// ---------------------------------------------------------------------------
// ODataModel
// ---------------------------------------------------------------------------

export class ODataModel {
  readonly module: string
  private config: Required<ODataModelConfig>

  /** Entity instance store: entitySet/key → BoundEntity */
  private entityStore = new Map<string, BoundEntity>()

  /** Change tracker for dirty state management */
  readonly changeTracker: ChangeTracker

  /** Batch manager for auto-batching */
  readonly batchManager: BatchManager

  /** Entity cache for query results */
  readonly entityCache: EntityCache

  /** Active list bindings for cleanup */
  private activeBindings = new Set<{ destroy: () => void }>()

  /** Pending changes awaiting submission */
  private pendingChanges = ref<PendingChange[]>([])

  /** Whether the model has any pending changes */
  readonly hasPendingChanges: ComputedRef<boolean>

  /** Count of pending changes */
  readonly pendingChangeCount: ComputedRef<number>

  constructor(config: ODataModelConfig) {
    this.module = config.module
    this.config = {
      module: config.module,
      defaultBatchGroup: config.defaultBatchGroup ?? '$auto',
      autoBatch: config.autoBatch ?? true,
      defaultPageSize: config.defaultPageSize ?? 20,
      cacheTtl: config.cacheTtl ?? 60_000,
    }

    this.changeTracker = new ChangeTracker()
    this.batchManager = new BatchManager(this.module)
    this.entityCache = new EntityCache({
      ttl: this.config.cacheTtl,
      maxEntries: 200,
      staleWhileRevalidate: true,
      swrWindow: 10_000,
    })

    this.hasPendingChanges = computed(() => this.pendingChanges.value.length > 0)
    this.pendingChangeCount = computed(() => this.pendingChanges.value.length)
  }

  // =========================================================================
  // List Binding
  // =========================================================================

  /**
   * Create a reactive list binding for an entity set.
   *
   * @example
   *   const customers = model.bindList('/Customers', {
   *     $top: 20,
   *     $expand: { Orders: true },
   *     $orderby: [{ field: 'name', direction: 'asc' }]
   *   })
   */
  bindList<T = Record<string, unknown>>(
    path: string,
    options?: BindingOptions
  ): ReactiveListBinding<T> {
    const entitySet = this.extractEntitySet(path)
    const context: BindingContext = { module: this.module, entitySet }

    const data = ref<T[]>([]) as Ref<T[]>
    const totalCount = ref(0)
    const isLoading = ref(false)
    const error = ref<string | null>(null)
    const currentPage = ref(1)
    const pageSize = ref(options?.$top ?? this.config.defaultPageSize)
    const currentFilters = ref(options?.$filter ?? [])
    const currentSort = ref(options?.$orderby ?? [])
    const currentSearch = ref(options?.$search ?? '')
    let abortController: AbortController | null = null

    const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value) || 1)
    const hasMore = computed(() => currentPage.value < totalPages.value)

    const buildQuery = (): import('@/types/odata').ODataQueryOptions => {
      const opts: import('@/types/odata').ODataQueryOptions = {
        $count: true,
        $top: pageSize.value,
        $skip: (currentPage.value - 1) * pageSize.value,
      }

      if (currentSort.value.length > 0) {
        opts.$orderby = currentSort.value
          .map((s: { field: string; direction: string }) => `${s.field} ${s.direction}`)
          .join(',')
      }

      if (currentFilters.value.length > 0) {
        opts.$filter = buildODataFilter(currentFilters.value)
      }

      if (currentSearch.value) {
        opts.$search = currentSearch.value
      }

      if (options?.$select?.length) {
        opts.$select = options.$select.join(',')
      }

      if (options?.$expand) {
        opts.$expand = buildExpandString(options.$expand as Record<string, ExpandOptions | true>)
      }

      if (options?.$apply) {
        opts.$apply = options.$apply
      }

      // Temporal params
      if (options?.temporal) {
        if (options.temporal.asOf) opts.asOf = options.temporal.asOf
        if (options.temporal.validAt) opts.validAt = options.temporal.validAt
        if (options.temporal.includeHistory) opts.includeHistory = true
      }

      return opts
    }

    const load = async (): Promise<void> => {
      // Cancel previous
      if (abortController) abortController.abort()
      abortController = new AbortController()
      const signal = abortController.signal

      isLoading.value = true
      error.value = null

      try {
        const response = await odataService.query<T>(
          this.module,
          entitySet,
          buildQuery(),
          { signal }
        )

        if (!signal.aborted) {
          data.value = response.value
          totalCount.value = response['@odata.count'] ?? response.value.length

          // Store entities in model cache
          for (const item of response.value) {
            this.storeEntity(entitySet, item as Record<string, unknown>)
          }
        }
      } catch (e) {
        if (e instanceof DOMException && e.name === 'AbortError') return
        if (signal.aborted) return
        error.value = e instanceof Error ? e.message : 'Failed to load data'
      } finally {
        if (!signal.aborted) {
          isLoading.value = false
        }
      }
    }

    const binding: ReactiveListBinding<T> = {
      data,
      totalCount,
      isLoading,
      error,
      currentPage,
      pageSize,
      totalPages,
      hasMore,

      refresh: load,

      goToPage: async (page: number) => {
        if (page < 1 || page > totalPages.value) return
        currentPage.value = page
        await load()
      },

      sort: async (field: string, direction: 'asc' | 'desc' = 'asc') => {
        currentSort.value = [{ field, direction }]
        currentPage.value = 1
        await load()
      },

      filter: async (filters) => {
        currentFilters.value = [...filters]
        currentPage.value = 1
        await load()
      },

      search: async (query: string) => {
        currentSearch.value = query
        currentPage.value = 1
        await load()
      },

      requestMore: async () => {
        if (!hasMore.value || isLoading.value) return
        currentPage.value++
        const signal = new AbortController().signal
        try {
          const response = await odataService.query<T>(
            this.module,
            entitySet,
            { ...buildQuery(), $skip: (currentPage.value - 1) * pageSize.value },
            { signal }
          )
          data.value = [...data.value, ...response.value]
        } catch (e) {
          if (!(e instanceof DOMException && e.name === 'AbortError')) {
            error.value = e instanceof Error ? e.message : 'Failed to load more data'
          }
        }
      },

      getContext: () => context,

      destroy: () => {
        if (abortController) abortController.abort()
        this.activeBindings.delete(binding)
      },
    }

    this.activeBindings.add(binding)

    // Auto-load
    load()

    return binding
  }

  // =========================================================================
  // Entity Context Binding
  // =========================================================================

  /**
   * Create a reactive entity context for a single entity.
   *
   * @example
   *   const customer = model.bindEntity('/Customers', '123', {
   *     $expand: { Orders: true }
   *   })
   *   customer.setProperty('name', 'New Name')
   *   // Later: model.submitChanges()
   */
  bindEntity<T = Record<string, unknown>>(
    path: string,
    key: string,
    options?: Pick<BindingOptions, '$select' | '$expand'>
  ): ReactiveEntityContext<T> {
    const entitySet = this.extractEntitySet(path)
    const context: BindingContext = { module: this.module, entitySet, key }
    const storeKey = `${entitySet}/${key}`

    const data = ref<T | null>(null) as Ref<T | null>
    const isLoading = ref(false)
    const error = ref<string | null>(null)
    const concurrencyError = ref(false)

    const isDirty = computed(() => this.changeTracker.isDirty(storeKey))
    const dirtyFields = computed(() => this.changeTracker.getDirtyFields(storeKey))

    const load = async (): Promise<void> => {
      isLoading.value = true
      error.value = null
      concurrencyError.value = false

      try {
        const opts: Record<string, string> = {}
        if (options?.$select?.length) {
          opts.$select = options.$select.join(',')
        }
        if (options?.$expand) {
          opts.$expand = buildExpandString(options.$expand as Record<string, ExpandOptions | true>)
        }

        const result = await odataService.getById<T>(this.module, entitySet, key, opts)
        data.value = result

        // Store in model
        this.storeEntity(entitySet, result as Record<string, unknown>)
        this.changeTracker.snapshot(storeKey, result as Record<string, unknown>)
      } catch (e) {
        error.value = e instanceof Error ? e.message : 'Failed to load entity'
      } finally {
        isLoading.value = false
      }
    }

    const entityContext: ReactiveEntityContext<T> = {
      data,
      isLoading,
      error,
      isDirty,
      dirtyFields,
      concurrencyError,

      refresh: load,

      setProperty: (field: string, value: unknown) => {
        if (!data.value) return
        const record = data.value as Record<string, unknown>
        record[field] = value
        this.changeTracker.markDirty(storeKey, field)

        // Register pending change
        this.registerPendingUpdate(entitySet, key, field, value)
      },

      resetChanges: () => {
        const original = this.changeTracker.getOriginal(storeKey)
        if (original && data.value) {
          const record = data.value as Record<string, unknown>
          for (const field of this.changeTracker.getDirtyFields(storeKey)) {
            record[field] = original[field]
          }
          this.changeTracker.clearDirty(storeKey)
          this.removePendingChanges(entitySet, key)
        }
      },

      delete: async () => {
        try {
          await odataService.delete(this.module, entitySet, key)
          this.entityStore.delete(storeKey)
          this.changeTracker.remove(storeKey)
          data.value = null
        } catch (e) {
          const axiosErr = e as { response?: { status?: number } }
          if (axiosErr.response?.status === 412) {
            concurrencyError.value = true
            error.value = 'This record was modified by another user.'
          }
          throw e
        }
      },

      getContext: () => context,
    }

    // Auto-load
    load()

    return entityContext
  }

  // =========================================================================
  // Property Binding
  // =========================================================================

  /**
   * Create a reactive property binding.
   *
   * @example
   *   const name = model.bindProperty('/Customers(123)/name')
   */
  bindProperty<T = unknown>(path: string): ReactivePropertyBinding<T> {
    const { entitySet, key, property } = this.parsePath(path)
    if (!key || !property) {
      throw new Error(`Invalid property path: ${path}. Expected format: /EntitySet(key)/property`)
    }

    const context: BindingContext = { module: this.module, entitySet, key }
    const storeKey = `${entitySet}/${key}`

    const value = ref<T | undefined>(undefined) as Ref<T | undefined>
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const load = async (): Promise<void> => {
      isLoading.value = true
      error.value = null
      try {
        const entity = await odataService.getById<Record<string, unknown>>(
          this.module,
          entitySet,
          key
        )
        this.storeEntity(entitySet, entity)
        this.changeTracker.snapshot(storeKey, entity)
        value.value = entity[property] as T
      } catch (e) {
        error.value = e instanceof Error ? e.message : 'Failed to load property'
      } finally {
        isLoading.value = false
      }
    }

    const binding: ReactivePropertyBinding<T> = {
      value,
      isLoading,
      error,
      refresh: load,
      setValue: (newValue: T) => {
        value.value = newValue
        this.changeTracker.markDirty(storeKey, property)
        this.registerPendingUpdate(entitySet, key, property, newValue)
      },
      getContext: () => context,
    }

    load()
    return binding
  }

  // =========================================================================
  // CRUD Operations
  // =========================================================================

  /**
   * Create a new entity.
   */
  async create<T = Record<string, unknown>>(
    entitySet: string,
    data: Partial<T>
  ): Promise<T> {
    const result = await odataService.create<T>(this.module, entitySet, data)
    this.storeEntity(entitySet, result as Record<string, unknown>)
    this.entityCache.invalidateByTag(entitySet)
    return result
  }

  /**
   * Update an entity (only dirty fields).
   */
  async update<T = Record<string, unknown>>(
    entitySet: string,
    key: string,
    data?: Partial<T>
  ): Promise<T> {
    const storeKey = `${entitySet}/${key}`

    // If no data provided, build from dirty fields
    if (!data) {
      const dirtyFields = this.changeTracker.getDirtyFields(storeKey)
      const entity = this.entityStore.get(storeKey)
      if (entity && dirtyFields.length > 0) {
        const patch: Record<string, unknown> = {}
        for (const field of dirtyFields) {
          patch[field] = (entity.data as Record<string, unknown>)[field]
        }
        data = patch as Partial<T>
      }
    }

    if (!data || Object.keys(data).length === 0) {
      throw new Error('No changes to submit')
    }

    const result = await odataService.update<T>(this.module, entitySet, key, data)
    this.storeEntity(entitySet, result as Record<string, unknown>)
    this.changeTracker.snapshot(storeKey, result as Record<string, unknown>)
    this.entityCache.invalidateByTag(entitySet)
    return result
  }

  /**
   * Delete an entity.
   */
  async remove(entitySet: string, key: string): Promise<void> {
    await odataService.delete(this.module, entitySet, key)
    this.entityStore.delete(`${entitySet}/${key}`)
    this.changeTracker.remove(`${entitySet}/${key}`)
    this.entityCache.invalidateByTag(entitySet)
  }

  // =========================================================================
  // Submit Changes (Batch)
  // =========================================================================

  /**
   * Submit all pending changes to the server.
   * Groups changes into a batch request when auto-batching is enabled.
   */
  async submitChanges(): Promise<void> {
    const changes = [...this.pendingChanges.value]
    if (changes.length === 0) return

    if (this.config.autoBatch && changes.length > 1) {
      await this.submitBatch(changes)
    } else {
      await this.submitSequential(changes)
    }

    // Clear pending changes
    this.pendingChanges.value = []
  }

  /**
   * Reset all pending changes, restoring original values.
   */
  resetChanges(): void {
    for (const change of this.pendingChanges.value) {
      if (change.type === 'update' && change.key) {
        const storeKey = `${change.entitySet}/${change.key}`
        const original = this.changeTracker.getOriginal(storeKey)
        if (original) {
          this.entityStore.set(storeKey, {
            data: { ...original },
            _original: { ...original },
            _entitySet: change.entitySet,
            _module: this.module,
            _key: change.key,
            _dirty: false,
            _dirtyFields: new Set(),
            _isNew: false,
          })
        }
        this.changeTracker.clearDirty(storeKey)
      }
    }
    this.pendingChanges.value = []
  }

  /**
   * Get all pending changes.
   */
  getPendingChanges(): PendingChange[] {
    return [...this.pendingChanges.value]
  }

  // =========================================================================
  // Internals
  // =========================================================================

  private storeEntity(entitySet: string, data: Record<string, unknown>): void {
    const id = String(data['Id'] ?? data['ID'] ?? data['id'] ?? '')
    if (!id) return

    const storeKey = `${entitySet}/${id}`
    const etag = data['@odata.etag'] as string | undefined

    const existing = this.entityStore.get(storeKey)
    if (existing) {
      existing.data = data
      if (etag) existing._etag = etag
    } else {
      this.entityStore.set(storeKey, {
        data,
        _original: { ...data },
        _entitySet: entitySet,
        _module: this.module,
        _key: id,
        _etag: etag,
        _dirty: false,
        _dirtyFields: new Set(),
        _isNew: false,
      })
    }

    // Update ETag store
    if (etag) {
      etagStore.set(`${this.module}/${entitySet}/${id}`, etag)
    }
  }

  private registerPendingUpdate(
    entitySet: string,
    key: string,
    field: string,
    value: unknown
  ): void {
    // Find existing pending update for this entity
    const existing = this.pendingChanges.value.find(
      (c: PendingChange) => c.type === 'update' && c.entitySet === entitySet && c.key === key
    )

    if (existing) {
      if (!existing.data) existing.data = {}
      existing.data[field] = value
      if (!existing.dirtyFields) existing.dirtyFields = []
      if (!existing.dirtyFields.includes(field)) {
        existing.dirtyFields.push(field)
      }
    } else {
      this.pendingChanges.value.push({
        type: 'update',
        entitySet,
        module: this.module,
        key,
        data: { [field]: value },
        dirtyFields: [field],
      })
    }
  }

  private removePendingChanges(entitySet: string, key: string): void {
    this.pendingChanges.value = this.pendingChanges.value.filter(
      (c: PendingChange) => !(c.entitySet === entitySet && c.key === key)
    )
  }

  private async submitBatch(changes: PendingChange[]): Promise<void> {
    const requests = changes.map((change, index) => {
      const id = `change-${index}`
      switch (change.type) {
        case 'create':
          return {
            id,
            method: 'POST' as const,
            url: change.entitySet,
            body: change.data,
            headers: { 'Content-Type': 'application/json' },
          }
        case 'update':
          return {
            id,
            method: 'PATCH' as const,
            url: `${change.entitySet}/${change.key}`,
            body: change.data,
            headers: { 'Content-Type': 'application/json' },
          }
        case 'delete':
          return {
            id,
            method: 'DELETE' as const,
            url: `${change.entitySet}/${change.key}`,
          }
      }
    })

    const responses = await odataService.batch(this.module, requests)

    // Process responses
    for (let i = 0; i < responses.length; i++) {
      const response = responses[i]
      const change = changes[i]
      if (response.status >= 200 && response.status < 300) {
        if (change.key) {
          const storeKey = `${change.entitySet}/${change.key}`
          this.changeTracker.clearDirty(storeKey)
          if (response.body) {
            this.storeEntity(change.entitySet, response.body as Record<string, unknown>)
            this.changeTracker.snapshot(storeKey, response.body as Record<string, unknown>)
          }
        }
      } else {
        throw new Error(
          `Batch operation ${i} failed with status ${response.status}: ${JSON.stringify(response.body)}`
        )
      }
    }
  }

  private async submitSequential(changes: PendingChange[]): Promise<void> {
    for (const change of changes) {
      switch (change.type) {
        case 'create':
          await this.create(change.entitySet, change.data ?? {})
          break
        case 'update':
          if (change.key) {
            await this.update(change.entitySet, change.key, change.data)
          }
          break
        case 'delete':
          if (change.key) {
            await this.remove(change.entitySet, change.key)
          }
          break
      }
    }
  }

  private extractEntitySet(path: string): string {
    // '/Customers' → 'Customers'
    // '/Customers(123)' → 'Customers'
    const clean = path.startsWith('/') ? path.slice(1) : path
    const parenIndex = clean.indexOf('(')
    return parenIndex > 0 ? clean.slice(0, parenIndex) : clean
  }

  private parsePath(path: string): { entitySet: string; key?: string; property?: string } {
    const clean = path.startsWith('/') ? path.slice(1) : path
    // Pattern: EntitySet(key)/property
    const match = clean.match(/^(\w+)\(([^)]+)\)\/(\w+)$/)
    if (match) {
      return { entitySet: match[1], key: match[2], property: match[3] }
    }
    // Pattern: EntitySet(key)
    const match2 = clean.match(/^(\w+)\(([^)]+)\)$/)
    if (match2) {
      return { entitySet: match2[1], key: match2[2] }
    }
    // Pattern: EntitySet
    return { entitySet: clean }
  }

  /**
   * Destroy the model and release all resources.
   */
  destroy(): void {
    for (const binding of this.activeBindings) {
      binding.destroy()
    }
    this.activeBindings.clear()
    this.entityStore.clear()
    this.changeTracker.clear()
    this.entityCache.clear()
    this.pendingChanges.value = []
  }
}
