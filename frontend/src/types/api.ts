// Generic API response types

export interface ApiResponse<T> {
  data: T
  message?: string
}

export interface ApiError {
  message: string
  code?: string
  details?: Record<string, string[]>
}

export interface PaginatedResponse<T> {
  value: T[]
  '@odata.count'?: number
  '@odata.nextLink'?: string
}

// Common entity fields
export interface AuditableEntity {
  id: string
  createdAt: string
  createdBy?: string
  modifiedAt?: string
  modifiedBy?: string
}

export interface TenantScopedEntity extends AuditableEntity {
  tenantId: string
}
