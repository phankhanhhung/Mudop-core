import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { QueryCache } from '../queryCache'

describe('QueryCache', () => {
  let cache: QueryCache<string>

  beforeEach(() => {
    vi.useFakeTimers()
    cache = new QueryCache<string>(1000) // 1 second TTL
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('get/set basics', () => {
    it('returns undefined for missing keys', () => {
      expect(cache.get('missing')).toBeUndefined()
    })

    it('returns the stored value for existing keys', () => {
      cache.set('key1', 'value1')
      expect(cache.get('key1')).toBe('value1')
    })

    it('overwrites existing values', () => {
      cache.set('key1', 'value1')
      cache.set('key1', 'value2')
      expect(cache.get('key1')).toBe('value2')
    })

    it('stores multiple independent entries', () => {
      cache.set('key1', 'value1')
      cache.set('key2', 'value2')
      expect(cache.get('key1')).toBe('value1')
      expect(cache.get('key2')).toBe('value2')
    })
  })

  describe('TTL expiry', () => {
    it('returns the value within the TTL window', () => {
      cache.set('key1', 'value1')
      vi.advanceTimersByTime(999)
      expect(cache.get('key1')).toBe('value1')
    })

    it('returns undefined after TTL expires', () => {
      cache.set('key1', 'value1')
      vi.advanceTimersByTime(1001)
      expect(cache.get('key1')).toBeUndefined()
    })

    it('uses default TTL of 30 seconds when not specified', () => {
      const defaultCache = new QueryCache<string>()
      defaultCache.set('key1', 'value1')

      vi.advanceTimersByTime(29_999)
      expect(defaultCache.get('key1')).toBe('value1')

      vi.advanceTimersByTime(2)
      expect(defaultCache.get('key1')).toBeUndefined()
    })

    it('resets TTL when value is overwritten', () => {
      cache.set('key1', 'value1')
      vi.advanceTimersByTime(800)

      // Overwrite refreshes the timestamp
      cache.set('key1', 'value2')
      vi.advanceTimersByTime(800)

      // 800ms after second set — still within TTL
      expect(cache.get('key1')).toBe('value2')
    })
  })

  describe('invalidate', () => {
    it('clears all entries when called without a prefix', () => {
      cache.set('users:1', 'alice')
      cache.set('users:2', 'bob')
      cache.set('orders:1', 'order1')

      cache.invalidate()

      expect(cache.get('users:1')).toBeUndefined()
      expect(cache.get('users:2')).toBeUndefined()
      expect(cache.get('orders:1')).toBeUndefined()
    })

    it('clears only entries matching the prefix', () => {
      cache.set('users:1', 'alice')
      cache.set('users:2', 'bob')
      cache.set('orders:1', 'order1')

      cache.invalidate('users:')

      expect(cache.get('users:1')).toBeUndefined()
      expect(cache.get('users:2')).toBeUndefined()
      expect(cache.get('orders:1')).toBe('order1')
    })

    it('does nothing if no keys match the prefix', () => {
      cache.set('users:1', 'alice')
      cache.invalidate('orders:')
      expect(cache.get('users:1')).toBe('alice')
    })
  })

  describe('clear', () => {
    it('removes all entries', () => {
      cache.set('key1', 'value1')
      cache.set('key2', 'value2')

      cache.clear()

      expect(cache.get('key1')).toBeUndefined()
      expect(cache.get('key2')).toBeUndefined()
    })
  })

  describe('max entry eviction', () => {
    it('evicts oldest entries when exceeding 50 entries', () => {
      // Insert 51 entries
      for (let i = 0; i < 51; i++) {
        // Advance time so entries have different timestamps
        vi.advanceTimersByTime(1)
        cache.set(`key${i}`, `value${i}`)
      }

      // The oldest entry (key0) should have been evicted
      expect(cache.get('key0')).toBeUndefined()

      // The newest entries should still be present
      expect(cache.get('key50')).toBe('value50')
      expect(cache.get('key1')).toBe('value1')
    })

    it('keeps exactly 50 entries after eviction', () => {
      for (let i = 0; i < 55; i++) {
        vi.advanceTimersByTime(1)
        cache.set(`key${i}`, `value${i}`)
      }

      // Count how many of the 55 keys are still present
      let count = 0
      for (let i = 0; i < 55; i++) {
        if (cache.get(`key${i}`) !== undefined) {
          count++
        }
      }
      expect(count).toBe(50)
    })
  })
})
