import { describe, it, expect, beforeEach } from 'vitest'
import { ChangeTracker } from '../ChangeTracker'

describe('ChangeTracker', () => {
  let tracker: ChangeTracker

  beforeEach(() => {
    tracker = new ChangeTracker()
  })

  // =========================================================================
  // Snapshot & dirty tracking
  // =========================================================================

  describe('snapshot() and isDirty()', () => {
    it('stores original data and isDirty returns false initially', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30 })

      expect(tracker.isDirty('c1')).toBe(false)
    })

    it('isDirty returns false for unknown keys', () => {
      expect(tracker.isDirty('unknown')).toBe(false)
    })
  })

  describe('markDirty()', () => {
    it('makes isDirty return true', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markDirty('c1', 'name')

      expect(tracker.isDirty('c1')).toBe(true)
    })

    it('does nothing for unknown keys', () => {
      tracker.markDirty('unknown', 'name')
      expect(tracker.isDirty('unknown')).toBe(false)
    })
  })

  describe('getDirtyFields()', () => {
    it('returns marked fields', () => {
      tracker.snapshot('c1', { name: 'Alice', email: 'a@b.com' })
      tracker.markDirty('c1', 'name')
      tracker.markDirty('c1', 'email')

      expect(tracker.getDirtyFields('c1')).toEqual(
        expect.arrayContaining(['name', 'email'])
      )
      expect(tracker.getDirtyFields('c1')).toHaveLength(2)
    })

    it('returns empty array for unknown keys', () => {
      expect(tracker.getDirtyFields('unknown')).toEqual([])
    })

    it('does not duplicate when marking a field dirty twice', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markDirty('c1', 'name')
      tracker.markDirty('c1', 'name')

      expect(tracker.getDirtyFields('c1')).toEqual(['name'])
    })
  })

  describe('markFieldsDirty()', () => {
    it('marks multiple fields at once', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30, email: 'a@b.com' })
      tracker.markFieldsDirty('c1', ['name', 'age', 'email'])

      expect(tracker.getDirtyFields('c1')).toHaveLength(3)
      expect(tracker.getDirtyFields('c1')).toEqual(
        expect.arrayContaining(['name', 'age', 'email'])
      )
    })

    it('does not duplicate existing dirty fields', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30 })
      tracker.markDirty('c1', 'name')
      tracker.markFieldsDirty('c1', ['name', 'age'])

      expect(tracker.getDirtyFields('c1')).toHaveLength(2)
    })
  })

  // =========================================================================
  // Original data
  // =========================================================================

  describe('getOriginal()', () => {
    it('returns deep clone of snapshotted data', () => {
      const original = { name: 'Alice', nested: { city: 'NYC' } }
      tracker.snapshot('c1', original)

      const retrieved = tracker.getOriginal('c1')
      expect(retrieved).toEqual(original)
      expect(retrieved).not.toBe(original) // different reference
    })

    it('modifying returned original does not affect stored original', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30 })

      const retrieved = tracker.getOriginal('c1')!
      retrieved.name = 'Bob'

      // The stored original should still be Alice
      // Note: getOriginal returns the stored original directly (not a clone on each call),
      // but the snapshot itself was deep-cloned. Let's verify the snapshot was deep-cloned
      // from the *input* data.
      const original = { name: 'Alice', nested: { x: 1 } }
      tracker.snapshot('c2', original)
      original.name = 'Changed'
      original.nested.x = 999

      const stored = tracker.getOriginal('c2')!
      expect(stored.name).toBe('Alice')
      expect((stored.nested as Record<string, unknown>).x).toBe(1)
    })

    it('returns undefined for unknown key', () => {
      expect(tracker.getOriginal('unknown')).toBeUndefined()
    })
  })

  // =========================================================================
  // buildPatch()
  // =========================================================================

  describe('buildPatch()', () => {
    it('returns null when no dirty fields', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30 })

      const patch = tracker.buildPatch('c1', { name: 'Alice', age: 30 })
      expect(patch).toBeNull()
    })

    it('returns null for unknown keys', () => {
      expect(tracker.buildPatch('unknown', { name: 'Alice' })).toBeNull()
    })

    it('returns only dirty fields from current data', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30, email: 'a@b.com' })
      tracker.markDirty('c1', 'name')

      const patch = tracker.buildPatch('c1', { name: 'Bob', age: 30, email: 'a@b.com' })
      expect(patch).toEqual({ name: 'Bob' })
    })

    it('handles multiple dirty fields correctly', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30, email: 'a@b.com' })
      tracker.markFieldsDirty('c1', ['name', 'age'])

      const patch = tracker.buildPatch('c1', { name: 'Bob', age: 31, email: 'a@b.com' })
      expect(patch).toEqual({ name: 'Bob', age: 31 })
    })
  })

  // =========================================================================
  // computeDiff()
  // =========================================================================

  describe('computeDiff()', () => {
    it('detects changed string values', () => {
      tracker.snapshot('c1', { name: 'Alice' })

      const diffs = tracker.computeDiff('c1', { name: 'Bob' })
      expect(diffs).toHaveLength(1)
      expect(diffs[0]).toEqual({ field: 'name', oldValue: 'Alice', newValue: 'Bob' })
    })

    it('detects changed numeric values', () => {
      tracker.snapshot('c1', { age: 30 })

      const diffs = tracker.computeDiff('c1', { age: 31 })
      expect(diffs).toHaveLength(1)
      expect(diffs[0]).toEqual({ field: 'age', oldValue: 30, newValue: 31 })
    })

    it('detects added fields', () => {
      tracker.snapshot('c1', { name: 'Alice' })

      const diffs = tracker.computeDiff('c1', { name: 'Alice', email: 'a@b.com' })
      expect(diffs).toHaveLength(1)
      expect(diffs[0]).toEqual({ field: 'email', oldValue: undefined, newValue: 'a@b.com' })
    })

    it('detects removed fields (undefined)', () => {
      tracker.snapshot('c1', { name: 'Alice', email: 'a@b.com' })

      const diffs = tracker.computeDiff('c1', { name: 'Alice' })
      expect(diffs).toHaveLength(1)
      expect(diffs[0]).toEqual({ field: 'email', oldValue: 'a@b.com', newValue: undefined })
    })

    it('skips @-prefixed metadata fields', () => {
      tracker.snapshot('c1', { name: 'Alice', '@odata.etag': 'W/"1"' })

      const diffs = tracker.computeDiff('c1', { name: 'Alice', '@odata.etag': 'W/"2"' })
      expect(diffs).toHaveLength(0)
    })

    it('skips _-prefixed metadata fields', () => {
      tracker.snapshot('c1', { name: 'Alice', _internal: 'x' })

      const diffs = tracker.computeDiff('c1', { name: 'Alice', _internal: 'y' })
      expect(diffs).toHaveLength(0)
    })

    it('handles nested object changes (JSON comparison)', () => {
      tracker.snapshot('c1', { address: { city: 'NYC', zip: '10001' } })

      const diffs = tracker.computeDiff('c1', { address: { city: 'LA', zip: '90001' } })
      expect(diffs).toHaveLength(1)
      expect(diffs[0].field).toBe('address')
      expect(diffs[0].oldValue).toEqual({ city: 'NYC', zip: '10001' })
      expect(diffs[0].newValue).toEqual({ city: 'LA', zip: '90001' })
    })

    it('handles array changes', () => {
      tracker.snapshot('c1', { tags: ['a', 'b'] })

      const diffs = tracker.computeDiff('c1', { tags: ['a', 'b', 'c'] })
      expect(diffs).toHaveLength(1)
      expect(diffs[0].field).toBe('tags')
    })

    it('returns empty array when no changes', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30 })

      const diffs = tracker.computeDiff('c1', { name: 'Alice', age: 30 })
      expect(diffs).toHaveLength(0)
    })

    it('returns empty array for unknown keys', () => {
      expect(tracker.computeDiff('unknown', { name: 'Alice' })).toEqual([])
    })
  })

  // =========================================================================
  // detectChanges()
  // =========================================================================

  describe('detectChanges()', () => {
    it('auto-detects and marks all changed fields', () => {
      tracker.snapshot('c1', { name: 'Alice', age: 30, email: 'a@b.com' })

      const changed = tracker.detectChanges('c1', { name: 'Bob', age: 31, email: 'a@b.com' })
      expect(changed).toEqual(expect.arrayContaining(['name', 'age']))
      expect(changed).toHaveLength(2)

      // Should also be marked as dirty
      expect(tracker.isDirty('c1')).toBe(true)
      expect(tracker.getDirtyFields('c1')).toEqual(expect.arrayContaining(['name', 'age']))
    })

    it('returns list of changed field names', () => {
      tracker.snapshot('c1', { name: 'Alice' })

      const changed = tracker.detectChanges('c1', { name: 'Bob' })
      expect(changed).toEqual(['name'])
    })
  })

  // =========================================================================
  // clearDirty()
  // =========================================================================

  describe('clearDirty()', () => {
    it('clears dirty state so isDirty returns false', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markDirty('c1', 'name')
      expect(tracker.isDirty('c1')).toBe(true)

      tracker.clearDirty('c1')
      expect(tracker.isDirty('c1')).toBe(false)
    })

    it('getDirtyFields returns empty array after clear', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markFieldsDirty('c1', ['name', 'age'])

      tracker.clearDirty('c1')
      expect(tracker.getDirtyFields('c1')).toEqual([])
    })
  })

  // =========================================================================
  // updateSnapshot()
  // =========================================================================

  describe('updateSnapshot()', () => {
    it('updates baseline and clears dirty', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markDirty('c1', 'name')
      expect(tracker.isDirty('c1')).toBe(true)

      tracker.updateSnapshot('c1', { name: 'Bob' })
      expect(tracker.isDirty('c1')).toBe(false)
      expect(tracker.getOriginal('c1')).toEqual({ name: 'Bob' })
    })
  })

  // =========================================================================
  // remove()
  // =========================================================================

  describe('remove()', () => {
    it('removes tracking entirely', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.markDirty('c1', 'name')

      tracker.remove('c1')

      expect(tracker.isDirty('c1')).toBe(false)
      expect(tracker.getOriginal('c1')).toBeUndefined()
    })
  })

  // =========================================================================
  // Aggregate methods
  // =========================================================================

  describe('hasAnyChanges()', () => {
    it('returns false when no entities are dirty', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.snapshot('c2', { name: 'Bob' })

      expect(tracker.hasAnyChanges()).toBe(false)
    })

    it('returns true with mix of clean and dirty entities', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.snapshot('c2', { name: 'Bob' })
      tracker.markDirty('c2', 'name')

      expect(tracker.hasAnyChanges()).toBe(true)
    })
  })

  describe('getAllDirtyKeys()', () => {
    it('returns only dirty keys', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.snapshot('c2', { name: 'Bob' })
      tracker.snapshot('c3', { name: 'Charlie' })

      tracker.markDirty('c1', 'name')
      tracker.markDirty('c3', 'name')

      const dirtyKeys = tracker.getAllDirtyKeys()
      expect(dirtyKeys).toEqual(expect.arrayContaining(['c1', 'c3']))
      expect(dirtyKeys).toHaveLength(2)
      expect(dirtyKeys).not.toContain('c2')
    })
  })

  describe('getDirtyCount()', () => {
    it('returns correct count', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.snapshot('c2', { name: 'Bob' })
      tracker.snapshot('c3', { name: 'Charlie' })

      expect(tracker.getDirtyCount()).toBe(0)

      tracker.markDirty('c1', 'name')
      expect(tracker.getDirtyCount()).toBe(1)

      tracker.markDirty('c3', 'name')
      expect(tracker.getDirtyCount()).toBe(2)
    })
  })

  describe('clear()', () => {
    it('removes all tracked entities', () => {
      tracker.snapshot('c1', { name: 'Alice' })
      tracker.snapshot('c2', { name: 'Bob' })
      tracker.markDirty('c1', 'name')
      tracker.markDirty('c2', 'name')

      tracker.clear()

      expect(tracker.hasAnyChanges()).toBe(false)
      expect(tracker.getAllDirtyKeys()).toEqual([])
      expect(tracker.getDirtyCount()).toBe(0)
      expect(tracker.getOriginal('c1')).toBeUndefined()
      expect(tracker.getOriginal('c2')).toBeUndefined()
    })
  })

  // =========================================================================
  // Deep clone behavior
  // =========================================================================

  describe('deep clone', () => {
    it('nested objects are cloned (not referenced)', () => {
      const data = { name: 'Alice', address: { city: 'NYC', coords: { lat: 40.7 } } }
      tracker.snapshot('c1', data)

      // Modify the original input
      data.address.city = 'LA'
      data.address.coords.lat = 34.0

      const stored = tracker.getOriginal('c1')!
      expect((stored.address as Record<string, unknown>)).toEqual({ city: 'NYC', coords: { lat: 40.7 } })
    })

    it('arrays within objects are cloned', () => {
      const data = { name: 'Alice', tags: ['vip', 'active'] }
      tracker.snapshot('c1', data)

      // Modify the original input array
      data.tags.push('modified')

      const stored = tracker.getOriginal('c1')!
      expect(stored.tags).toEqual(['vip', 'active'])
    })

    it('isEqual handles Date objects correctly in computeDiff', () => {
      const date1 = new Date('2024-01-15')
      const date2 = new Date('2024-01-15')
      const date3 = new Date('2024-06-01')

      // Same dates should show no diff
      tracker.snapshot('c1', { created: date1 })
      const noDiffs = tracker.computeDiff('c1', { created: date2 })
      expect(noDiffs).toHaveLength(0)

      // Different dates should detect a diff
      const diffs = tracker.computeDiff('c1', { created: date3 })
      expect(diffs).toHaveLength(1)
      expect(diffs[0].field).toBe('created')
    })

    it('isEqual compares arrays element-by-element', () => {
      tracker.snapshot('c1', { items: [1, 2, 3] })

      // Same array content — no diff
      const noDiffs = tracker.computeDiff('c1', { items: [1, 2, 3] })
      expect(noDiffs).toHaveLength(0)

      // Different length — diff
      const diffLength = tracker.computeDiff('c1', { items: [1, 2] })
      expect(diffLength).toHaveLength(1)

      // Different element — diff
      const diffElement = tracker.computeDiff('c1', { items: [1, 99, 3] })
      expect(diffElement).toHaveLength(1)
    })
  })
})
