/**
 * Simple TTL-based cache for OData query results.
 * Prevents redundant API calls when navigating back to previous pages
 * or re-visiting the same list view.
 */

interface CacheEntry<T> {
  data: T
  timestamp: number
}

const DEFAULT_TTL = 30_000 // 30 seconds

export class QueryCache<T = unknown> {
  private cache = new Map<string, CacheEntry<T>>()
  private ttl: number

  constructor(ttlMs: number = DEFAULT_TTL) {
    this.ttl = ttlMs
  }

  get(key: string): T | undefined {
    const entry = this.cache.get(key)
    if (!entry) return undefined

    // Check if expired
    if (Date.now() - entry.timestamp > this.ttl) {
      this.cache.delete(key)
      return undefined
    }

    return entry.data
  }

  set(key: string, data: T): void {
    this.cache.set(key, { data, timestamp: Date.now() })

    // Evict old entries if cache gets too large (max 50 entries)
    if (this.cache.size > 50) {
      const oldest = [...this.cache.entries()]
        .sort((a, b) => a[1].timestamp - b[1].timestamp)
        .slice(0, this.cache.size - 50)
      for (const [k] of oldest) {
        this.cache.delete(k)
      }
    }
  }

  invalidate(keyPrefix?: string): void {
    if (!keyPrefix) {
      this.cache.clear()
      return
    }
    for (const key of this.cache.keys()) {
      if (key.startsWith(keyPrefix)) {
        this.cache.delete(key)
      }
    }
  }

  clear(): void {
    this.cache.clear()
  }
}
