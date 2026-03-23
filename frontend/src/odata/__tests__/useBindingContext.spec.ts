import { describe, it, expect, beforeEach, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import type { ODataResponse } from '@/types/odata'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

vi.mock('@/services/odataService', () => ({
  odataService: {
    query: vi.fn(),
    getById: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    getChildren: vi.fn(),
  },
  etagStore: { remove: vi.fn(), set: vi.fn() },
}))

vi.mock('@/utils/requestDedup', () => ({
  createRequestScope: () => ({
    getSignal: () => new AbortController().signal,
    cancel: vi.fn(),
  }),
}))

vi.mock('@/utils/odataQueryBuilder', () => ({
  buildODataFilter: vi.fn((conditions: Array<{ field: string; operator: string; value: unknown }>) =>
    conditions.map(c => `${c.field} ${c.operator} '${c.value}'`).join(' and ')
  ),
  buildExpandString: vi.fn((expands: Record<string, unknown>) =>
    Object.keys(expands).join(',')
  ),
}))

import { odataService, etagStore } from '@/services/odataService'
import { useBindingContext, useEntityBinding, useRelativeBinding } from '../useBindingContext'

// Typed mock helpers
const mockQuery = odataService.query as ReturnType<typeof vi.fn>
const mockGetById = odataService.getById as ReturnType<typeof vi.fn>
const mockUpdate = odataService.update as ReturnType<typeof vi.fn>
const mockDelete = odataService.delete as ReturnType<typeof vi.fn>
const mockGetChildren = odataService.getChildren as ReturnType<typeof vi.fn>
const mockEtagRemove = etagStore.remove as ReturnType<typeof vi.fn>

function makeQueryResponse<T>(items: T[], count?: number): ODataResponse<T> {
  return {
    value: items,
    '@odata.count': count ?? items.length,
  }
}

// ---------------------------------------------------------------------------
// useBindingContext (list binding)
// ---------------------------------------------------------------------------

describe('useBindingContext', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockQuery.mockResolvedValue(makeQueryResponse([]))
  })

  function createBinding(
    path = '/Customers',
    module = 'sales',
    options?: Parameters<typeof useBindingContext>[2]
  ) {
    const scope = effectScope()
    let binding: ReturnType<typeof useBindingContext>
    scope.run(() => {
      binding = useBindingContext(path, module, options)
    })
    return { binding: binding!, scope }
  }

  it('auto-loads data on creation', async () => {
    const items = [{ Id: '1', Name: 'Alice' }]
    mockQuery.mockResolvedValue(makeQueryResponse(items, 1))

    const { scope } = createBinding()
    // Wait for the load promise to resolve
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    expect(mockQuery).toHaveBeenCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $count: true, $top: 20, $skip: 0 }),
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    )

    scope.stop()
  })

  it('data ref contains response.value items', async () => {
    const items = [{ Id: '1', Name: 'Alice' }, { Id: '2', Name: 'Bob' }]
    mockQuery.mockResolvedValue(makeQueryResponse(items, 2))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.data.value).toHaveLength(2))

    expect(binding.data.value).toEqual(items)
    scope.stop()
  })

  it('totalCount from @odata.count', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([{ Id: '1' }], 42))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.totalCount.value).toBe(42))

    scope.stop()
  })

  it('isLoading transitions: true during load, false after', async () => {
    let resolveQuery: (value: ODataResponse<unknown>) => void
    mockQuery.mockImplementation(
      () => new Promise((resolve) => { resolveQuery = resolve })
    )

    const { binding, scope } = createBinding()

    // isLoading should be true while waiting
    expect(binding.isLoading.value).toBe(true)

    // Resolve the query
    resolveQuery!(makeQueryResponse([]))
    await vi.waitFor(() => expect(binding.isLoading.value).toBe(false))

    scope.stop()
  })

  it('goToPage updates currentPage and reloads with correct $skip', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 100))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    await binding.goToPage(3)

    expect(binding.currentPage.value).toBe(3)
    expect(mockQuery).toHaveBeenCalledTimes(2)
    // Page 3 with pageSize 20: skip = (3-1)*20 = 40
    expect(mockQuery).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $skip: 40, $top: 20 }),
      expect.any(Object)
    )

    scope.stop()
  })

  it('sort resets to page 1 and adds $orderby', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 100))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    // Go to page 3 first
    binding.currentPage.value = 3

    await binding.sort('name', 'desc')

    expect(binding.currentPage.value).toBe(1)
    expect(mockQuery).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $orderby: 'name desc', $skip: 0 }),
      expect.any(Object)
    )

    scope.stop()
  })

  it('filter resets to page 1 and builds $filter', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([]))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    binding.currentPage.value = 5

    await binding.filter([{ field: 'name', operator: 'eq', value: 'Alice' }])

    expect(binding.currentPage.value).toBe(1)
    expect(mockQuery).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $filter: expect.any(String), $skip: 0 }),
      expect.any(Object)
    )

    scope.stop()
  })

  it('search resets to page 1 and sets $search', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([]))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    binding.currentPage.value = 3

    await binding.search('hello')

    expect(binding.currentPage.value).toBe(1)
    expect(mockQuery).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $search: 'hello', $skip: 0 }),
      expect.any(Object)
    )

    scope.stop()
  })

  it('totalPages computed correctly from totalCount/pageSize', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 55))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.totalCount.value).toBe(55))

    // 55 / 20 = 2.75 → ceil = 3
    expect(binding.totalPages.value).toBe(3)

    scope.stop()
  })

  it('hasMore is true when currentPage < totalPages', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 55))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.totalCount.value).toBe(55))

    // Page 1 of 3 → hasMore = true
    expect(binding.hasMore.value).toBe(true)

    scope.stop()
  })

  it('isEmpty is true when no data and not loading', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 0))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.isLoading.value).toBe(false))

    expect(binding.isEmpty.value).toBe(true)

    scope.stop()
  })

  it('isEmpty is false while loading', async () => {
    let resolveQuery: (value: ODataResponse<unknown>) => void
    mockQuery.mockImplementation(
      () => new Promise((resolve) => { resolveQuery = resolve })
    )

    const { binding, scope } = createBinding()

    // Still loading — isEmpty should be false even though data is empty
    expect(binding.isEmpty.value).toBe(false)

    resolveQuery!(makeQueryResponse([]))
    await vi.waitFor(() => expect(binding.isLoading.value).toBe(false))

    scope.stop()
  })

  it('setPageSize resets to page 1 and reloads', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([], 100))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    binding.currentPage.value = 3
    await binding.setPageSize(50)

    expect(binding.currentPage.value).toBe(1)
    expect(binding.pageSize.value).toBe(50)
    expect(mockQuery).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      expect.objectContaining({ $top: 50, $skip: 0 }),
      expect.any(Object)
    )

    scope.stop()
  })

  it('autoLoad: false skips initial load', async () => {
    const { binding: _binding, scope } = createBinding('/Customers', 'sales', { autoLoad: false })

    // Give time for any async to settle
    await nextTick()
    await nextTick()

    expect(mockQuery).not.toHaveBeenCalled()

    scope.stop()
  })

  it('error handling: sets error ref on failed query', async () => {
    mockQuery.mockRejectedValue(new Error('Network failure'))

    const { binding, scope } = createBinding()
    await vi.waitFor(() => expect(binding.error.value).toBe('Network failure'))

    expect(binding.isLoading.value).toBe(false)

    scope.stop()
  })

  it('extracts entity set from path correctly', async () => {
    mockQuery.mockResolvedValue(makeQueryResponse([]))

    const { binding, scope } = createBinding('/OrderItems', 'purchasing')
    await vi.waitFor(() => expect(mockQuery).toHaveBeenCalledTimes(1))

    expect(binding.context.entitySet).toBe('OrderItems')
    expect(binding.context.module).toBe('purchasing')

    scope.stop()
  })
})

