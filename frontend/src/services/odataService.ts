import api from './api'
import type { ODataQueryOptions, ODataResponse, BatchRequest, BatchResponse, WriteOptions } from '@/types/odata'
import { dedup } from '@/utils/requestDedup'
import { QueryCache } from '@/utils/queryCache'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
/** Shorthand for encoding a single URL path segment. */
const enc = encodeURIComponent

// ---------------------------------------------------------------------------
// ETag Store — lightweight module-level Map for optimistic concurrency
// ---------------------------------------------------------------------------
const etagMap = new Map<string, string>()

// ---------------------------------------------------------------------------
// Query result cache — 30s TTL for list queries
// ---------------------------------------------------------------------------
const queryResultCache = new QueryCache<ODataResponse<unknown>>(30_000)

const ETAG_MAP_MAX_SIZE = 1000

export const etagStore = {
  get(key: string): string | undefined {
    return etagMap.get(key)
  },
  set(key: string, etag: string): void {
    // LRU-style eviction: drop the oldest entry when map exceeds max size
    if (etagMap.size >= ETAG_MAP_MAX_SIZE && !etagMap.has(key)) {
      const oldestKey = etagMap.keys().next().value
      if (oldestKey !== undefined) {
        etagMap.delete(oldestKey)
      }
    }
    etagMap.set(key, etag)
  },
  remove(key: string): void {
    etagMap.delete(key)
  },
  clear(): void {
    etagMap.clear()
  }
}

function etagKey(module: string, entitySet: string, id: string): string {
  return `${module}/${entitySet}/${id}`
}

function buildQueryString(options: ODataQueryOptions): string {
  const params = new URLSearchParams()

  if (options.$filter) params.append('$filter', options.$filter)
  if (options.$select) params.append('$select', options.$select)
  if (options.$expand) params.append('$expand', options.$expand)
  if (options.$orderby) params.append('$orderby', options.$orderby)
  if (options.$top !== undefined) params.append('$top', options.$top.toString())
  if (options.$skip !== undefined) params.append('$skip', options.$skip.toString())
  if (options.$count) params.append('$count', 'true')
  if (options.$search) params.append('$search', options.$search)
  if (options.$apply) params.append('$apply', options.$apply)
  if (options.$compute) params.append('$compute', options.$compute)
  if (options.$deltatoken) params.append('$deltatoken', options.$deltatoken)
  if (options.asOf) params.append('asOf', options.asOf)
  if (options.validAt) params.append('validAt', options.validAt)
  if (options.includeHistory) params.append('includeHistory', 'true')

  const queryString = params.toString()
  return queryString ? `?${queryString}` : ''
}

