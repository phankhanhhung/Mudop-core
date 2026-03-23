import { describe, it, expect, vi, beforeEach } from 'vitest'

// ---------------------------------------------------------------------------
// Mocks — must be hoisted before any imports that trigger the module under test
// ---------------------------------------------------------------------------

vi.mock('@/services/api', () => ({
  tokenManager: { getAccessToken: vi.fn().mockReturnValue('test-token') },
}))

// Use vi.hoisted so mockAxiosInstance is available inside the vi.mock factory,
// which is itself hoisted to the top of the file by Vitest's transform.
const { mockAxiosInstance } = vi.hoisted(() => {
  const mockAxiosInstance = {
    get: vi.fn(),
    post: vi.fn(),
    interceptors: {
      request: { use: vi.fn() },
    },
  }
  return { mockAxiosInstance }
})

vi.mock('axios', () => ({
  default: {
    create: vi.fn().mockReturnValue(mockAxiosInstance),
  },
}))

// ---------------------------------------------------------------------------
// Import service AFTER mocks are registered
// ---------------------------------------------------------------------------

import { migrationService } from '../migrationService'
import type { BulkImportResult } from '../importService'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeBulkImportResult(overrides: Partial<BulkImportResult> = {}): BulkImportResult {
  return {
    totalRecords: 0,
    successCount: 0,
    errorCount: 0,
    errors: [],
    ...overrides,
  }
}

