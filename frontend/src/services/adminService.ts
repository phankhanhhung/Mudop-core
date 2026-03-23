import api from './api'

export interface CompileRequest {
  bmmdlSource: string
  moduleName: string
  tenantId?: string | null
  publish?: boolean
  initSchema?: boolean
  force?: boolean
}

export interface CompileResponse {
  success: boolean
  entityCount: number
  serviceCount: number
  typeCount: number
  enumCount: number
  compilationTime: string
  errors: string[]
  warnings: string[]
  schemaResult?: string
  versionInfo?: {
    version: string
    changeCategory: string
    totalChanges: number
    hasBreakingChanges: boolean
    requiresApproval: boolean
    migrationSql?: string
  }
}

export interface ClearDatabaseRequest {
  clearRegistry?: boolean
  dropSchemas?: boolean
  schemas?: string[] | null
}

export interface ClearDatabaseResponse {
  success: boolean
  droppedSchemas: string[]
  clearedTables: string[]
  errors: string[]
}

export interface BootstrapResponse {
  success: boolean
  messages: string[]
  error?: string
  entityCount: number
}

export interface ModuleDependencyInfo {
  dependsOnName: string
  versionRange: string
  resolvedId: string | null
  isResolved: boolean
}

export interface ModuleStatus {
  id: string
  name: string
  version: string
  author?: string
  entityCount: number
  serviceCount: number
  createdAt: string
  publishedAt?: string
  schemaInitialized: boolean
  tableCount: number
  schemaName?: string
  dependencies: ModuleDependencyInfo[]
}

export interface HealthResponse {
  status: string
  timestamp: string
  endpoints: string[]
}

export interface DdlPreviewRequest {
  bmmdlSource: string
  moduleName: string
}

export interface DdlPreviewResponse {
  success: boolean
  ddl: string
  tableCount: number
  errors: string[]
  warnings: string[]
}

/**
 * Admin service for protected operations.
 * Admin endpoints rely on the user's JWT token (Authorization header)
 * which is automatically added by the request interceptor.
 *
 * Note: Admin endpoints are served by the Registry API.
 */
export const adminService = {
  /**
   * Compile BMMDL source code and optionally install into system
   */
  async compile(request: CompileRequest): Promise<CompileResponse> {
    const response = await api.post<CompileResponse>('/admin/compile', {
      bmmdlSource: request.bmmdlSource,
      moduleName: request.moduleName,
      tenantId: request.tenantId ?? null,
      publish: request.publish ?? true,
      initSchema: request.initSchema ?? false,
      force: request.force ?? false
    })
    return response.data
  },

  /**
   * Validate BMMDL source without installing (compile with publish=false)
   */
  async validate(bmmdlSource: string, moduleName: string): Promise<CompileResponse> {
    const response = await api.post<CompileResponse>('/admin/compile', {
      bmmdlSource,
      moduleName,
      publish: false,
      initSchema: false
    })
    return response.data
  },

  /**
   * Clear database - drop schemas and/or truncate registry tables.
   * WARNING: This is a destructive operation!
   */
  async clearDatabase(request: ClearDatabaseRequest): Promise<ClearDatabaseResponse> {
    const response = await api.post<ClearDatabaseResponse>('/admin/clear-database', request)
    return response.data
  },

  /**
   * Bootstrap platform - clear DB then compile and install Module 0 (Platform).
   * Equivalent to "bmmdlc bootstrap --init-platform".
   */
  async bootstrapPlatform(): Promise<BootstrapResponse> {
    const response = await api.post<BootstrapResponse>('/admin/bootstrap')
    return response.data
  },

  /**
   * List all published modules with schema status
   */
  async listModules(): Promise<ModuleStatus[]> {
    const response = await api.get<ModuleStatus[]>('/admin/modules')
    return response.data
  },

  /**
   * Health check for admin endpoints
   */
  async healthCheck(): Promise<HealthResponse> {
    const response = await api.get<HealthResponse>('/admin/health')
    return response.data
  },

  /**
   * Preview DDL that would be generated from BMMDL source
   */
  async previewDdl(request: DdlPreviewRequest): Promise<DdlPreviewResponse> {
    const response = await api.post<DdlPreviewResponse>('/admin/ddl-preview', request)
    return response.data
  }
}

export default adminService
