import api from './api'

// ---- Types ----

export interface ReportField {
  name: string
  label: string
  width: number        // default 150
  format?: string      // "date" | "datetime" | "currency" | "percent"
  aggregate?: string   // "sum" | "count" | "avg" | "min" | "max"
}

export interface SortConfig {
  field: string
  direction: 'asc' | 'desc'
}

export interface ReportTemplate {
  id: string
  name: string
  description?: string
  module: string
  entityType: string
  layoutType: 'list' | 'detail' | 'summary'
  fields: ReportField[]
  groupBy?: string
  sortBy: SortConfig[]
  header?: string
  footer?: string
  isPublic: boolean
  shareToken?: string
  scheduleCron?: string
  scheduleRecipients: string[]
  createdAt: string
  updatedAt?: string
}

export interface CreateReportRequest {
  name: string
  description?: string
  module: string
  entityType: string
  layoutType: 'list' | 'detail' | 'summary'
  fields: ReportField[]
  groupBy?: string
  sortBy: SortConfig[]
  header?: string
  footer?: string
  isPublic: boolean
  scheduleCron?: string
  scheduleRecipients: string[]
}

export type UpdateReportRequest = CreateReportRequest

export interface ShareTokenResponse {
  shareToken: string
  shareUrl: string
}

export interface ReportDataResponse {
  template: ReportTemplate
  rows: Record<string, unknown>[]
  totalCount: number
}

// ---- Service ----

export const reportService = {
  async listTemplates(): Promise<ReportTemplate[]> {
    const r = await api.get<ReportTemplate[]>('/admin/reports/templates')
    return r.data
  },

  async getTemplate(id: string): Promise<ReportTemplate> {
    const r = await api.get<ReportTemplate>(`/admin/reports/templates/${encodeURIComponent(id)}`)
    return r.data
  },

  async createTemplate(data: CreateReportRequest): Promise<ReportTemplate> {
    const r = await api.post<ReportTemplate>('/admin/reports/templates', data)
    return r.data
  },

  async updateTemplate(id: string, data: UpdateReportRequest): Promise<ReportTemplate> {
    const r = await api.put<ReportTemplate>(`/admin/reports/templates/${encodeURIComponent(id)}`, data)
    return r.data
  },

  async deleteTemplate(id: string): Promise<void> {
    await api.delete(`/admin/reports/templates/${encodeURIComponent(id)}`)
  },

  async shareTemplate(id: string): Promise<ShareTokenResponse> {
    const r = await api.post<ShareTokenResponse>(`/admin/reports/templates/${encodeURIComponent(id)}/share`)
    return r.data
  },

  async revokeShare(id: string): Promise<void> {
    await api.delete(`/admin/reports/templates/${encodeURIComponent(id)}/share`)
  },

  async getReportData(id: string, filter?: string, top = 1000): Promise<ReportDataResponse> {
    const params: Record<string, string | number> = { $top: top }
    if (filter) params.$filter = filter
    const r = await api.get<ReportDataResponse>(`/admin/reports/templates/${encodeURIComponent(id)}/data`, { params })
    return r.data
  },

  async triggerScheduledSend(id: string): Promise<{ message: string }> {
    const r = await api.post<{ message: string }>(`/admin/reports/templates/${encodeURIComponent(id)}/schedule-send`)
    return r.data
  },

  async getPublicReport(shareToken: string): Promise<ReportTemplate> {
    const r = await api.get<ReportTemplate>(`/reports/${encodeURIComponent(shareToken)}`)
    return r.data
  },
}

export default reportService