function makeRows(count: number): Record<string, unknown>[] {
  return Array.from({ length: count }, (_, i) => ({ Name: `Row ${i}`, Value: i }))
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('migrationService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    // Restore interceptors.request.use so the service module can re-add it without error
    mockAxiosInstance.interceptors.request.use = vi.fn()
  })

  // ── fetchFromTenant() ───────────────────────────────────────────────────

  describe('fetchFromTenant()', () => {
    it('makes GET request to /odata/{module}/{entityType}?$top={maxRows}', async () => {
      mockAxiosInstance.get.mockResolvedValue({ data: { value: [] } })
      await migrationService.fetchFromTenant('tenant-1', 'crm', 'Customer', 100)
      expect(mockAxiosInstance.get).toHaveBeenCalledWith(
        '/odata/crm/Customer?$top=100',
        expect.any(Object),
      )
    })

    it('passes X-Tenant-Id header in request config', async () => {
      mockAxiosInstance.get.mockResolvedValue({ data: { value: [] } })
      await migrationService.fetchFromTenant('tenant-abc', 'crm', 'Customer', 10)
      const callConfig = mockAxiosInstance.get.mock.calls[0][1] as Record<string, unknown>
      expect((callConfig.headers as Record<string, string>)['X-Tenant-Id']).toBe('tenant-abc')
    })

    it('returns the value array from the response data', async () => {
      const rows = [{ Name: 'Alice' }, { Name: 'Bob' }]
      mockAxiosInstance.get.mockResolvedValue({ data: { value: rows } })
      const result = await migrationService.fetchFromTenant('t1', 'crm', 'Customer', 50)
      expect(result).toEqual(rows)
    })

    it('uses default maxRows of 5000 when not specified', async () => {
      mockAxiosInstance.get.mockResolvedValue({ data: { value: [] } })
      await migrationService.fetchFromTenant('t1', 'crm', 'Customer')
      expect(mockAxiosInstance.get).toHaveBeenCalledWith(
        '/odata/crm/Customer?$top=5000',
        expect.any(Object),
      )
    })

    it('returns empty array when response has no value property (null/undefined)', async () => {
      mockAxiosInstance.get.mockResolvedValue({ data: {} })
      const result = await migrationService.fetchFromTenant('t1', 'crm', 'Customer')
      expect(result).toEqual([])
    })
  })

  // ── importToTenant() ────────────────────────────────────────────────────

  describe('importToTenant()', () => {
    it('makes POST to /odata/{module}/{entityType}/$bulk-import', async () => {
      mockAxiosInstance.post.mockResolvedValue({ data: makeBulkImportResult() })
      await migrationService.importToTenant('t1', 'crm', 'Customer', [])
      expect(mockAxiosInstance.post).toHaveBeenCalledWith(
        '/odata/crm/Customer/$bulk-import',
        expect.any(Object),
        expect.any(Object),
      )
    })

    it('passes X-Tenant-Id header', async () => {
      mockAxiosInstance.post.mockResolvedValue({ data: makeBulkImportResult() })
      await migrationService.importToTenant('tenant-xyz', 'crm', 'Customer', [])
      const callConfig = mockAxiosInstance.post.mock.calls[0][2] as Record<string, unknown>
      expect((callConfig.headers as Record<string, string>)['X-Tenant-Id']).toBe('tenant-xyz')
    })

    it('passes { records: cleanRecords, stopOnError: false } as body', async () => {
      mockAxiosInstance.post.mockResolvedValue({ data: makeBulkImportResult() })
      const records = [{ Name: 'Alice' }]
      await migrationService.importToTenant('t1', 'crm', 'Customer', records)
      const body = mockAxiosInstance.post.mock.calls[0][1] as Record<string, unknown>
      expect(body.stopOnError).toBe(false)
      expect(Array.isArray(body.records)).toBe(true)
    })

    it('strips system fields: ID, @odata.etag, TenantId, CreatedAt, ModifiedAt, SystemStart, SystemEnd, Version', async () => {
      mockAxiosInstance.post.mockResolvedValue({ data: makeBulkImportResult() })
      const dirtyRecord: Record<string, unknown> = {
        ID: 'some-uuid',
        '@odata.etag': 'W/"xyz"',
        TenantId: 'tenant-1',
        CreatedAt: '2024-01-01',
        ModifiedAt: '2024-01-02',
        SystemStart: '2024-01-01T00:00:00Z',
        SystemEnd: '9999-12-31T00:00:00Z',
        Version: 1,
        Name: 'Alice',
        Email: 'alice@example.com',
      }
      await migrationService.importToTenant('t1', 'crm', 'Customer', [dirtyRecord])
      const body = mockAxiosInstance.post.mock.calls[0][1] as { records: Record<string, unknown>[] }
      const cleanedRecord = body.records[0]
      expect(cleanedRecord).not.toHaveProperty('ID')
      expect(cleanedRecord).not.toHaveProperty('@odata.etag')
      expect(cleanedRecord).not.toHaveProperty('TenantId')
      expect(cleanedRecord).not.toHaveProperty('CreatedAt')
      expect(cleanedRecord).not.toHaveProperty('ModifiedAt')
      expect(cleanedRecord).not.toHaveProperty('SystemStart')
      expect(cleanedRecord).not.toHaveProperty('SystemEnd')
      expect(cleanedRecord).not.toHaveProperty('Version')
      expect(cleanedRecord).toHaveProperty('Name', 'Alice')
      expect(cleanedRecord).toHaveProperty('Email', 'alice@example.com')
    })

    it('returns the response data as BulkImportResult', async () => {
      const importResult = makeBulkImportResult({ totalRecords: 3, successCount: 3, errorCount: 0 })
      mockAxiosInstance.post.mockResolvedValue({ data: importResult })
      const result = await migrationService.importToTenant('t1', 'crm', 'Customer', [])
      expect(result).toEqual(importResult)
    })
  })

  // ── migrate() ───────────────────────────────────────────────────────────

  describe('migrate()', () => {
    it('returns array of MigrationEntityResult per entity', async () => {
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue([])
      const results = await migrationService.migrate('src', 'tgt', 'crm', ['Customer', 'Order'])
      expect(results).toHaveLength(2)
      expect(results[0]).toMatchObject({ entityType: 'Customer' })
      expect(results[1]).toMatchObject({ entityType: 'Order' })
      fetchSpy.mockRestore()
    })

    it('calls fetchFromTenant for each entityType with sourceTenantId', async () => {
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue([])
      await migrationService.migrate('source-tenant', 'target-tenant', 'sales', ['Product', 'Invoice'])
      expect(fetchSpy).toHaveBeenCalledWith('source-tenant', 'sales', 'Product', 5000)
      expect(fetchSpy).toHaveBeenCalledWith('source-tenant', 'sales', 'Invoice', 5000)
      fetchSpy.mockRestore()
    })

    it('calls importToTenant for each entityType with targetTenantId', async () => {
      const rows = makeRows(2)
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue(rows)
      const importSpy = vi
        .spyOn(migrationService, 'importToTenant')
        .mockResolvedValue(makeBulkImportResult({ successCount: 2 }))
      await migrationService.migrate('src', 'target-tenant', 'crm', ['Customer'])
      expect(importSpy).toHaveBeenCalledWith('target-tenant', 'crm', 'Customer', expect.any(Array))
      fetchSpy.mockRestore()
      importSpy.mockRestore()
    })

    it('reports progress via onProgress callback for each entity', async () => {
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue([])
      const onProgress = vi.fn()
      await migrationService.migrate('src', 'tgt', 'crm', ['Customer', 'Order'], onProgress)
      expect(onProgress).toHaveBeenCalledTimes(2)
      expect(onProgress).toHaveBeenNthCalledWith(
        1,
        expect.objectContaining({ currentEntity: 'Customer', entitiesCompleted: 0, totalEntities: 2 }),
      )
      expect(onProgress).toHaveBeenNthCalledWith(
        2,
        expect.objectContaining({ currentEntity: 'Order', entitiesCompleted: 1, totalEntities: 2 }),
      )
      fetchSpy.mockRestore()
    })

    it('handles empty rows — pushes { entityType, rowsCopied: 0, errors: 0 }', async () => {
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue([])
      const results = await migrationService.migrate('src', 'tgt', 'crm', ['Customer'])
      expect(results[0]).toEqual({ entityType: 'Customer', rowsCopied: 0, errors: 0 })
      fetchSpy.mockRestore()
    })

    it('chunks large result sets (> 50 rows) — importToTenant called 3 times for 110 rows', async () => {
      const rows = makeRows(110)
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue(rows)
      const importSpy = vi
        .spyOn(migrationService, 'importToTenant')
        .mockResolvedValue(makeBulkImportResult({ successCount: 50 }))
      await migrationService.migrate('src', 'tgt', 'crm', ['Customer'])
      // 110 rows / 50 chunk size = 3 calls (50 + 50 + 10)
      expect(importSpy).toHaveBeenCalledTimes(3)
      const call1Rows = importSpy.mock.calls[0][3] as Record<string, unknown>[]
      const call2Rows = importSpy.mock.calls[1][3] as Record<string, unknown>[]
      const call3Rows = importSpy.mock.calls[2][3] as Record<string, unknown>[]
      expect(call1Rows).toHaveLength(50)
      expect(call2Rows).toHaveLength(50)
      expect(call3Rows).toHaveLength(10)
      fetchSpy.mockRestore()
      importSpy.mockRestore()
    })

    it('handles fetch failure gracefully — pushes { entityType, rowsCopied: 0, errors: -1 }', async () => {
      const fetchSpy = vi
        .spyOn(migrationService, 'fetchFromTenant')
        .mockRejectedValue(new Error('Network error'))
      const results = await migrationService.migrate('src', 'tgt', 'crm', ['Customer'])
      expect(results[0]).toEqual({ entityType: 'Customer', rowsCopied: 0, errors: -1 })
      fetchSpy.mockRestore()
    })

    it('accumulates rowsCopied and errors from import results', async () => {
      const rows = makeRows(10)
      const fetchSpy = vi.spyOn(migrationService, 'fetchFromTenant').mockResolvedValue(rows)
      const importSpy = vi
        .spyOn(migrationService, 'importToTenant')
        .mockResolvedValue(makeBulkImportResult({ successCount: 8, errorCount: 2 }))
      const results = await migrationService.migrate('src', 'tgt', 'crm', ['Customer'])
      expect(results[0]).toMatchObject({ entityType: 'Customer', rowsCopied: 8, errors: 2 })
      fetchSpy.mockRestore()
      importSpy.mockRestore()
    })
  })
})