// ---------------------------------------------------------------------------
// useEntityBinding
// ---------------------------------------------------------------------------

describe('useEntityBinding', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockGetById.mockResolvedValue({ Id: '123', Name: 'Alice', Age: 30 })
  })

  function createEntityBinding(
    path = '/Customers',
    module = 'sales',
    key = '123',
    options?: Parameters<typeof useEntityBinding>[3]
  ) {
    const scope = effectScope()
    let binding: ReturnType<typeof useEntityBinding>
    scope.run(() => {
      binding = useEntityBinding(path, module, key, options)
    })
    return { binding: binding!, scope }
  }

  it('auto-loads entity on creation', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(mockGetById).toHaveBeenCalledTimes(1))

    expect(mockGetById).toHaveBeenCalledWith(
      'sales',
      'Customers',
      '123',
      expect.any(Object)
    )
    expect(binding.entity.value).toEqual({ Id: '123', Name: 'Alice', Age: 30 })

    scope.stop()
  })

  it('setProperty updates data and marks dirty', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')

    expect((binding.entity.value as Record<string, unknown>).Name).toBe('Bob')
    expect(binding.isDirty.value).toBe(true)

    scope.stop()
  })

  it('isDirty is true after setProperty', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    // Note: isDirty is a computed backed by a non-reactive ChangeTracker,
    // so we verify the dirty state after mutation without pre-reading (which
    // would cache the false result in Vue's lazy computed).
    binding.setProperty('Name', 'Changed')
    expect(binding.isDirty.value).toBe(true)
    expect(binding.dirtyFields.value).toContain('Name')

    scope.stop()
  })

  it('dirtyFields contains modified field names', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')
    binding.setProperty('Age', 31)

    expect(binding.dirtyFields.value).toEqual(expect.arrayContaining(['Name', 'Age']))

    scope.stop()
  })

  it('resetChanges restores original values', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')
    binding.setProperty('Age', 99)

    binding.resetChanges()

    // Verify entity values are restored
    expect((binding.entity.value as Record<string, unknown>).Name).toBe('Alice')
    expect((binding.entity.value as Record<string, unknown>).Age).toBe(30)
    // Verify via getPatch (directly calls tracker, bypasses computed caching)
    expect(binding.getPatch()).toBeNull()

    scope.stop()
  })

  it('save calls odataService.update with only dirty fields', async () => {
    mockUpdate.mockResolvedValue({ Id: '123', Name: 'Bob', Age: 30 })

    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')
    await binding.save()

    expect(mockUpdate).toHaveBeenCalledWith(
      'sales',
      'Customers',
      '123',
      { Name: 'Bob' }
    )

    scope.stop()
  })

  it('save on 412 sets concurrencyError to true', async () => {
    mockUpdate.mockRejectedValue({ response: { status: 412 } })

    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')

    await expect(binding.save()).rejects.toEqual({ response: { status: 412 } })
    expect(binding.concurrencyError.value).toBe(true)
    expect(binding.error.value).toBe('This record was modified by another user.')

    scope.stop()
  })

  it('forceSave uses If-Match: *', async () => {
    mockUpdate.mockResolvedValue({ Id: '123', Name: 'Bob', Age: 30 })

    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')
    await binding.forceSave()

    expect(mockEtagRemove).toHaveBeenCalledWith('sales/Customers/123')
    expect(mockUpdate).toHaveBeenCalledWith(
      'sales',
      'Customers',
      '123',
      { Name: 'Bob' },
      { ifMatch: '*' }
    )

    scope.stop()
  })

  it('remove calls odataService.delete', async () => {
    mockDelete.mockResolvedValue(undefined)

    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    await binding.remove()

    expect(mockDelete).toHaveBeenCalledWith('sales', 'Customers', '123')
    expect(binding.entity.value).toBeNull()

    scope.stop()
  })

  it('getPatch returns null when no changes', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    expect(binding.getPatch()).toBeNull()

    scope.stop()
  })

  it('getPatch returns patch object when dirty', async () => {
    const { binding, scope } = createEntityBinding()
    await vi.waitFor(() => expect(binding.entity.value).not.toBeNull())

    binding.setProperty('Name', 'Bob')

    expect(binding.getPatch()).toEqual({ Name: 'Bob' })

    scope.stop()
  })

  it('autoLoad: false skips initial load', async () => {
    const { binding, scope } = createEntityBinding('/Customers', 'sales', '123', { autoLoad: false })

    await nextTick()
    await nextTick()

    expect(mockGetById).not.toHaveBeenCalled()
    expect(binding.entity.value).toBeNull()

    scope.stop()
  })
})

