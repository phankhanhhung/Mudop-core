import { describe, it, expect, beforeEach } from 'vitest'
import { ref } from 'vue'
import { OptimisticUpdateManager } from '../OptimisticUpdates'

interface TestItem {
  Id: string
  name: string
  [key: string]: unknown
}

describe('OptimisticUpdateManager', () => {
  let manager: OptimisticUpdateManager

  beforeEach(() => {
    manager = new OptimisticUpdateManager()
  })

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  function makeItems(...items: Array<{ id: string; name: string }>): TestItem[] {
    return items.map(i => ({ Id: i.id, name: i.name }))
  }

  function createDeferred<T>(): {
    promise: Promise<T>
    resolve: (v: T) => void
    reject: (e: Error) => void
  } {
    let resolve!: (v: T) => void
    let reject!: (e: Error) => void
    const promise = new Promise<T>((res, rej) => {
      resolve = res
      reject = rej
    })
    return { promise, resolve, reject }
  }

  // ===========================================================================
  // optimisticUpdate()
  // ===========================================================================

  describe('optimisticUpdate()', () => {
    it('immediately updates the item in the data ref', () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }, { id: '2', name: 'Bob' }))
      const deferred = createDeferred<TestItem>()

      // Start the update (do not await yet)
      manager.optimisticUpdate(data, '1', { name: 'Alice Updated' }, deferred.promise)

      // Should already be updated
      expect(data.value[0].name).toBe('Alice Updated')
      expect(data.value[1].name).toBe('Bob') // Untouched

      // Clean up
      deferred.resolve({ Id: '1', name: 'Alice Updated' })
    })

    it('on server success: replaces with server response data', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const serverResponse: TestItem = { Id: '1', name: 'Alice Server' }

      await manager.optimisticUpdate(data, '1', { name: 'Optimistic' }, Promise.resolve(serverResponse))

      expect(data.value[0].name).toBe('Alice Server')
    })

    it('on server failure: rolls back to previous data', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Original' }))
      const serverError = new Error('Update failed')

      try {
        await manager.optimisticUpdate(data, '1', { name: 'Optimistic' }, Promise.reject(serverError))
      } catch {
        // expected
      }

      expect(data.value[0].name).toBe('Original')
    })

    it('returns the server result on success', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const serverResponse: TestItem = { Id: '1', name: 'From Server' }

      const result = await manager.optimisticUpdate(
        data,
        '1',
        { name: 'Optimistic' },
        Promise.resolve(serverResponse)
      )

      expect(result).toEqual(serverResponse)
    })

    it('throws the server error on failure', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      await expect(
        manager.optimisticUpdate(data, '1', { name: 'X' }, Promise.reject(new Error('Boom')))
      ).rejects.toThrow('Boom')
    })

    it('item not in list: just returns server promise result', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const serverResponse: TestItem = { Id: '99', name: 'NotInList' }

      const result = await manager.optimisticUpdate(
        data,
        '99',
        { name: 'X' },
        Promise.resolve(serverResponse)
      )

      expect(result).toEqual(serverResponse)
      // Original data untouched
      expect(data.value).toHaveLength(1)
      expect(data.value[0].name).toBe('Alice')
    })

    it('hasPending is true during operation, false after', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const deferred = createDeferred<TestItem>()

      expect(manager.hasPending.value).toBe(false)

      const promise = manager.optimisticUpdate(data, '1', { name: 'New' }, deferred.promise)

      expect(manager.hasPending.value).toBe(true)

      deferred.resolve({ Id: '1', name: 'New' })
      await promise

      expect(manager.hasPending.value).toBe(false)
    })
  })

  // ===========================================================================
  // optimisticCreate()
  // ===========================================================================

  describe('optimisticCreate()', () => {
    it('immediately adds item to data ref', () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const deferred = createDeferred<TestItem>()

      manager.optimisticCreate(data, { Id: 'temp-2', name: 'Bob' }, deferred.promise)

      expect(data.value).toHaveLength(2)
      expect(data.value[1]).toEqual({ Id: 'temp-2', name: 'Bob' })

      deferred.resolve({ Id: '2', name: 'Bob' })
    })

    it('on server success: replaces optimistic item with server response', async () => {
      const data = ref<TestItem[]>([])
      const serverResponse: TestItem = { Id: 'server-1', name: 'Server Bob' }

      await manager.optimisticCreate(data, { Id: 'temp-1', name: 'Bob' }, Promise.resolve(serverResponse))

      expect(data.value).toHaveLength(1)
      expect(data.value[0]).toEqual(serverResponse)
    })

    it('on server failure: removes the optimistic item', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      try {
        await manager.optimisticCreate(
          data,
          { Id: 'temp-2', name: 'FailBob' },
          Promise.reject(new Error('Create failed'))
        )
      } catch {
        // expected
      }

      expect(data.value).toHaveLength(1)
      expect(data.value[0].name).toBe('Alice')
    })

    it('pendingCount tracks active operations', async () => {
      const data = ref<TestItem[]>([])
      const d1 = createDeferred<TestItem>()
      const d2 = createDeferred<TestItem>()

      expect(manager.pendingCount.value).toBe(0)

      const p1 = manager.optimisticCreate(data, { Id: 'a', name: 'A' }, d1.promise)
      expect(manager.pendingCount.value).toBe(1)

      const p2 = manager.optimisticCreate(data, { Id: 'b', name: 'B' }, d2.promise)
      expect(manager.pendingCount.value).toBe(2)

      d1.resolve({ Id: 'a', name: 'A' })
      await p1
      expect(manager.pendingCount.value).toBe(1)

      d2.resolve({ Id: 'b', name: 'B' })
      await p2
      expect(manager.pendingCount.value).toBe(0)
    })
  })

  // ===========================================================================
  // optimisticDelete()
  // ===========================================================================

  describe('optimisticDelete()', () => {
    it('immediately removes item from data ref', () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }, { id: '2', name: 'Bob' }))
      const deferred = createDeferred<void>()

      manager.optimisticDelete(data, '1', deferred.promise)

      expect(data.value).toHaveLength(1)
      expect(data.value[0].name).toBe('Bob')

      deferred.resolve()
    })

    it('on server success: item stays removed', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }, { id: '2', name: 'Bob' }))

      await manager.optimisticDelete(data, '1', Promise.resolve())

      expect(data.value).toHaveLength(1)
      expect(data.value[0].name).toBe('Bob')
    })

    it('on server failure: item is restored at original position', async () => {
      const data = ref<TestItem[]>(
        makeItems({ id: '1', name: 'Alice' }, { id: '2', name: 'Bob' }, { id: '3', name: 'Carol' })
      )

      try {
        await manager.optimisticDelete(data, '2', Promise.reject(new Error('Delete failed')))
      } catch {
        // expected
      }

      expect(data.value).toHaveLength(3)
      expect(data.value[0].name).toBe('Alice')
      expect(data.value[1].name).toBe('Bob')
      expect(data.value[2].name).toBe('Carol')
    })

    it('item not found: just awaits server promise', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      await manager.optimisticDelete(data, '99', Promise.resolve())

      // Data unchanged
      expect(data.value).toHaveLength(1)
      expect(data.value[0].name).toBe('Alice')
    })
  })

  // ===========================================================================
  // History and state
  // ===========================================================================

  describe('history and state', () => {
    it('history accumulates completed operations', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      await manager.optimisticUpdate(
        data,
        '1',
        { name: 'Updated' },
        Promise.resolve({ Id: '1', name: 'Updated' })
      )
      expect(manager.history.value).toHaveLength(1)
      expect(manager.history.value[0].type).toBe('update')

      await manager.optimisticCreate(
        data,
        { Id: 'temp', name: 'New' },
        Promise.resolve({ Id: '2', name: 'New' })
      )
      expect(manager.history.value).toHaveLength(2)
      expect(manager.history.value[1].type).toBe('create')
    })

    it('history bounded to max 100 entries (prunes to 50)', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      // Generate 101 history entries
      for (let i = 0; i < 101; i++) {
        await manager.optimisticUpdate(
          data,
          '1',
          { name: `Iteration ${i}` },
          Promise.resolve({ Id: '1', name: `Iteration ${i}` })
        )
      }

      // After exceeding 100, it prunes to the last 50
      expect(manager.history.value.length).toBeLessThanOrEqual(50)
    })

    it('clearHistory() empties the history', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))

      await manager.optimisticUpdate(
        data,
        '1',
        { name: 'New' },
        Promise.resolve({ Id: '1', name: 'New' })
      )
      expect(manager.history.value.length).toBeGreaterThan(0)

      manager.clearHistory()
      expect(manager.history.value).toHaveLength(0)
    })

    it('getPending() returns current active operations', async () => {
      const data = ref<TestItem[]>(makeItems({ id: '1', name: 'Alice' }))
      const deferred = createDeferred<TestItem>()

      expect(manager.getPending()).toHaveLength(0)

      const promise = manager.optimisticUpdate(data, '1', { name: 'Pending' }, deferred.promise)
      expect(manager.getPending()).toHaveLength(1)
      expect(manager.getPending()[0].type).toBe('update')
      expect(manager.getPending()[0].status).toBe('pending')

      deferred.resolve({ Id: '1', name: 'Pending' })
      await promise

      expect(manager.getPending()).toHaveLength(0)
    })
  })
})
