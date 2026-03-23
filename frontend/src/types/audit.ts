export interface AuditLogEntry {
  id: string
  eventName: string
  entityName: string
  entityId?: string
  tenantId?: string
  userId?: string
  correlationId?: string
  payload: Record<string, unknown>
  createdAt: string
}

export interface AuditLogFilter {
  entityName?: string
  entityId?: string
  userId?: string
  from?: string
  to?: string
  eventType?: string
}

export interface AuditLogResponse {
  value: AuditLogEntry[]
  count: number
}
