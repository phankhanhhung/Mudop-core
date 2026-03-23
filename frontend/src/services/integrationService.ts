import api from './api'

// ---- Types ----

export interface WebhookConfig {
  id: string
  name: string
  targetUrl: string
  hasSecret: boolean        // server never sends the secret back
  eventFilter: string[]     // glob patterns e.g. ["Customer.*", "Order.created"]
  isActive: boolean
  createdAt: string         // ISO date string
  updatedAt?: string
}

export interface CreateWebhookRequest {
  name: string
  targetUrl: string
  secret?: string           // only sent on create/update
  eventFilter: string[]
  isActive: boolean
}

export type UpdateWebhookRequest = CreateWebhookRequest

export interface TestDeliveryResult {
  success: boolean
  statusCode: number
  durationMs: number
  error?: string
}

export interface OutboxEntry {
  id: string
  eventName: string
  entityName: string
  entityId?: string
  status: 'pending' | 'delivered' | 'dead_letter'
  retryCount: number
  maxRetries: number
  nextRetryAt?: string
  createdAt: string
  processedAt?: string
  errorMessage?: string
  isIntegration: boolean
}

export interface WebhookDeliveryLog {
  id: string
  webhookId: string
  eventName: string
  targetUrl: string
  statusCode: number
  success: boolean
  errorMessage?: string
  durationMs: number
  attemptedAt: string
}

export interface IntegrationHealth {
  webhookCount: number
  activeWebhookCount: number
  pendingOutboxCount: number
  deadLetterCount: number
}

// ---- Service ----

export const integrationService = {
  // ---- Webhooks ----

  async listWebhooks(): Promise<WebhookConfig[]> {
    const r = await api.get<WebhookConfig[]>('/admin/integrations/webhooks')
    return r.data
  },

  async createWebhook(data: CreateWebhookRequest): Promise<WebhookConfig> {
    const r = await api.post<WebhookConfig>('/admin/integrations/webhooks', data)
    return r.data
  },

  async updateWebhook(id: string, data: UpdateWebhookRequest): Promise<WebhookConfig> {
    const r = await api.put<WebhookConfig>(`/admin/integrations/webhooks/${encodeURIComponent(id)}`, data)
    return r.data
  },

  async deleteWebhook(id: string): Promise<void> {
    await api.delete(`/admin/integrations/webhooks/${encodeURIComponent(id)}`)
  },

  async testWebhook(id: string): Promise<TestDeliveryResult> {
    const r = await api.post<TestDeliveryResult>(`/admin/integrations/webhooks/${encodeURIComponent(id)}/test`)
    return r.data
  },

  // ---- Outbox ----

  async listOutbox(status?: 'pending' | 'delivered' | 'dead_letter' | 'all', limit = 50): Promise<OutboxEntry[]> {
    const params: Record<string, string | number> = { limit }
    if (status) params.status = status
    const r = await api.get<OutboxEntry[]>('/admin/integrations/outbox', { params })
    return r.data
  },

  async retryOutboxEntry(id: string): Promise<void> {
    await api.post(`/admin/integrations/outbox/${encodeURIComponent(id)}/retry`)
  },

  async dismissOutboxEntry(id: string): Promise<void> {
    await api.delete(`/admin/integrations/outbox/${encodeURIComponent(id)}`)
  },

  // ---- Delivery Log ----

  async listDeliveryLog(webhookId?: string, limit = 50): Promise<WebhookDeliveryLog[]> {
    const params: Record<string, string | number> = { limit }
    if (webhookId) params.webhookId = webhookId
    const r = await api.get<WebhookDeliveryLog[]>('/admin/integrations/delivery-log', { params })
    return r.data
  },

  // ---- Health ----

  async getHealth(): Promise<IntegrationHealth> {
    const r = await api.get<IntegrationHealth>('/admin/integrations/health')
    return r.data
  },
}

export default integrationService
