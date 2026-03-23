/**
 * Advanced Entity Cache — entity-level caching with tag-based invalidation.
 *
 * Improvements over the existing QueryCache:
 * - Entity-level cache (cache per entity instance by key, not just per query)
 * - Tag-based invalidation (invalidate by entity type, not just URL prefix)
 * - Stale-while-revalidate: return cached data + background refresh
 * - Size-aware LRU eviction
 * - Cache statistics for monitoring
 *
 * Usage:
 *   const cache = new EntityCache({ ttl: 60000, maxEntries: 200 })
 *
 *   // Store
 *   cache.set('Customers/123', data, { tags: ['Customers'] })
 *
 *   // Get (with SWR)
 *   const result = cache.get('Customers/123')
 *   if (result.stale) { // Background refresh recommended }
 *
 *   // Invalidate by tag
 *   cache.invalidateByTag('Customers') // All customer entries
 */

import type { CacheOptions, CacheEntry } from './types'

export interface CacheGetResult<T> {
  /** The cached data, or undefined if not found */
  data: T | undefined
  /** Whether the entry was found in cache */
  hit: boolean
  /** Whether the data is stale (past TTL but within SWR window) */
  stale: boolean
  /** Cache entry age in ms */
  age: number
}

export interface CacheSetOptions {
  /** Tags for this entry (used for tag-based invalidation) */
  tags?: string[]
  /** ETag for this entry */
  etag?: string
  /** Custom TTL override for this entry */
  ttl?: number
}

export interface CacheStats {
  /** Total entries in cache */
  entries: number
  /** Cache hits since creation */
  hits: number
  /** Cache misses since creation */
  misses: number
  /** Hit ratio (0-1) */
  hitRatio: number
  /** Estimated memory usage in bytes */
  estimatedSize: number
  /** Number of stale-while-revalidate serves */
  swrServes: number
}

export class EntityCache {
  private cache = new Map<string, CacheEntry>()
  private config: CacheOptions
  private stats = { hits: 0, misses: 0, swrServes: 0 }

  /** Tag → set of cache keys */
  private tagIndex = new Map<string, Set<string>>()

  /** LRU order (most recently accessed at end) */
  private lruOrder: string[] = []

  constructor(config?: Partial<CacheOptions>) {
    this.config = {
      ttl: config?.ttl ?? 60_000,
      maxEntries: config?.maxEntries ?? 200,
      staleWhileRevalidate: config?.staleWhileRevalidate ?? true,
      swrWindow: config?.swrWindow ?? 10_000,
    }
  }

  /**
   * Get an entry from cache.
   * Returns cached data with freshness metadata.
   */
  get<T = unknown>(key: string): CacheGetResult<T> {
    const entry = this.cache.get(key)

    if (!entry) {
      this.stats.misses++
      return { data: undefined, hit: false, stale: false, age: 0 }
    }

    const age = Date.now() - entry.timestamp
    const isFresh = age <= (this.config.ttl)
    const isWithinSwr = age <= (this.config.ttl + this.config.swrWindow)

    // Expired beyond SWR window
    if (!isWithinSwr) {
      this.remove(key)
      this.stats.misses++
      return { data: undefined, hit: false, stale: false, age }
    }

    // Update LRU position
    this.touchLru(key)

    if (isFresh) {
      this.stats.hits++
      return { data: entry.data as T, hit: true, stale: false, age }
    }

    // Stale but within SWR window
    if (this.config.staleWhileRevalidate) {
      this.stats.swrServes++
      return { data: entry.data as T, hit: true, stale: true, age }
    }

    // SWR disabled, treat expired as miss
    this.remove(key)
    this.stats.misses++
    return { data: undefined, hit: false, stale: false, age }
  }

  /**
   * Store an entry in cache.
   */
  set<T = unknown>(key: string, data: T, options?: CacheSetOptions): void {
    // Evict if needed
    this.evictIfNeeded()

    const size = this.estimateSize(data)
    const entry: CacheEntry = {
      data,
      timestamp: Date.now(),
      etag: options?.etag,
      tags: options?.tags ?? [],
      size,
    }

    this.cache.set(key, entry)
    this.touchLru(key)

    // Update tag index
    for (const tag of entry.tags) {
      if (!this.tagIndex.has(tag)) {
        this.tagIndex.set(tag, new Set())
      }
      this.tagIndex.get(tag)!.add(key)
    }
  }

  /**
   * Check if a key exists and is fresh.
   */
  has(key: string): boolean {
    const result = this.get(key)
    return result.hit && !result.stale
  }

  /**
   * Remove a specific entry.
   */
  remove(key: string): void {
    const entry = this.cache.get(key)
    if (!entry) return

    // Remove from tag index
    for (const tag of entry.tags) {
      this.tagIndex.get(tag)?.delete(key)
    }

    this.cache.delete(key)
    this.lruOrder = this.lruOrder.filter(k => k !== key)
  }

  /**
   * Invalidate all entries with a specific tag.
   */
  invalidateByTag(tag: string): void {
    const keys = this.tagIndex.get(tag)
    if (!keys) return

    for (const key of keys) {
      this.cache.delete(key)
      this.lruOrder = this.lruOrder.filter(k => k !== key)
    }

    this.tagIndex.delete(tag)
  }

  /**
   * Invalidate entries matching a key prefix.
   */
  invalidateByPrefix(prefix: string): void {
    for (const key of [...this.cache.keys()]) {
      if (key.startsWith(prefix)) {
        this.remove(key)
      }
    }
  }

  /**
   * Invalidate all entries with matching tags.
   */
  invalidateByTags(tags: string[]): void {
    for (const tag of tags) {
      this.invalidateByTag(tag)
    }
  }

  /**
   * Clear entire cache.
   */
  clear(): void {
    this.cache.clear()
    this.tagIndex.clear()
    this.lruOrder = []
  }

  /**
   * Get cache statistics.
   */
  getStats(): CacheStats {
    let estimatedSize = 0
    for (const [, entry] of this.cache) {
      estimatedSize += entry.size
    }

    const total = this.stats.hits + this.stats.misses
    return {
      entries: this.cache.size,
      hits: this.stats.hits,
      misses: this.stats.misses,
      hitRatio: total > 0 ? this.stats.hits / total : 0,
      estimatedSize,
      swrServes: this.stats.swrServes,
    }
  }

  /**
   * Reset statistics counters.
   */
  resetStats(): void {
    this.stats = { hits: 0, misses: 0, swrServes: 0 }
  }

  /**
   * Get all tags currently in the cache.
   */
  getTags(): string[] {
    return [...this.tagIndex.keys()]
  }

  /**
   * Get all cache keys.
   */
  getKeys(): string[] {
    return [...this.cache.keys()]
  }

  // =========================================================================
  // Private
  // =========================================================================

  private evictIfNeeded(): void {
    while (this.cache.size >= this.config.maxEntries && this.lruOrder.length > 0) {
      const oldest = this.lruOrder.shift()
      if (oldest) {
        this.remove(oldest)
      }
    }
  }

  private touchLru(key: string): void {
    const index = this.lruOrder.indexOf(key)
    if (index >= 0) {
      this.lruOrder.splice(index, 1)
    }
    this.lruOrder.push(key)
  }

  private estimateSize(data: unknown): number {
    try {
      return JSON.stringify(data).length * 2 // Rough bytes estimate
    } catch {
      return 1024 // Default 1KB for non-serializable
    }
  }
}
