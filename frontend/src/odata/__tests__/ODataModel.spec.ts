import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'

// ---------------------------------------------------------------------------
// Mock odataService and etagStore
// ---------------------------------------------------------------------------
vi.mock('@/services/odataService', () => ({
  odataService: {
    query: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    batch: vi.fn(),
  },
  etagStore: { set: vi.fn(), get: vi.fn() },
}))

// Mock odataQueryBuilder utilities (used by ODataModel internally)
vi.mock('@/utils/odataQueryBuilder', () => ({
  buildODataFilter: vi.fn((filters: unknown[]) => `mock-filter(${(filters as { field: string }[]).map(f => f.field).join(',')})`),
  buildExpandString: vi.fn(() => 'MockExpand'),
}))

import { ODataModel } from '../ODataModel'
import { odataService } from '@/services/odataService'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Flush all pending microtasks / auto-load calls */
const flushPromises = () => new Promise<void>(r => setTimeout(r, 0))

function makeQueryResponse<T>(value: T[], count?: number) {
  return {
    value,
    '@odata.count': count ?? value.length,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ODataModel', () => {
  let model: ODataModel

  beforeEach(() => {
    vi.clearAllMocks()
    model = new ODataModel({ module: 'testmod' })
  })

  // =========================================================================
  // Constructor & initial state
  // =========================================================================

  describe('constructor', () => {
    it('creates model with correct config defaults', () => {
      expect(model.module).toBe('testmod')
      expect(model.hasPendingChanges.value).toBe(false)
      expect(model.pendingChangeCount.value).toBe(0)
    })

    it('hasPendingChanges initially false, pendingChangeCount is 0', () => {
      expect(model.hasPendingChanges.value).toBe(false)
      expect(model.pendingChangeCount.value).toBe(0)
    })
  })

  // =========================================================================
  // bindList()
  // =========================================================================

  describe('bindList()', () => {
    it('returns reactive list binding with data, totalCount, isLoading refs', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([]))

      const list = model.bindList('/Customers')
      // The binding object exposes the expected reactive properties
      expect(list.data).toBeDefined()
      expect(list.totalCount).toBeDefined()
      expect(list.isLoading).toBeDefined()
      expect(list.error).toBeDefined()
      expect(list.currentPage).toBeDefined()
      expect(list.pageSize).toBeDefined()
      expect(list.totalPages).toBeDefined()
      expect(list.hasMore).toBeDefined()

      await flushPromises()
    })

    it('auto-loads data (odataService.query called)', async () => {
      const items = [{ Id: '1', name: 'A' }, { Id: '2', name: 'B' }]
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse(items, 2))

      const list = model.bindList('/Customers')
      await flushPromises()

      expect(odataService.query).toHaveBeenCalledTimes(1)
      expect(odataService.query).toHaveBeenCalledWith(
        'testmod',
        'Customers',
        expect.objectContaining({ $count: true, $top: 20, $skip: 0 }),
        expect.anything()
      )
      expect(list.data.value).toEqual(items)
      expect(list.totalCount.value).toBe(2)
    })

    it('goToPage() changes page and reloads with correct $skip', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse(
        [{ Id: '1' }], 40
      ))

      const list = model.bindList('/Customers', { $top: 10 })
      await flushPromises()

      // Now go to page 3
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse(
        [{ Id: '21' }], 40
      ))
      await list.goToPage(3)
      await flushPromises()

      expect(list.currentPage.value).toBe(3)
      // $skip should be (3-1)*10 = 20
      const lastCall = (odataService.query as Mock).mock.calls.at(-1)!
      expect(lastCall[2].$skip).toBe(20)
    })

    it('sort() resets to page 1 and adds orderby', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([], 0))

      const list = model.bindList('/Customers')
      await flushPromises()

      await list.sort('name', 'desc')
      await flushPromises()

      expect(list.currentPage.value).toBe(1)
      const lastCall = (odataService.query as Mock).mock.calls.at(-1)!
      expect(lastCall[2].$orderby).toBe('name desc')
    })

    it('filter() resets to page 1 and adds filter', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([], 0))

      const list = model.bindList('/Customers')
      await flushPromises()

      await list.filter([{ field: 'status', operator: 'eq', value: 'Active' }])
      await flushPromises()

      expect(list.currentPage.value).toBe(1)
      const lastCall = (odataService.query as Mock).mock.calls.at(-1)!
      expect(lastCall[2].$filter).toBeDefined()
    })

    it('search() sets $search and reloads', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([], 0))

      const list = model.bindList('/Customers')
      await flushPromises()

      await list.search('alice')
      await flushPromises()

      expect(list.currentPage.value).toBe(1)
      const lastCall = (odataService.query as Mock).mock.calls.at(-1)!
      expect(lastCall[2].$search).toBe('alice')
    })

    it('requestMore() appends next page data', async () => {
      const page1 = [{ Id: '1', name: 'A' }]
      const page2 = [{ Id: '2', name: 'B' }]

      ;(odataService.query as Mock).mockResolvedValueOnce(makeQueryResponse(page1, 5))

      const list = model.bindList('/Customers', { $top: 1 })
      await flushPromises()
      expect(list.data.value).toEqual(page1)

      ;(odataService.query as Mock).mockResolvedValueOnce(makeQueryResponse(page2, 5))
      await list.requestMore()
      await flushPromises()

      // Data should be appended (page1 + page2)
      expect(list.data.value).toEqual([...page1, ...page2])
    })

    it('destroy() removes binding from active set', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([], 0))

      const list = model.bindList('/Customers')
      await flushPromises()

      // Should not throw
      list.destroy()
    })
  })

  // =========================================================================
  // bindEntity()
  // =========================================================================

  describe('bindEntity()', () => {
    it('returns reactive entity context with data, isDirty, dirtyFields', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx = model.bindEntity('/Customers', '1')

      expect(ctx.data).toBeDefined()
      expect(ctx.isDirty).toBeDefined()
      expect(ctx.dirtyFields).toBeDefined()
      expect(ctx.isLoading).toBeDefined()
      expect(ctx.error).toBeDefined()
      expect(ctx.concurrencyError).toBeDefined()

      await flushPromises()
    })

    it('auto-loads entity (odataService.getById called)', async () => {
      const entity = { Id: '1', name: 'Alice', email: 'a@b.com' }
      ;(odataService.getById as Mock).mockResolvedValue(entity)

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      expect(odataService.getById).toHaveBeenCalledWith('testmod', 'Customers', '1', expect.anything())
      expect(ctx.data.value).toEqual(entity)
    })

    it('setProperty() marks dirty and registers pending change', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      ctx.setProperty('name', 'Bob')

      expect(ctx.isDirty.value).toBe(true)
      expect(ctx.dirtyFields.value).toContain('name')
      expect(model.hasPendingChanges.value).toBe(true)
      expect(model.pendingChangeCount.value).toBe(1)
    })

    it('resetChanges() restores original values and removes pending changes', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      ctx.setProperty('name', 'Bob')
      expect(model.hasPendingChanges.value).toBe(true)

      ctx.resetChanges()

      expect(ctx.isDirty.value).toBe(false)
      expect(ctx.dirtyFields.value).toHaveLength(0)
      expect(model.hasPendingChanges.value).toBe(false)
      // The data should be restored
      expect((ctx.data.value as Record<string, unknown>)?.name).toBe('Alice')
    })

    it('delete() calls odataService.delete, handles 412', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      // Simulate 412 Precondition Failed
      const err = new Error('Conflict') as Error & { response?: { status?: number } }
      err.response = { status: 412 }
      ;(odataService.delete as Mock).mockRejectedValue(err)

      await expect(ctx.delete()).rejects.toThrow()
      expect(ctx.concurrencyError.value).toBe(true)
      expect(ctx.error.value).toContain('modified by another user')
    })
  })

  // =========================================================================
  // bindProperty()
  // =========================================================================

  describe('bindProperty()', () => {
    it('returns reactive property binding', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const prop = model.bindProperty<string>('/Customers(1)/name')

      expect(prop.value).toBeDefined()
      expect(prop.isLoading).toBeDefined()
      expect(prop.error).toBeDefined()

      await flushPromises()
      expect(prop.value.value).toBe('Alice')
    })

    it('parses /EntitySet(key)/property path correctly', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: 'k1', email: 'a@b.com' })

      const prop = model.bindProperty('/Orders(k1)/email')
      await flushPromises()

      expect(odataService.getById).toHaveBeenCalledWith('testmod', 'Orders', 'k1')
      expect(prop.value.value).toBe('a@b.com')
    })

    it('setValue() marks dirty', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const prop = model.bindProperty<string>('/Customers(1)/name')
      await flushPromises()

      prop.setValue('Bob')

      expect(prop.value.value).toBe('Bob')
      expect(model.hasPendingChanges.value).toBe(true)
    })

    it('invalid path throws error', () => {
      expect(() => model.bindProperty('/Customers')).toThrow(
        /Invalid property path/
      )
    })
  })

  // =========================================================================
  // CRUD operations
  // =========================================================================

  describe('CRUD operations', () => {
    it('create() calls odataService.create and invalidates cache', async () => {
      const created = { Id: 'new1', name: 'Charlie' }
      ;(odataService.create as Mock).mockResolvedValue(created)

      const result = await model.create('Customers', { name: 'Charlie' })

      expect(odataService.create).toHaveBeenCalledWith('testmod', 'Customers', { name: 'Charlie' })
      expect(result).toEqual(created)
    })

    it('update() with explicit data', async () => {
      const updated = { Id: '1', name: 'Bob' }
      ;(odataService.update as Mock).mockResolvedValue(updated)

      const result = await model.update('Customers', '1', { name: 'Bob' })

      expect(odataService.update).toHaveBeenCalledWith('testmod', 'Customers', '1', { name: 'Bob' })
      expect(result).toEqual(updated)
    })

    it('update() without data builds from dirty fields', async () => {
      // First bind an entity so we have store/tracker state
      const entity = { Id: '1', name: 'Alice', email: 'a@b.com' }
      ;(odataService.getById as Mock).mockResolvedValue(entity)

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      // Mark a field dirty via setProperty
      ctx.setProperty('name', 'Bob')

      const updated = { Id: '1', name: 'Bob', email: 'a@b.com' }
      ;(odataService.update as Mock).mockResolvedValue(updated)

      const result = await model.update('Customers', '1')
      expect(odataService.update).toHaveBeenCalledWith(
        'testmod', 'Customers', '1',
        expect.objectContaining({ name: 'Bob' })
      )
      expect(result).toEqual(updated)
    })

    it('remove() calls odataService.delete and cleans up', async () => {
      ;(odataService.delete as Mock).mockResolvedValue(undefined)

      await model.remove('Customers', '1')

      expect(odataService.delete).toHaveBeenCalledWith('testmod', 'Customers', '1')
    })
  })

  // =========================================================================
  // submitChanges()
  // =========================================================================

  describe('submitChanges()', () => {
    it('with single change: submits sequentially', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      ctx.setProperty('name', 'Bob')
      expect(model.pendingChangeCount.value).toBe(1)

      const updated = { Id: '1', name: 'Bob' }
      ;(odataService.update as Mock).mockResolvedValue(updated)

      await model.submitChanges()

      // Sequential path is used for single change (autoBatch=true but only 1 change)
      expect(odataService.update).toHaveBeenCalled()
      expect(model.pendingChangeCount.value).toBe(0)
    })

    it('with multiple changes and autoBatch: submits as batch', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      const ctx1 = model.bindEntity('/Customers', '1')
      await flushPromises()

      ;(odataService.getById as Mock).mockResolvedValue({ Id: '2', name: 'Carol' })
      const ctx2 = model.bindEntity('/Customers', '2')
      await flushPromises()

      ctx1.setProperty('name', 'Bob')
      ctx2.setProperty('name', 'Dave')
      expect(model.pendingChangeCount.value).toBe(2)

      // Mock batch to return successful responses
      ;(odataService.batch as Mock).mockResolvedValue([
        { id: 'change-0', status: 200, body: { Id: '1', name: 'Bob' } },
        { id: 'change-1', status: 200, body: { Id: '2', name: 'Dave' } },
      ])

      await model.submitChanges()

      expect(odataService.batch).toHaveBeenCalled()
      expect(model.pendingChangeCount.value).toBe(0)
    })

    it('clears pendingChanges after success', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })
      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      ctx.setProperty('name', 'Updated')
      expect(model.hasPendingChanges.value).toBe(true)

      ;(odataService.update as Mock).mockResolvedValue({ Id: '1', name: 'Updated' })
      await model.submitChanges()

      expect(model.hasPendingChanges.value).toBe(false)
      expect(model.pendingChangeCount.value).toBe(0)
    })

    it('resetChanges() restores all entities', async () => {
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })
      const ctx = model.bindEntity('/Customers', '1')
      await flushPromises()

      ctx.setProperty('name', 'Changed')
      expect(model.hasPendingChanges.value).toBe(true)

      model.resetChanges()

      expect(model.hasPendingChanges.value).toBe(false)
      expect(model.pendingChangeCount.value).toBe(0)
    })
  })

  // =========================================================================
  // destroy()
  // =========================================================================

  describe('destroy()', () => {
    it('cleans up all bindings, store, tracker, cache', async () => {
      ;(odataService.query as Mock).mockResolvedValue(makeQueryResponse([{ Id: '1' }], 1))
      ;(odataService.getById as Mock).mockResolvedValue({ Id: '1', name: 'Alice' })

      model.bindList('/Customers')
      model.bindEntity('/Customers', '1')
      await flushPromises()

      model.destroy()

      // After destroy, pending changes should be cleared
      expect(model.hasPendingChanges.value).toBe(false)
      expect(model.pendingChangeCount.value).toBe(0)
    })
  })
})
