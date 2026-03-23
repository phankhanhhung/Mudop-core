import { describe, it, expect, vi, beforeEach } from 'vitest'

// ---------------------------------------------------------------------------
// Mocks — must be hoisted before any imports that trigger the module under test
// ---------------------------------------------------------------------------

// Use vi.hoisted so mockApi is available inside the vi.mock factory,
// which is itself hoisted to the top of the file by Vitest's transform.
const { mockGet, mockPost, mockPut, mockDelete } = vi.hoisted(() => {
  const mockGet = vi.fn()
  const mockPost = vi.fn()
  const mockPut = vi.fn()
  const mockDelete = vi.fn()
  return { mockGet, mockPost, mockPut, mockDelete }
})

vi.mock('@/services/api', () => ({
  default: {
    get: mockGet,
    post: mockPost,
    put: mockPut,
    delete: mockDelete,
  },
}))

// ---------------------------------------------------------------------------
// Import service AFTER mocks are registered
// ---------------------------------------------------------------------------

import { integrationService } from '../integrationService'
import type {
  WebhookConfig,
  CreateWebhookRequest,
  TestDeliveryResult,
  OutboxEntry,
  WebhookDeliveryLog,
  IntegrationHealth,
} from '../integrationService'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWebhook(overrides: Partial<WebhookConfig> = {}): WebhookConfig {
  return {
    id: 'wh-1',
    name: 'Test Webhook',
    targetUrl: 'https://example.com/hook',
    hasSecret: false,
    eventFilter: ['Customer.*'],
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeOutboxEntry(overrides: Partial<OutboxEntry> = {}): OutboxEntry {
  return {
    id: 'ob-1',
    eventName: 'Customer.created',
    entityName: 'Customer',
    entityId: 'cust-1',
    status: 'pending',
    retryCount: 0,
    maxRetries: 3,
    createdAt: '2026-01-01T00:00:00Z',
    isIntegration: true,
    ...overrides,
  }
}

function makeDeliveryLog(overrides: Partial<WebhookDeliveryLog> = {}): WebhookDeliveryLog {
  return {
    id: 'dl-1',
    webhookId: 'wh-1',
    eventName: 'Customer.created',
    targetUrl: 'https://example.com/hook',
    statusCode: 200,
    success: true,
    durationMs: 42,
    attemptedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeHealth(overrides: Partial<IntegrationHealth> = {}): IntegrationHealth {
  return {
    webhookCount: 3,
    activeWebhookCount: 2,
    pendingOutboxCount: 5,
    deadLetterCount: 1,
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('integrationService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  // ── listWebhooks() ────────────────────────────────────────────────────────

  describe('listWebhooks()', () => {
    it('makes GET request to /admin/integrations/webhooks', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listWebhooks()
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/webhooks')
    })

    it('returns the array of webhook configs from response data', async () => {
      const webhooks = [makeWebhook({ id: 'wh-1' }), makeWebhook({ id: 'wh-2', name: 'Second' })]
      mockGet.mockResolvedValue({ data: webhooks })
      const result = await integrationService.listWebhooks()
      expect(result).toEqual(webhooks)
    })
  })

  // ── createWebhook() ──────────────────────────────────────────────────────

  describe('createWebhook()', () => {
    it('makes POST request to /admin/integrations/webhooks with the request body', async () => {
      const request: CreateWebhookRequest = {
        name: 'New Hook',
        targetUrl: 'https://example.com/new',
        secret: 'my-secret',
        eventFilter: ['Order.*'],
        isActive: true,
      }
      mockPost.mockResolvedValue({ data: makeWebhook() })
      await integrationService.createWebhook(request)
      expect(mockPost).toHaveBeenCalledWith('/admin/integrations/webhooks', request)
    })

    it('returns the created webhook from response data', async () => {
      const created = makeWebhook({ id: 'wh-new', name: 'New Hook' })
      mockPost.mockResolvedValue({ data: created })
      const result = await integrationService.createWebhook({
        name: 'New Hook',
        targetUrl: 'https://example.com/new',
        eventFilter: [],
        isActive: true,
      })
      expect(result).toEqual(created)
    })
  })

  // ── updateWebhook() ──────────────────────────────────────────────────────

  describe('updateWebhook()', () => {
    it('makes PUT request to /admin/integrations/webhooks/{id} with the request body', async () => {
      const id = 'wh-42'
      const request: CreateWebhookRequest = {
        name: 'Updated Hook',
        targetUrl: 'https://example.com/updated',
        eventFilter: ['Customer.updated'],
        isActive: false,
      }
      mockPut.mockResolvedValue({ data: makeWebhook() })
      await integrationService.updateWebhook(id, request)
      expect(mockPut).toHaveBeenCalledWith(`/admin/integrations/webhooks/${id}`, request)
    })

    it('returns the updated webhook from response data', async () => {
      const updated = makeWebhook({ id: 'wh-42', name: 'Updated Hook', isActive: false })
      mockPut.mockResolvedValue({ data: updated })
      const result = await integrationService.updateWebhook('wh-42', {
        name: 'Updated Hook',
        targetUrl: 'https://example.com/updated',
        eventFilter: [],
        isActive: false,
      })
      expect(result).toEqual(updated)
    })
  })

  // ── deleteWebhook() ──────────────────────────────────────────────────────

  describe('deleteWebhook()', () => {
    it('makes DELETE request to /admin/integrations/webhooks/{id}', async () => {
      mockDelete.mockResolvedValue({})
      await integrationService.deleteWebhook('wh-99')
      expect(mockDelete).toHaveBeenCalledWith('/admin/integrations/webhooks/wh-99')
    })

    it('resolves with undefined (void)', async () => {
      mockDelete.mockResolvedValue({})
      const result = await integrationService.deleteWebhook('wh-99')
      expect(result).toBeUndefined()
    })
  })

  // ── testWebhook() ────────────────────────────────────────────────────────

  describe('testWebhook()', () => {
    it('makes POST request to /admin/integrations/webhooks/{id}/test', async () => {
      const testResult: TestDeliveryResult = { success: true, statusCode: 200, durationMs: 55 }
      mockPost.mockResolvedValue({ data: testResult })
      await integrationService.testWebhook('wh-5')
      expect(mockPost).toHaveBeenCalledWith('/admin/integrations/webhooks/wh-5/test')
    })

    it('returns the TestDeliveryResult from response data', async () => {
      const testResult: TestDeliveryResult = {
        success: false,
        statusCode: 503,
        durationMs: 1200,
        error: 'Service unavailable',
      }
      mockPost.mockResolvedValue({ data: testResult })
      const result = await integrationService.testWebhook('wh-5')
      expect(result).toEqual(testResult)
    })
  })

  // ── listOutbox() ─────────────────────────────────────────────────────────

  describe('listOutbox()', () => {
    it('makes GET request to /admin/integrations/outbox with default limit=50 when no params given', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listOutbox()
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/outbox', {
        params: { limit: 50 },
      })
    })

    it('includes status param when status is provided', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listOutbox('dead_letter')
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/outbox', {
        params: { limit: 50, status: 'dead_letter' },
      })
    })

    it('includes custom limit param when limit is provided', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listOutbox(undefined, 100)
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/outbox', {
        params: { limit: 100 },
      })
    })

    it('includes both status and limit when both are provided', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listOutbox('pending', 25)
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/outbox', {
        params: { limit: 25, status: 'pending' },
      })
    })

    it('returns the array of outbox entries from response data', async () => {
      const entries = [makeOutboxEntry({ id: 'ob-1' }), makeOutboxEntry({ id: 'ob-2', status: 'delivered' })]
      mockGet.mockResolvedValue({ data: entries })
      const result = await integrationService.listOutbox()
      expect(result).toEqual(entries)
    })
  })

  // ── retryOutboxEntry() ───────────────────────────────────────────────────

  describe('retryOutboxEntry()', () => {
    it('makes POST request to /admin/integrations/outbox/{id}/retry', async () => {
      mockPost.mockResolvedValue({})
      await integrationService.retryOutboxEntry('ob-7')
      expect(mockPost).toHaveBeenCalledWith('/admin/integrations/outbox/ob-7/retry')
    })

    it('resolves with undefined (void)', async () => {
      mockPost.mockResolvedValue({})
      const result = await integrationService.retryOutboxEntry('ob-7')
      expect(result).toBeUndefined()
    })
  })

  // ── dismissOutboxEntry() ─────────────────────────────────────────────────

  describe('dismissOutboxEntry()', () => {
    it('makes DELETE request to /admin/integrations/outbox/{id}', async () => {
      mockDelete.mockResolvedValue({})
      await integrationService.dismissOutboxEntry('ob-8')
      expect(mockDelete).toHaveBeenCalledWith('/admin/integrations/outbox/ob-8')
    })

    it('resolves with undefined (void)', async () => {
      mockDelete.mockResolvedValue({})
      const result = await integrationService.dismissOutboxEntry('ob-8')
      expect(result).toBeUndefined()
    })
  })

  // ── listDeliveryLog() ────────────────────────────────────────────────────

  describe('listDeliveryLog()', () => {
    it('makes GET request to /admin/integrations/delivery-log with default limit=50 and no webhookId', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listDeliveryLog()
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/delivery-log', {
        params: { limit: 50 },
      })
    })

    it('includes webhookId param when webhookId is provided', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listDeliveryLog('wh-3')
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/delivery-log', {
        params: { limit: 50, webhookId: 'wh-3' },
      })
    })

    it('uses custom limit when provided alongside webhookId', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await integrationService.listDeliveryLog('wh-3', 20)
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/delivery-log', {
        params: { limit: 20, webhookId: 'wh-3' },
      })
    })

    it('returns the array of delivery log entries from response data', async () => {
      const logs = [makeDeliveryLog({ id: 'dl-1' }), makeDeliveryLog({ id: 'dl-2', success: false, statusCode: 500 })]
      mockGet.mockResolvedValue({ data: logs })
      const result = await integrationService.listDeliveryLog()
      expect(result).toEqual(logs)
    })
  })

  // ── getHealth() ──────────────────────────────────────────────────────────

  describe('getHealth()', () => {
    it('makes GET request to /admin/integrations/health', async () => {
      mockGet.mockResolvedValue({ data: makeHealth() })
      await integrationService.getHealth()
      expect(mockGet).toHaveBeenCalledWith('/admin/integrations/health')
    })

    it('returns the IntegrationHealth object from response data', async () => {
      const health = makeHealth({ webhookCount: 10, activeWebhookCount: 7, pendingOutboxCount: 0, deadLetterCount: 3 })
      mockGet.mockResolvedValue({ data: health })
      const result = await integrationService.getHealth()
      expect(result).toEqual(health)
    })
  })
})
