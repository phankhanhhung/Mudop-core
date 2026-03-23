import api from './api'
import type { AuditLogFilter, AuditLogResponse } from '@/types/audit'

export const auditService = {
  async listLogs(filter: AuditLogFilter = {}, top = 50, skip = 0): Promise<AuditLogResponse> {
    const params: Record<string, string> = {}
    if (filter.entityName) params.entityName = filter.entityName
    if (filter.entityId) params.entityId = filter.entityId
    if (filter.userId) params.userId = filter.userId
    if (filter.from) params.from = filter.from
    if (filter.to) params.to = filter.to
    if (filter.eventType) params.eventType = filter.eventType
    params.top = String(top)
    params.skip = String(skip)

    const response = await api.get<AuditLogResponse>('/audit-logs', { params })
    return response.data
  }
}

export default auditService