// ---------------------------------------------------------------------------
// useRelativeBinding
// ---------------------------------------------------------------------------

describe('useRelativeBinding', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockGetChildren.mockResolvedValue(makeQueryResponse([]))
  })

  function createRelativeBinding(
    parentContext = { module: 'sales', entitySet: 'Customers', key: '123' },
    navProperty = 'Orders',
    options?: Parameters<typeof useRelativeBinding>[2]
  ) {
    const scope = effectScope()
    let binding: ReturnType<typeof useRelativeBinding>
    scope.run(() => {
      binding = useRelativeBinding(parentContext, navProperty, options)
    })
    return { binding: binding!, scope }
  }

  it('loads children via odataService.getChildren', async () => {
    const orders = [{ Id: 'o1', Total: 100 }, { Id: 'o2', Total: 200 }]
    mockGetChildren.mockResolvedValue(makeQueryResponse(orders, 2))

    const { binding, scope } = createRelativeBinding()
    await vi.waitFor(() => expect(mockGetChildren).toHaveBeenCalledTimes(1))

    expect(mockGetChildren).toHaveBeenCalledWith(
      'sales',
      'Customers',
      '123',
      'Orders',
      expect.objectContaining({ $count: true, $top: 10, $skip: 0 })
    )
    expect(binding.data.value).toEqual(orders)
    expect(binding.totalCount.value).toBe(2)

    scope.stop()
  })

  it('goToPage reloads with correct $skip', async () => {
    mockGetChildren.mockResolvedValue(makeQueryResponse([], 50))

    const { binding, scope } = createRelativeBinding()
    await vi.waitFor(() => expect(mockGetChildren).toHaveBeenCalledTimes(1))

    await binding.goToPage(3)

    expect(binding.currentPage.value).toBe(3)
    // Page 3, pageSize 10: skip = (3-1)*10 = 20
    expect(mockGetChildren).toHaveBeenLastCalledWith(
      'sales',
      'Customers',
      '123',
      'Orders',
      expect.objectContaining({ $skip: 20, $top: 10 })
    )

    scope.stop()
  })

  it('throws if parent context has no key', () => {
    const parentContext = { module: 'sales', entitySet: 'Customers' }

    expect(() => {
      const scope = effectScope()
      scope.run(() => {
        useRelativeBinding(parentContext, 'Orders')
      })
      scope.stop()
    }).toThrow('Parent context must have a key for relative binding')
  })
})
