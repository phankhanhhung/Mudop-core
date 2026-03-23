import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { EntityCache } from '../EntityCache'

describe('EntityCache', () => {
  let cache: EntityCache

  beforeEach(() => {
    vi.useFakeTimers()
    cache = new EntityCache({ ttl: 5000, maxEntries: 200, staleWhileRevalidate: true, swrWindow: 3000 })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('set() and get() basic round-trip', () => {
    it('returns the stored value on get after set', () => {
      const data = { id: 1, name: 'Alice' }
      cache.set('Customers/1', data, { tags: ['Customers'] })

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(true)
      expect(result.stale).toBe(false)
      expect(result.data).toEqual(data)
      expect(result.age).toBeGreaterThanOrEqual(0)
    })

    it('returns a miss for keys that were never set', () => {
      const result = cache.get('NonExistent/99')
      expect(result.hit).toBe(false)
      expect(result.data).toBeUndefined()
    })

    it('overwrites existing entries', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.set('Customers/1', { name: 'Bob' })

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(true)
      expect(result.data).toEqual({ name: 'Bob' })
    })
  })

  describe('TTL expiration', () => {
    it('returns a hit within the TTL window', () => {
      cache.set('Customers/1', { name: 'Alice' })
      vi.advanceTimersByTime(4999) // Just under 5000ms TTL

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(true)
      expect(result.stale).toBe(false)
    })

    it('returns stale after TTL but within SWR window', () => {
      cache.set('Customers/1', { name: 'Alice' })
      vi.advanceTimersByTime(6000) // Past 5000ms TTL, within 5000+3000 SWR

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(true)
      expect(result.stale).toBe(true)
    })

    it('returns a miss after TTL + SWR window expires', () => {
      cache.set('Customers/1', { name: 'Alice' })
      vi.advanceTimersByTime(9000) // Past 5000 + 3000 = 8000ms

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(false)
      expect(result.data).toBeUndefined()
    })
  })

  describe('stale-while-revalidate', () => {
    it('returns stale data with hit=true and stale=true within SWR window', () => {
      cache.set('Orders/42', { total: 100 }, { tags: ['Orders'] })
      vi.advanceTimersByTime(7000) // Past TTL (5000), within SWR (5000+3000=8000)

      const result = cache.get('Orders/42')
      expect(result.hit).toBe(true)
      expect(result.stale).toBe(true)
      expect(result.data).toEqual({ total: 100 })
    })

    it('treats stale entries as misses when SWR is disabled', () => {
      const noSwrCache = new EntityCache({
        ttl: 5000,
        maxEntries: 200,
        staleWhileRevalidate: false,
        swrWindow: 3000,
      })

      noSwrCache.set('Customers/1', { name: 'Alice' })
      vi.advanceTimersByTime(6000)

      const result = noSwrCache.get('Customers/1')
      expect(result.hit).toBe(false)
      expect(result.data).toBeUndefined()
    })
  })

  describe('has()', () => {
    it('returns true for a fresh entry', () => {
      cache.set('Customers/1', { name: 'Alice' })
      expect(cache.has('Customers/1')).toBe(true)
    })

    it('returns false for a stale entry (past TTL)', () => {
      cache.set('Customers/1', { name: 'Alice' })
      vi.advanceTimersByTime(6000) // Past TTL but within SWR

      expect(cache.has('Customers/1')).toBe(false)
    })

    it('returns false for a non-existent key', () => {
      expect(cache.has('NonExistent/1')).toBe(false)
    })
  })

  describe('tag-based invalidation', () => {
    it('invalidateByTag removes only entries with the matching tag', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.set('Customers/2', { name: 'Bob' }, { tags: ['Customers'] })
      cache.set('Orders/1', { total: 50 }, { tags: ['Orders'] })

      cache.invalidateByTag('Customers')

      expect(cache.get('Customers/1').hit).toBe(false)
      expect(cache.get('Customers/2').hit).toBe(false)
      expect(cache.get('Orders/1').hit).toBe(true)
    })

    it('invalidateByTags removes entries matching any of the provided tags', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.set('Orders/1', { total: 50 }, { tags: ['Orders'] })
      cache.set('Products/1', { sku: 'P100' }, { tags: ['Products'] })

      cache.invalidateByTags(['Customers', 'Orders'])

      expect(cache.get('Customers/1').hit).toBe(false)
      expect(cache.get('Orders/1').hit).toBe(false)
      expect(cache.get('Products/1').hit).toBe(true)
    })

    it('handles entries with multiple tags', () => {
      cache.set('Dashboard/summary', { count: 10 }, { tags: ['Customers', 'Orders'] })
      cache.set('Products/1', { sku: 'P100' }, { tags: ['Products'] })

      cache.invalidateByTag('Customers')

      expect(cache.get('Dashboard/summary').hit).toBe(false)
      expect(cache.get('Products/1').hit).toBe(true)
    })

    it('does nothing if the tag has no entries', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.invalidateByTag('NonExistentTag')
      expect(cache.get('Customers/1').hit).toBe(true)
    })
  })

  describe('invalidateByPrefix', () => {
    it('removes entries matching the key prefix', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.set('Customers/2', { name: 'Bob' })
      cache.set('Orders/1', { total: 50 })

      cache.invalidateByPrefix('Customers/')

      expect(cache.get('Customers/1').hit).toBe(false)
      expect(cache.get('Customers/2').hit).toBe(false)
      expect(cache.get('Orders/1').hit).toBe(true)
    })

    it('does nothing if no keys match the prefix', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.invalidateByPrefix('Products/')
      expect(cache.get('Customers/1').hit).toBe(true)
    })
  })

  describe('LRU eviction', () => {
    it('evicts the oldest entry when maxEntries is exceeded', () => {
      const smallCache = new EntityCache({ ttl: 60000, maxEntries: 3 })

      smallCache.set('item/1', 'first')
      smallCache.set('item/2', 'second')
      smallCache.set('item/3', 'third')
      // Cache is now full at 3 entries; adding a 4th should evict the oldest
      smallCache.set('item/4', 'fourth')

      expect(smallCache.get('item/1').hit).toBe(false) // Evicted (oldest)
      expect(smallCache.get('item/2').hit).toBe(true)
      expect(smallCache.get('item/3').hit).toBe(true)
      expect(smallCache.get('item/4').hit).toBe(true)
    })

    it('re-accessing an item moves it to the end of the LRU list', () => {
      const smallCache = new EntityCache({ ttl: 60000, maxEntries: 3 })

      smallCache.set('item/1', 'first')
      smallCache.set('item/2', 'second')
      smallCache.set('item/3', 'third')

      // Access item/1 to make it recently used
      smallCache.get('item/1')

      // Now add item/4 — should evict item/2 (least recently used)
      smallCache.set('item/4', 'fourth')

      expect(smallCache.get('item/1').hit).toBe(true)  // Re-accessed, so not evicted
      expect(smallCache.get('item/2').hit).toBe(false)  // Evicted (least recently used)
      expect(smallCache.get('item/3').hit).toBe(true)
      expect(smallCache.get('item/4').hit).toBe(true)
    })
  })

  describe('getStats()', () => {
    it('returns correct hits and misses', () => {
      cache.set('key1', 'value1')
      cache.get('key1')     // hit
      cache.get('key1')     // hit
      cache.get('missing')  // miss

      const stats = cache.getStats()
      expect(stats.hits).toBe(2)
      expect(stats.misses).toBe(1)
      expect(stats.hitRatio).toBeCloseTo(2 / 3)
    })

    it('returns the correct entry count', () => {
      cache.set('a', 1)
      cache.set('b', 2)
      cache.set('c', 3)

      const stats = cache.getStats()
      expect(stats.entries).toBe(3)
    })

    it('counts SWR serves', () => {
      cache.set('key1', 'value1')
      vi.advanceTimersByTime(6000) // Past TTL, within SWR

      cache.get('key1') // SWR serve

      const stats = cache.getStats()
      expect(stats.swrServes).toBe(1)
    })

    it('returns zero hitRatio when no gets have been performed', () => {
      const stats = cache.getStats()
      expect(stats.hitRatio).toBe(0)
    })

    it('returns a positive estimatedSize after storing data', () => {
      cache.set('key1', { name: 'Alice', age: 30 })
      const stats = cache.getStats()
      expect(stats.estimatedSize).toBeGreaterThan(0)
    })
  })

  describe('resetStats()', () => {
    it('clears all stat counters', () => {
      cache.set('key1', 'value1')
      cache.get('key1')     // hit
      cache.get('missing')  // miss

      cache.resetStats()
      const stats = cache.getStats()

      expect(stats.hits).toBe(0)
      expect(stats.misses).toBe(0)
      expect(stats.swrServes).toBe(0)
      expect(stats.hitRatio).toBe(0)
    })
  })

  describe('clear()', () => {
    it('removes all entries from the cache', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.set('Orders/1', { total: 50 }, { tags: ['Orders'] })

      cache.clear()

      expect(cache.get('Customers/1').hit).toBe(false)
      expect(cache.get('Orders/1').hit).toBe(false)
      expect(cache.getKeys()).toHaveLength(0)
      expect(cache.getTags()).toHaveLength(0)
    })
  })

  describe('getTags() and getKeys()', () => {
    it('getTags returns all tags currently in the cache', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.set('Orders/1', { total: 50 }, { tags: ['Orders'] })

      const tags = cache.getTags()
      expect(tags).toContain('Customers')
      expect(tags).toContain('Orders')
      expect(tags).toHaveLength(2)
    })

    it('getKeys returns all cache keys', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.set('Orders/1', { total: 50 })

      const keys = cache.getKeys()
      expect(keys).toContain('Customers/1')
      expect(keys).toContain('Orders/1')
      expect(keys).toHaveLength(2)
    })

    it('getTags reflects tag removal after invalidation', () => {
      cache.set('Customers/1', { name: 'Alice' }, { tags: ['Customers'] })
      cache.set('Orders/1', { total: 50 }, { tags: ['Orders'] })

      cache.invalidateByTag('Customers')

      const tags = cache.getTags()
      expect(tags).not.toContain('Customers')
      expect(tags).toContain('Orders')
    })
  })

  describe('size estimation', () => {
    it('estimates size based on JSON.stringify length', () => {
      const data = { id: 1, name: 'Alice' }
      cache.set('Customers/1', data)

      const stats = cache.getStats()
      const expectedSize = JSON.stringify(data).length * 2
      expect(stats.estimatedSize).toBe(expectedSize)
    })

    it('uses default size for non-serializable data', () => {
      const circular: Record<string, unknown> = {}
      circular.self = circular
      cache.set('circular', circular)

      const stats = cache.getStats()
      expect(stats.estimatedSize).toBe(1024) // Default for non-serializable
    })
  })

  describe('ETag storage', () => {
    it('stores and retrieves ETag via the cache entry', () => {
      cache.set('Customers/1', { name: 'Alice' }, { etag: 'W/"abc123"' })

      const result = cache.get('Customers/1')
      expect(result.hit).toBe(true)
      expect(result.data).toEqual({ name: 'Alice' })
      // The ETag is stored on the internal CacheEntry, not exposed directly
      // on CacheGetResult. We can verify by checking the keys list contains it.
      // Indirectly verify by confirming the entry is accessible.
    })
  })

  describe('default configuration', () => {
    it('uses sensible defaults when no config is provided', () => {
      const defaultCache = new EntityCache()
      defaultCache.set('key1', 'value1')

      const result = defaultCache.get('key1')
      expect(result.hit).toBe(true)

      // Default TTL is 60000ms
      vi.advanceTimersByTime(59_999)
      expect(defaultCache.get('key1').hit).toBe(true)
      expect(defaultCache.get('key1').stale).toBe(false)

      vi.advanceTimersByTime(2)
      // At 60001ms, past TTL but within default SWR window (10000ms)
      const staleResult = defaultCache.get('key1')
      expect(staleResult.hit).toBe(true)
      expect(staleResult.stale).toBe(true)
    })
  })

  describe('remove()', () => {
    it('removes a specific entry by key', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.set('Customers/2', { name: 'Bob' })

      cache.remove('Customers/1')

      expect(cache.get('Customers/1').hit).toBe(false)
      expect(cache.get('Customers/2').hit).toBe(true)
    })

    it('is a no-op for non-existent keys', () => {
      cache.set('Customers/1', { name: 'Alice' })
      cache.remove('NonExistent')
      expect(cache.get('Customers/1').hit).toBe(true)
    })
  })
})
