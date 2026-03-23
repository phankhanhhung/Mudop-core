import { describe, it, expect, beforeEach, vi } from 'vitest'
// vue types used indirectly

// Mock odataService and etagStore before importing ETagManager
const mockGetById = vi.fn()
const mockUpdate = vi.fn()

const mockEtagMap = new Map<string, string>()
const mockEtagStore = {
  get: (key: string) => mockEtagMap.get(key),
  set: (key: string, etag: string) => mockEtagMap.set(key, etag),
  remove: (key: string) => mockEtagMap.delete(key),
  clear: () => mockEtagMap.clear(),
}

vi.mock('@/services/odataService', () => ({
  odataService: {
    getById: (...args: unknown[]) => mockGetById(...args),
    update: (...args: unknown[]) => mockUpdate(...args),
  },
  etagStore: {
    get: (key: string) => mockEtagStore.get(key),
    set: (key: string, etag: string) => mockEtagStore.set(key, etag),
    remove: (key: string) => mockEtagStore.remove(key),
    clear: () => mockEtagStore.clear(),
  },
}))

import { ETagManager } from '../ETagManager'
import type { ConflictInfo } from '../ETagManager'

function make412Error(): Error & { response: { status: number } } {
  const err = new Error('Precondition Failed') as Error & { response: { status: number } }
  err.response = { status: 412 }
  return err
}

function make500Error(): Error & { response: { status: number } } {
  const err = new Error('Internal Server Error') as Error & { response: { status: number } }
  err.response = { status: 500 }
  return err
}

