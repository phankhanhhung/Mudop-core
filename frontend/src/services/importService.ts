import api from './api'

export interface BulkImportRequest {
  records: Record<string, unknown>[]
  stopOnError?: boolean
}

export interface BulkImportError {
  rowIndex: number
  message: string
  data: Record<string, unknown>
}

export interface BulkImportResult {
  totalRecords: number
  successCount: number
  errorCount: number
  errors: BulkImportError[]
}

export default {
  async bulkImport(
    module: string,
    entitySet: string,
    request: BulkImportRequest
  ): Promise<BulkImportResult> {
    const { data } = await api.post<BulkImportResult>(
      `/odata/${module}/${entitySet}/$bulk-import`,
      request
    )
    return data
  },
}