export const odataService = {
  /**
   * Query entities with OData options.
   * Uses request deduplication for concurrent identical queries
   * and a short-lived TTL cache for recently fetched results.
   */
  async query<T>(
    module: string,
    entitySet: string,
    options: ODataQueryOptions = {},
    requestOptions?: { signal?: AbortSignal; skipCache?: boolean }
  ): Promise<ODataResponse<T>> {
    const queryString = buildQueryString(options)
    const url = `/odata/${enc(module)}/${enc(entitySet)}${queryString}`
    const cacheKey = `query:${url}`

    // Check cache first (unless explicitly skipped)
    if (!requestOptions?.skipCache) {
      const cached = queryResultCache.get(cacheKey) as ODataResponse<T> | undefined
      if (cached) return cached
    }

    // Deduplicate concurrent identical requests
    const result = await dedup(cacheKey, async (dedupSignal) => {
      const signal = requestOptions?.signal
        ? requestOptions.signal
        : dedupSignal
      const response = await api.get<ODataResponse<T>>(url, { signal })
      return response.data
    })

    // Capture per-item ETags from @odata.etag annotation
    const items = result.value
    if (Array.isArray(items)) {
      for (const item of items) {
        const rec = item as Record<string, unknown>
        const itemEtag = rec['@odata.etag'] as string | undefined
        const itemId = rec['Id'] ?? rec['ID'] ?? rec['id']
        if (itemEtag && itemId) {
          etagStore.set(etagKey(module, entitySet, String(itemId)), itemEtag)
        }
      }
    }

    // Cache the result
    queryResultCache.set(cacheKey, result as ODataResponse<unknown>)

    return result
  },

  /**
   * Get a single entity by ID.
   * Uses request deduplication for concurrent identical fetches.
   */
  async getById<T>(
    module: string,
    entitySet: string,
    id: string,
    options: Pick<ODataQueryOptions, '$select' | '$expand'> = {},
    requestOptions?: { signal?: AbortSignal }
  ): Promise<T> {
    const params = new URLSearchParams()
    if (options.$select) params.append('$select', options.$select)
    if (options.$expand) params.append('$expand', options.$expand)
    const queryString = params.toString() ? `?${params.toString()}` : ''

    const url = `/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}${queryString}`
    const dedupKey = `getById:${url}`

    const result = await dedup(dedupKey, async (dedupSignal) => {
      const signal = requestOptions?.signal ?? dedupSignal
      const response = await api.get<T>(url, { signal })

      // Capture ETag from response header
      const etag = response.headers['etag'] as string | undefined
      if (etag) {
        etagStore.set(etagKey(module, entitySet, id), etag)
      }

      return response.data
    })

    return result
  },

  /**
   * Get a singleton entity (no key segment in URL).
   */
  async getSingleton<T>(
    module: string,
    entitySet: string,
    options: Pick<ODataQueryOptions, '$select' | '$expand'> = {}
  ): Promise<T> {
    const params = new URLSearchParams()
    if (options.$select) params.append('$select', options.$select)
    if (options.$expand) params.append('$expand', options.$expand)
    const queryString = params.toString() ? `?${params.toString()}` : ''

    const url = `/odata/${enc(module)}/${enc(entitySet)}${queryString}`
    const response = await api.get<T>(url)

    // Capture ETag from response header
    const etag = response.headers['etag'] as string | undefined
    if (etag) {
      etagStore.set(etagKey(module, entitySet, '_singleton'), etag)
    }

    return response.data
  },

  /**
   * Update a singleton entity (PATCH, no key segment).
   */
  async updateSingleton<T>(
    module: string,
    entitySet: string,
    data: Partial<T>,
    options?: WriteOptions
  ): Promise<T> {
    const headers: Record<string, string> = {}
    const etag = options?.ifMatch ?? etagStore.get(etagKey(module, entitySet, '_singleton'))
    if (etag) {
      headers['If-Match'] = etag
    }
    if (options?.prefer) {
      headers['Prefer'] = options.prefer
    }

    const response = await api.patch<T>(`/odata/${enc(module)}/${enc(entitySet)}`, data, { headers })

    // Update stored ETag from response
    const newEtag = response.headers['etag'] as string | undefined
    if (newEtag) {
      etagStore.set(etagKey(module, entitySet, '_singleton'), newEtag)
    }

    // Invalidate cached queries for this entity set
    queryResultCache.invalidate(`query:/odata/${enc(module)}/${enc(entitySet)}`)

    return response.data
  },

  /**
   * Create a new entity. Invalidates query cache for this entity set.
   */
  async create<T>(module: string, entitySet: string, data: Partial<T>, options?: WriteOptions): Promise<T> {
    const headers: Record<string, string> = {}
    if (options?.prefer) {
      headers['Prefer'] = options.prefer
    }
    const response = await api.post<T>(`/odata/${enc(module)}/${enc(entitySet)}`, data, { headers })
    // Invalidate cached queries for this entity set
    queryResultCache.invalidate(`query:/odata/${enc(module)}/${enc(entitySet)}`)
    return response.data
  },

  /**
   * Update an existing entity (PATCH)
   */
  async update<T>(
    module: string,
    entitySet: string,
    id: string,
    data: Partial<T>,
    options?: WriteOptions
  ): Promise<T> {
    const headers: Record<string, string> = {}
    const etag = options?.ifMatch ?? etagStore.get(etagKey(module, entitySet, id))
    if (etag) {
      headers['If-Match'] = etag
    }
    if (options?.prefer) {
      headers['Prefer'] = options.prefer
    }

    const response = await api.patch<T>(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}`, data, { headers })

    // Update stored ETag from response
    const newEtag = response.headers['etag'] as string | undefined
    if (newEtag) {
      etagStore.set(etagKey(module, entitySet, id), newEtag)
    }

    // Invalidate cached queries for this entity set
    queryResultCache.invalidate(`query:/odata/${enc(module)}/${enc(entitySet)}`)

    return response.data
  },

  /**
   * Replace an entity (PUT)
   */
  async replace<T>(
    module: string,
    entitySet: string,
    id: string,
    data: T,
    options?: WriteOptions
  ): Promise<T> {
    const headers: Record<string, string> = {}
    const etag = options?.ifMatch ?? etagStore.get(etagKey(module, entitySet, id))
    if (etag) {
      headers['If-Match'] = etag
    }
    if (options?.prefer) {
      headers['Prefer'] = options.prefer
    }

    const response = await api.put<T>(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}`, data, { headers })

    // Update stored ETag from response
    const newEtag = response.headers['etag'] as string | undefined
    if (newEtag) {
      etagStore.set(etagKey(module, entitySet, id), newEtag)
    }

    return response.data
  },

  /**
   * Delete an entity
   */
  async delete(module: string, entitySet: string, id: string, options?: { ifMatch?: string }): Promise<void> {
    const headers: Record<string, string> = {}
    const etag = options?.ifMatch ?? etagStore.get(etagKey(module, entitySet, id))
    if (etag) {
      headers['If-Match'] = etag
    }

    await api.delete(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}`, { headers })

    // Remove stored ETag after successful deletion
    etagStore.remove(etagKey(module, entitySet, id))
    // Invalidate cached queries for this entity set
    queryResultCache.invalidate(`query:/odata/${enc(module)}/${enc(entitySet)}`)
  },

  /**
   * Execute a bound action
   */
  async executeAction<TParams, TResult>(
    module: string,
    entitySet: string,
    id: string,
    actionName: string,
    params?: TParams
  ): Promise<TResult> {
    const response = await api.post<TResult>(
      `/odata/${enc(module)}/${enc(entitySet)}/${enc(String(id))}/${enc(actionName)}`,
      params
    )
    return response.data
  },

  /**
   * Execute an unbound action via the service endpoint
   */
  async executeUnboundAction<TParams, TResult>(
    serviceName: string,
    actionName: string,
    params?: TParams
  ): Promise<TResult> {
    const response = await api.post<TResult>(
      `/odata/services/${enc(serviceName)}/${enc(actionName)}`,
      params
    )
    return response.data
  },

  /**
   * Call an unbound function via the service endpoint
   */
  async callFunction<TResult>(
    serviceName: string,
    functionName: string,
    params?: Record<string, unknown>
  ): Promise<TResult> {
    const queryParams = params
      ? '?' +
        Object.entries(params)
          .map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)
          .join('&')
      : ''
    const response = await api.get<TResult>(
      `/odata/services/${enc(serviceName)}/${enc(functionName)}()${queryParams}`
    )
    return response.data
  },

  /**
   * Execute batch requests
   */
  async batch(module: string, requests: BatchRequest[]): Promise<BatchResponse[]> {
    const response = await api.post<{ responses: BatchResponse[] }>(
      `/odata/${enc(module)}/$batch`,
      { requests }
    )
    return response.data.responses
  },

  /**
   * Get child entities via navigation property (containment navigation)
   */
  async getChildren<T>(
    module: string,
    entitySet: string,
    parentId: string,
    navProperty: string,
    options: ODataQueryOptions = {}
  ): Promise<ODataResponse<T>> {
    const queryString = buildQueryString(options)
    const response = await api.get<ODataResponse<T>>(
      `/odata/${enc(module)}/${enc(entitySet)}/${enc(parentId)}/${enc(navProperty)}${queryString}`
    )
    return response.data
  },

  /**
   * Get metadata for a module
   */
  async getMetadata(module: string): Promise<string> {
    const response = await api.get<string>(`/odata/${enc(module)}/$metadata`, {
      headers: { Accept: 'application/xml' }
    })
    return response.data
  },

  /**
   * Download entity media stream (GET $value for HasStream entities)
   */
  async getMediaStream(
    module: string,
    entitySet: string,
    id: string
  ): Promise<{ blob: Blob; contentType: string }> {
    const response = await api.get(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/$value`, {
      responseType: 'blob'
    })
    const contentType = response.headers['content-type'] || 'application/octet-stream'
    return { blob: response.data, contentType }
  },

  /**
   * Upload/replace entity media stream (PUT $value for HasStream entities)
   */
  async uploadMediaStream(
    module: string,
    entitySet: string,
    id: string,
    file: File
  ): Promise<void> {
    const response = await api.put(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/$value`, file, {
      headers: { 'Content-Type': file.type },
    })
    const etag = response.headers['etag'] as string | undefined
    if (etag) {
      etagStore.set(etagKey(module, entitySet, id), etag)
    }
  },

  /**
   * Delete entity media stream (DELETE $value for HasStream entities)
   */
  async deleteMediaStream(
    module: string,
    entitySet: string,
    id: string
  ): Promise<void> {
    const response = await api.delete(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/$value`)
    const etag = response.headers['etag'] as string | undefined
    if (etag) {
      etagStore.set(etagKey(module, entitySet, id), etag)
    }
  },

  /**
   * Query with delta tracking — sends Prefer: odata.track-changes header,
   * returns full ODataResponse including @odata.deltaLink
   */
  async queryDelta<T>(
    module: string,
    entitySet: string,
    options: ODataQueryOptions = {}
  ): Promise<ODataResponse<T>> {
    const queryString = buildQueryString(options)
    const headers: Record<string, string> = {}
    if (options.trackChanges) {
      headers['Prefer'] = 'odata.track-changes'
    }
    const response = await api.get<ODataResponse<T>>(
      `/odata/${enc(module)}/${enc(entitySet)}${queryString}`,
      { headers }
    )
    return response.data
  },

  /**
   * Get version history for a temporal entity
   */
  async getVersions<T = Record<string, unknown>>(
    module: string,
    entitySet: string,
    id: string | number
  ): Promise<T[]> {
    const response = await api.get<{ value: T[] }>(
      `/odata/${enc(module)}/${enc(entitySet)}/${enc(String(id))}/versions`
    )
    return response.data.value
  },

  /**
   * Get count of entities using standalone $count endpoint (returns text/plain integer)
   */
  async count(
    module: string,
    entitySet: string,
    filter?: string
  ): Promise<number> {
    const params = new URLSearchParams()
    if (filter) params.set('$filter', filter)
    const query = params.toString() ? `?${params.toString()}` : ''
    const response = await api.get(`/odata/${enc(module)}/${enc(entitySet)}/$count${query}`, {
      headers: { Accept: 'text/plain' }
    })
    return parseInt(String(response.data), 10)
  }
}

/**
 * Clears all module-level OData caches (etag store + query result cache).
 * Should be called on logout to prevent stale data leaking across sessions.
 */
export function clearODataCaches(): void {
  etagMap.clear()
  queryResultCache.clear()
}

export default odataService
