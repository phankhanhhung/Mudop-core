import axios from 'axios'
import { tokenManager } from './api'
import type { BulkImportResult } from './importService'
import type { ODataResponse } from '@/types/odata'

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

// Create a dedicated axios instance for migration that doesn't use the global tenant interceptor
const migrationApi = axios.create({
  baseURL: BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 60000,
})

// Add auth interceptor (no tenant header — callers pass it explicitly per request)
migrationApi.interceptors.request.use((config) => {
  const token = tokenManager.getAccessToken()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

export interface MigrationEntityResult {
  entityType: string
  rowsCopied: number
  errors: number
}

export interface MigrationProgress {
  currentEntity: string
  entitiesCompleted: number
  totalEntities: number
  rowsCopied: number
}

export type MigrationProgressCallback = (progress: MigrationProgress) => void

const SYSTEM_FIELDS = [
  'ID',
  '@odata.etag',
  'TenantId',
  'CreatedAt',
  'ModifiedAt',
  'SystemStart',
  'SystemEnd',
  'Version',
]

export const migrationService = {
  /**
   * Fetch all rows from a source tenant entity.
   * Uses explicit X-Tenant-Id header to override the global tenant.
   */
  async fetchFromTenant(
    tenantId: string,
    module: string,
    entityType: string,
    maxRows: number = 5000,
  ): Promise<Record<string, unknown>[]> {
    const url = `/odata/${module}/${entityType}?$top=${maxRows}`
    const response = await migrationApi.get<ODataResponse<Record<string, unknown>>>(url, {
      headers: { 'X-Tenant-Id': tenantId },
    })
    return response.data.value ?? []
  },

  /**
   * Bulk-import rows into a target tenant entity.
   * Uses explicit X-Tenant-Id header to override the global tenant.
   */
  async importToTenant(
    tenantId: string,
    module: string,
    entityType: string,
    records: Record<string, unknown>[],
  ): Promise<BulkImportResult> {
    // Strip system/read-only fields from records before import
    const cleanRecords = records.map((r) => {
      const cleaned: Record<string, unknown> = {}
      for (const [k, v] of Object.entries(r)) {
        if (!SYSTEM_FIELDS.includes(k)) {
          cleaned[k] = v
        }
      }
      return cleaned
    })

    const response = await migrationApi.post<BulkImportResult>(
      `/odata/${module}/${entityType}/$bulk-import`,
      { records: cleanRecords, stopOnError: false },
      { headers: { 'X-Tenant-Id': tenantId } },
    )
    return response.data
  },

  /**
   * Migrate entities from source tenant to target tenant.
   * Reports progress via callback.
   */
  async migrate(
    sourceTenantId: string,
    targetTenantId: string,
    module: string,
    entityTypes: string[],
    onProgress?: MigrationProgressCallback,
    maxRowsPerEntity: number = 5000,
  ): Promise<MigrationEntityResult[]> {
    const results: MigrationEntityResult[] = []
    let totalRowsCopied = 0

    for (let i = 0; i < entityTypes.length; i++) {
      const entityType = entityTypes[i]

      onProgress?.({
        currentEntity: entityType,
        entitiesCompleted: i,
        totalEntities: entityTypes.length,
        rowsCopied: totalRowsCopied,
      })

      try {
        const rows = await this.fetchFromTenant(sourceTenantId, module, entityType, maxRowsPerEntity)

        if (rows.length === 0) {
          results.push({ entityType, rowsCopied: 0, errors: 0 })
          continue
        }

        // Import in chunks of 50
        const CHUNK_SIZE = 50
        let rowsCopied = 0
        let errors = 0

        for (let j = 0; j < rows.length; j += CHUNK_SIZE) {
          const chunk = rows.slice(j, j + CHUNK_SIZE)
          try {
            const importResult = await this.importToTenant(
              targetTenantId,
              module,
              entityType,
              chunk,
            )
            rowsCopied += importResult.successCount
            errors += importResult.errorCount
          } catch {
            errors += chunk.length
          }
        }

        totalRowsCopied += rowsCopied
        results.push({ entityType, rowsCopied, errors })
      } catch {
        results.push({
          entityType,
          rowsCopied: 0,
          errors: -1, // -1 = failed to fetch
        })
      }
    }

    return results
  },
}

export default migrationService