describe('ETagManager', () => {
  let manager: ETagManager

  beforeEach(() => {
    vi.clearAllMocks()
    mockEtagMap.clear()
    manager = new ETagManager('testModule')
  })

  // =========================================================================
  // Constructor
  // =========================================================================

  describe('constructor', () => {
    it('sets default config values', () => {
      // By default, hasConflict is false and conflictInfo is null
      expect(manager.hasConflict.value).toBe(false)
      expect(manager.conflictInfo.value).toBeNull()
    })
  })

  // =========================================================================
  // updateWithRetry — success on first try
  // =========================================================================

  describe('updateWithRetry()', () => {
    it('succeeds on first try and returns result', async () => {
      const expectedResult = { Id: '1', name: 'Updated' }
      mockUpdate.mockResolvedValueOnce(expectedResult)

      const result = await manager.updateWithRetry('Customers', '1', { name: 'Updated' })

      expect(result).toEqual(expectedResult)
      expect(mockUpdate).toHaveBeenCalledTimes(1)
      expect(mockUpdate).toHaveBeenCalledWith('testModule', 'Customers', '1', { name: 'Updated' })
    })

    it('clears conflict state on each attempt', async () => {
      mockUpdate.mockResolvedValueOnce({ Id: '1' })

      // Manually set conflict state
      manager.hasConflict.value = true
      manager.conflictInfo.value = {} as ConflictInfo

      await manager.updateWithRetry('Customers', '1', { name: 'Test' })

      expect(manager.hasConflict.value).toBe(false)
      expect(manager.conflictInfo.value).toBeNull()
    })

    // -----------------------------------------------------------------------
    // 412 with 'fail' strategy
    // -----------------------------------------------------------------------

    it('412 with fail strategy throws immediately', async () => {
      const error412 = make412Error()
      mockUpdate.mockRejectedValue(error412)

      await expect(
        manager.updateWithRetry('Customers', '1', { name: 'x' }, 'fail')
      ).rejects.toThrow('Precondition Failed')

      expect(mockUpdate).toHaveBeenCalledTimes(1)
      expect(manager.hasConflict.value).toBe(true)
    })

    // -----------------------------------------------------------------------
    // 412 with 'refresh-retry' strategy
    // -----------------------------------------------------------------------

    it('412 with refresh-retry fetches latest, retries with fresh ETag, succeeds', async () => {
      const error412 = make412Error()
      const freshResult = { Id: '1', name: 'Refreshed' }

      // First call: 412, second call: success
      mockUpdate
        .mockRejectedValueOnce(error412)
        .mockResolvedValueOnce(freshResult)

      // getById is called to refresh the entity and get a fresh ETag
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerVersion' })

      const result = await manager.updateWithRetry('Customers', '1', { name: 'Updated' }, 'refresh-retry')

      expect(result).toEqual(freshResult)
      expect(mockGetById).toHaveBeenCalledTimes(1)
      expect(mockGetById).toHaveBeenCalledWith('testModule', 'Customers', '1')
      expect(mockUpdate).toHaveBeenCalledTimes(2)
    })

    it('412 with refresh-retry throws after exhausting retries', async () => {
      const error412 = make412Error()
      mockUpdate.mockRejectedValue(error412)
      mockGetById.mockResolvedValue({ Id: '1', name: 'ServerVersion' })

      const mgr = new ETagManager('testModule', { defaultStrategy: 'refresh-retry', maxRetries: 1 })

      await expect(
        mgr.updateWithRetry('Customers', '1', { name: 'x' })
      ).rejects.toThrow()

      expect(mgr.hasConflict.value).toBe(true)
    })

    // -----------------------------------------------------------------------
    // 412 with 'force-overwrite' strategy
    // -----------------------------------------------------------------------

    it('412 with force-overwrite retries with If-Match: *', async () => {
      const error412 = make412Error()
      const forcedResult = { Id: '1', name: 'Forced' }

      mockUpdate
        .mockRejectedValueOnce(error412)
        .mockResolvedValueOnce(forcedResult)

      const result = await manager.updateWithRetry('Customers', '1', { name: 'Forced' }, 'force-overwrite')

      expect(result).toEqual(forcedResult)
      // forceUpdate calls odataService.update with { ifMatch: '*' }
      expect(mockUpdate).toHaveBeenCalledTimes(2)
      expect(mockUpdate).toHaveBeenLastCalledWith(
        'testModule', 'Customers', '1', { name: 'Forced' }, { ifMatch: '*' }
      )
    })

    // -----------------------------------------------------------------------
    // 412 with 'manual' strategy
    // -----------------------------------------------------------------------

    it('412 with manual strategy calls onConflict callback', async () => {
      const error412 = make412Error()
      const retriedResult = { Id: '1', name: 'ManualRetry' }

      mockUpdate
        .mockRejectedValueOnce(error412)
        .mockResolvedValueOnce(retriedResult)

      // getById is called during buildConflictInfo
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerLatest', status: 'Active' })
      mockEtagMap.set('testModule/Customers/1', 'W/"fresh-etag"')

      const onConflict = vi.fn().mockResolvedValueOnce('retry' as const)

      const mgr = new ETagManager('testModule', {
        defaultStrategy: 'manual',
        onConflict,
      })

      const result = await mgr.updateWithRetry('Customers', '1', { name: 'ClientChange' })

      expect(onConflict).toHaveBeenCalledTimes(1)
      const conflictArg: ConflictInfo = onConflict.mock.calls[0][0]
      expect(conflictArg.entitySet).toBe('Customers')
      expect(conflictArg.key).toBe('1')
      expect(conflictArg.clientData).toEqual({ name: 'ClientChange' })
      expect(conflictArg.serverData).toEqual({ Id: '1', name: 'ServerLatest', status: 'Active' })
      expect(conflictArg.serverEtag).toBe('W/"fresh-etag"')
      expect(result).toEqual(retriedResult)
    })

    it('412 with manual strategy and force decision uses If-Match: *', async () => {
      const error412 = make412Error()
      const forcedResult = { Id: '1', name: 'Forced' }

      mockUpdate
        .mockRejectedValueOnce(error412)
        .mockResolvedValueOnce(forcedResult)

      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerLatest' })

      const onConflict = vi.fn().mockResolvedValueOnce('force' as const)

      const mgr = new ETagManager('testModule', {
        defaultStrategy: 'manual',
        onConflict,
      })

      const result = await mgr.updateWithRetry('Customers', '1', { name: 'x' })

      expect(result).toEqual(forcedResult)
      expect(mockUpdate).toHaveBeenLastCalledWith(
        'testModule', 'Customers', '1', { name: 'x' }, { ifMatch: '*' }
      )
    })

    it('412 with manual strategy and cancel decision throws', async () => {
      const error412 = make412Error()

      mockUpdate.mockRejectedValue(error412)
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerLatest' })

      const onConflict = vi.fn().mockResolvedValueOnce('cancel' as const)

      const mgr = new ETagManager('testModule', {
        defaultStrategy: 'manual',
        onConflict,
      })

      await expect(
        mgr.updateWithRetry('Customers', '1', { name: 'x' })
      ).rejects.toThrow('Precondition Failed')
    })

    // -----------------------------------------------------------------------
    // hasConflict and conflictInfo
    // -----------------------------------------------------------------------

    it('hasConflict ref set to true on conflict (manual mode)', async () => {
      const error412 = make412Error()
      mockUpdate.mockRejectedValue(error412)
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'Server' })

      const onConflict = vi.fn().mockResolvedValueOnce('cancel' as const)
      const mgr = new ETagManager('testModule', { defaultStrategy: 'manual', onConflict })

      await mgr.updateWithRetry('Customers', '1', { name: 'Client' }).catch(() => {})

      expect(mgr.hasConflict.value).toBe(true)
    })

    it('conflictInfo populated with client/server data diff', async () => {
      const error412 = make412Error()
      mockUpdate.mockRejectedValue(error412)
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerName', email: 'server@test.com' })
      mockEtagMap.set('testModule/Customers/1', 'W/"etag-2"')

      const onConflict = vi.fn().mockResolvedValueOnce('cancel' as const)
      const mgr = new ETagManager('testModule', { defaultStrategy: 'manual', onConflict })

      await mgr.updateWithRetry('Customers', '1', { name: 'ClientName', email: 'server@test.com' }).catch(() => {})

      expect(mgr.conflictInfo.value).not.toBeNull()
      expect(mgr.conflictInfo.value!.conflictingFields).toContain('name')
      expect(mgr.conflictInfo.value!.conflictingFields).not.toContain('email')
      expect(mgr.conflictInfo.value!.serverEtag).toBe('W/"etag-2"')
    })

    // -----------------------------------------------------------------------
    // Non-412 errors
    // -----------------------------------------------------------------------

    it('non-412 errors thrown as-is (not retried)', async () => {
      const error500 = make500Error()
      mockUpdate.mockRejectedValue(error500)

      await expect(
        manager.updateWithRetry('Customers', '1', { name: 'x' })
      ).rejects.toThrow('Internal Server Error')

      expect(mockUpdate).toHaveBeenCalledTimes(1)
      expect(manager.hasConflict.value).toBe(false)
    })

    it('non-axios errors (no response property) are thrown as-is', async () => {
      const plainError = new Error('Network error')
      mockUpdate.mockRejectedValue(plainError)

      await expect(
        manager.updateWithRetry('Customers', '1', { name: 'x' })
      ).rejects.toThrow('Network error')

      expect(mockUpdate).toHaveBeenCalledTimes(1)
    })
  })

  // =========================================================================
  // forceUpdate
  // =========================================================================

  describe('forceUpdate()', () => {
    it('calls update with ifMatch: * and removes cached etag', async () => {
      mockEtagMap.set('testModule/Customers/1', 'W/"old-etag"')
      mockUpdate.mockResolvedValueOnce({ Id: '1', name: 'Forced' })

      const result = await manager.forceUpdate('Customers', '1', { name: 'Forced' })

      expect(result).toEqual({ Id: '1', name: 'Forced' })
      expect(mockEtagMap.has('testModule/Customers/1')).toBe(false)
      expect(mockUpdate).toHaveBeenCalledWith(
        'testModule', 'Customers', '1', { name: 'Forced' }, { ifMatch: '*' }
      )
    })
  })

  // =========================================================================
  // getDiff
  // =========================================================================

  describe('getDiff()', () => {
    it('returns field-level diffs between client and server data', async () => {
      mockGetById.mockResolvedValueOnce({ Id: '1', name: 'ServerName', age: 30, status: 'Active' })

      const diffs = await manager.getDiff('Customers', '1', { name: 'ClientName', age: 30, status: 'Active' })

      expect(diffs).toHaveLength(1)
      expect(diffs[0]).toEqual({ field: 'name', clientValue: 'ClientName', serverValue: 'ServerName' })
    })

    it('skips @-prefixed and _-prefixed metadata fields', async () => {
      mockGetById.mockResolvedValueOnce({ Id: '1', '@odata.etag': 'W/"2"', _internal: 'y' })

      const diffs = await manager.getDiff('Customers', '1', { '@odata.etag': 'W/"1"', _internal: 'x' })

      expect(diffs).toHaveLength(0)
    })
  })

  // =========================================================================
  // clearConflict
  // =========================================================================

  describe('clearConflict()', () => {
    it('resets hasConflict and conflictInfo', () => {
      manager.hasConflict.value = true
      manager.conflictInfo.value = { entitySet: 'X' } as ConflictInfo

      manager.clearConflict()

      expect(manager.hasConflict.value).toBe(false)
      expect(manager.conflictInfo.value).toBeNull()
    })
  })
})
