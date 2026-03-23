import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'

const mockGetVersions = vi.fn()

vi.mock('@/services/odataService', () => ({
  odataService: {
    getVersions: (...args: unknown[]) => mockGetVersions(...args),
  },
}))

import { useTemporalBinding } from '../useTemporalBinding'
import type { UseTemporalBindingReturn } from '../useTemporalBinding'

describe('useTemporalBinding', () => {
  let temporal: UseTemporalBindingReturn
  let scope: ReturnType<typeof effectScope>

  beforeEach(() => {
    vi.clearAllMocks()
    scope = effectScope()

    scope.run(() => {
      temporal = useTemporalBinding({
        module: 'myapp',
        entitySet: 'Prices',
        key: '123',
      })
    })
  })

  afterEach(() => {
    scope.stop()
  })

  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('asOf is null', () => {
      expect(temporal.asOf.value).toBeNull()
    })

    it('validAt is null', () => {
      expect(temporal.validAt.value).toBeNull()
    })

    it('includeHistory is false', () => {
      expect(temporal.includeHistory.value).toBe(false)
    })

    it('isActive is false', () => {
      expect(temporal.isActive.value).toBe(false)
    })

    it('versions is empty', () => {
      expect(temporal.versions.value).toEqual([])
    })

    it('versionCount is 0', () => {
      expect(temporal.versionCount.value).toBe(0)
    })

    it('isLoadingVersions is false', () => {
      expect(temporal.isLoadingVersions.value).toBe(false)
    })
  })

  // =========================================================================
  // timeTravel
  // =========================================================================

  describe('timeTravel()', () => {
    it('sets asOf, isActive becomes true', () => {
      temporal.timeTravel('2025-06-15T00:00:00Z')

      expect(temporal.asOf.value).toBe('2025-06-15T00:00:00Z')
      expect(temporal.isActive.value).toBe(true)
    })
  })

  // =========================================================================
  // setValidTime
  // =========================================================================

  describe('setValidTime()', () => {
    it('sets validAt', () => {
      temporal.setValidTime('2025-01-01')

      expect(temporal.validAt.value).toBe('2025-01-01')
      expect(temporal.isActive.value).toBe(true)
    })
  })

  // =========================================================================
  // showHistory / hideHistory
  // =========================================================================

  describe('showHistory() / hideHistory()', () => {
    it('showHistory sets includeHistory true', () => {
      temporal.showHistory()

      expect(temporal.includeHistory.value).toBe(true)
      expect(temporal.isActive.value).toBe(true)
    })

    it('hideHistory sets includeHistory false', () => {
      temporal.showHistory()
      temporal.hideHistory()

      expect(temporal.includeHistory.value).toBe(false)
    })
  })

  // =========================================================================
  // resetTemporal
  // =========================================================================

  describe('resetTemporal()', () => {
    it('clears all temporal state', () => {
      temporal.timeTravel('2025-06-15T00:00:00Z')
      temporal.setValidTime('2025-01-01')
      temporal.showHistory()

      expect(temporal.isActive.value).toBe(true)

      temporal.resetTemporal()

      expect(temporal.asOf.value).toBeNull()
      expect(temporal.validAt.value).toBeNull()
      expect(temporal.includeHistory.value).toBe(false)
      expect(temporal.isActive.value).toBe(false)
      expect(temporal.versions.value).toEqual([])
    })
  })

  // =========================================================================
  // loadVersions
  // =========================================================================

  describe('loadVersions()', () => {
    it('fetches and maps version entries', async () => {
      mockGetVersions.mockResolvedValueOnce([
        {
          Id: '1',
          price: 10.0,
          system_start: '2025-01-01T00:00:00Z',
          system_end: '2025-06-01T00:00:00Z',
          effectiveFrom: '2025-01-01',
          effectiveTo: '2025-12-31',
          version: 1,
        },
        {
          Id: '1',
          price: 15.0,
          system_start: '2025-06-01T00:00:00Z',
          system_end: '9999-12-31T23:59:59Z',
          effectiveFrom: '2025-06-01',
          effectiveTo: '2026-12-31',
          version: 2,
        },
      ])

      await temporal.loadVersions()

      expect(mockGetVersions).toHaveBeenCalledWith('myapp', 'Prices', '123')
      expect(temporal.versions.value).toHaveLength(2)
      expect(temporal.versionCount.value).toBe(2)

      const v1 = temporal.versions.value[0]
      expect(v1.systemStart).toBe('2025-01-01T00:00:00Z')
      expect(v1.systemEnd).toBe('2025-06-01T00:00:00Z')
      expect(v1.validFrom).toBe('2025-01-01')
      expect(v1.validTo).toBe('2025-12-31')
      expect(v1.version).toBe(1)
      expect(v1.data.price).toBe(10.0)
    })

    it('with no key returns empty array', async () => {
      scope.stop()
      scope = effectScope()

      let noKeyTemporal!: UseTemporalBindingReturn
      scope.run(() => {
        noKeyTemporal = useTemporalBinding({
          module: 'myapp',
          entitySet: 'Prices',
          // no key
        })
      })

      await noKeyTemporal.loadVersions()

      expect(noKeyTemporal.versions.value).toEqual([])
      expect(mockGetVersions).not.toHaveBeenCalled()
    })

    it('handles errors gracefully (sets versions to empty)', async () => {
      mockGetVersions.mockRejectedValueOnce(new Error('Network error'))

      await temporal.loadVersions()

      expect(temporal.versions.value).toEqual([])
      expect(temporal.isLoadingVersions.value).toBe(false)
    })

    it('sets isLoadingVersions during fetch', async () => {
      let resolvePromise: (val: unknown[]) => void
      const promise = new Promise<unknown[]>((resolve) => { resolvePromise = resolve })
      mockGetVersions.mockReturnValueOnce(promise)

      const loadPromise = temporal.loadVersions()
      expect(temporal.isLoadingVersions.value).toBe(true)

      resolvePromise!([])
      await loadPromise

      expect(temporal.isLoadingVersions.value).toBe(false)
    })

    it('uses valid_from/valid_to fallbacks when effectiveFrom/To not available', async () => {
      mockGetVersions.mockResolvedValueOnce([
        {
          Id: '1',
          price: 10.0,
          system_start: '2025-01-01T00:00:00Z',
          system_end: '2025-06-01T00:00:00Z',
          valid_from: '2025-01-01',
          valid_to: '2025-12-31',
        },
      ])

      await temporal.loadVersions()

      const v = temporal.versions.value[0]
      expect(v.validFrom).toBe('2025-01-01')
      expect(v.validTo).toBe('2025-12-31')
      // version should fallback to index + 1
      expect(v.version).toBe(1)
    })
  })

  // =========================================================================
  // compareVersions
  // =========================================================================

  describe('compareVersions()', () => {
    beforeEach(async () => {
      mockGetVersions.mockResolvedValueOnce([
        {
          Id: '1',
          price: 10.0,
          name: 'Product A',
          status: 'Active',
          system_start: '2025-01-01T00:00:00Z',
          system_end: '2025-06-01T00:00:00Z',
          version: 1,
          '@odata.etag': 'W/"1"',
          _internal: 'meta',
        },
        {
          Id: '1',
          price: 15.0,
          name: 'Product A',
          status: 'Inactive',
          system_start: '2025-06-01T00:00:00Z',
          system_end: '9999-12-31T23:59:59Z',
          version: 2,
          '@odata.etag': 'W/"2"',
          _internal: 'meta2',
        },
      ])

      await temporal.loadVersions()
    })

    it('returns field-level diffs', () => {
      const diffs = temporal.compareVersions(0, 1)

      // price and status differ; name is the same
      const fields = diffs.map(d => d.field)
      expect(fields).toContain('price')
      expect(fields).toContain('status')
      expect(fields).not.toContain('name')
    })

    it('skips metadata fields (@, _, system_start, etc.)', () => {
      const diffs = temporal.compareVersions(0, 1)

      const fields = diffs.map(d => d.field)
      expect(fields).not.toContain('@odata.etag')
      expect(fields).not.toContain('_internal')
      expect(fields).not.toContain('system_start')
      expect(fields).not.toContain('system_end')
      expect(fields).not.toContain('version')
    })

    it('returns empty array for out-of-bounds indices', () => {
      expect(temporal.compareVersions(0, 99)).toEqual([])
      expect(temporal.compareVersions(-1, 0)).toEqual([])
    })

    it('returns correct versionA/versionB values', () => {
      const diffs = temporal.compareVersions(0, 1)

      const priceDiff = diffs.find(d => d.field === 'price')!
      expect(priceDiff.versionA).toBe(10.0)
      expect(priceDiff.versionB).toBe(15.0)
    })
  })

  // =========================================================================
  // getQueryParams
  // =========================================================================

  describe('getQueryParams()', () => {
    it('returns non-null temporal params as record', () => {
      temporal.timeTravel('2025-06-15T00:00:00Z')
      temporal.setValidTime('2025-01-01')
      temporal.showHistory()

      const params = temporal.getQueryParams()

      expect(params).toEqual({
        asOf: '2025-06-15T00:00:00Z',
        validAt: '2025-01-01',
        includeHistory: 'true',
      })
    })

    it('returns empty object when no temporal params set', () => {
      const params = temporal.getQueryParams()
      expect(params).toEqual({})
    })

    it('only includes set params', () => {
      temporal.timeTravel('2025-06-15T00:00:00Z')

      const params = temporal.getQueryParams()
      expect(params).toEqual({ asOf: '2025-06-15T00:00:00Z' })
      expect(params).not.toHaveProperty('validAt')
      expect(params).not.toHaveProperty('includeHistory')
    })
  })

  // =========================================================================
  // getODataOptions
  // =========================================================================

  describe('getODataOptions()', () => {
    it('returns OData-compatible options', () => {
      temporal.timeTravel('2025-06-15T00:00:00Z')
      temporal.showHistory()

      const opts = temporal.getODataOptions()

      expect(opts.asOf).toBe('2025-06-15T00:00:00Z')
      expect(opts.includeHistory).toBe(true)
    })

    it('returns empty object when no params set', () => {
      const opts = temporal.getODataOptions()
      expect(opts).toEqual({})
    })
  })

  // =========================================================================
  // onParamsChange callback
  // =========================================================================

  describe('onParamsChange callback', () => {
    it('called when params change', async () => {
      scope.stop()
      scope = effectScope()

      const onParamsChange = vi.fn()

      scope.run(() => {
        temporal = useTemporalBinding({
          module: 'myapp',
          entitySet: 'Prices',
          key: '123',
          onParamsChange,
        })
      })

      temporal.timeTravel('2025-06-15T00:00:00Z')

      // watch is triggered asynchronously
      await nextTick()

      expect(onParamsChange).toHaveBeenCalled()
      const callArg = onParamsChange.mock.calls[0][0]
      expect(callArg.asOf).toBe('2025-06-15T00:00:00Z')
    })

    it('called when includeHistory changes', async () => {
      scope.stop()
      scope = effectScope()

      const onParamsChange = vi.fn()

      scope.run(() => {
        temporal = useTemporalBinding({
          module: 'myapp',
          entitySet: 'Prices',
          key: '123',
          onParamsChange,
        })
      })

      temporal.showHistory()

      await nextTick()

      expect(onParamsChange).toHaveBeenCalled()
      const callArg = onParamsChange.mock.calls[0][0]
      expect(callArg.includeHistory).toBe(true)
    })

    it('called when validAt changes', async () => {
      scope.stop()
      scope = effectScope()

      const onParamsChange = vi.fn()

      scope.run(() => {
        temporal = useTemporalBinding({
          module: 'myapp',
          entitySet: 'Prices',
          key: '123',
          onParamsChange,
        })
      })

      temporal.setValidTime('2025-01-01')

      await nextTick()

      expect(onParamsChange).toHaveBeenCalled()
      const callArg = onParamsChange.mock.calls[0][0]
      expect(callArg.validAt).toBe('2025-01-01')
    })
  })
})
