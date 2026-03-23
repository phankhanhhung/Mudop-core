import { describe, it, expect, vi, beforeEach } from 'vitest'

// ---------------------------------------------------------------------------
// Mocks — hoisted before any imports that trigger the module under test
// ---------------------------------------------------------------------------

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

import { reportService } from '../reportService'
import type {
  ReportTemplate,
  CreateReportRequest,
  ShareTokenResponse,
  ReportDataResponse,
} from '../reportService'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeTemplate(overrides: Partial<ReportTemplate> = {}): ReportTemplate {
  return {
    id: 'tpl-1',
    name: 'Sales Report',
    module: 'crm',
    entityType: 'Customer',
    layoutType: 'list',
    fields: [
      { name: 'Name', label: 'Name', width: 150 },
      { name: 'Status', label: 'Status', width: 100 },
    ],
    sortBy: [],
    scheduleRecipients: [],
    isPublic: false,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeCreateRequest(overrides: Partial<CreateReportRequest> = {}): CreateReportRequest {
  return {
    name: 'New Report',
    module: 'crm',
    entityType: 'Customer',
    layoutType: 'list',
    fields: [{ name: 'Name', label: 'Name', width: 150 }],
    sortBy: [],
    scheduleRecipients: [],
    isPublic: false,
    ...overrides,
  }
}

function makeDataResponse(overrides: Partial<ReportDataResponse> = {}): ReportDataResponse {
  return {
    template: makeTemplate(),
    rows: [{ Name: 'Alice', Status: 'Active' }],
    totalCount: 1,
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('reportService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  // ── listTemplates() ───────────────────────────────────────────────────────

  describe('listTemplates()', () => {
    it('makes GET request to /admin/reports/templates', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await reportService.listTemplates()
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates')
    })

    it('returns the array of report templates from response data', async () => {
      const templates = [makeTemplate({ id: 'tpl-1' }), makeTemplate({ id: 'tpl-2', name: 'Monthly Report' })]
      mockGet.mockResolvedValue({ data: templates })
      const result = await reportService.listTemplates()
      expect(result).toEqual(templates)
    })
  })

  // ── getTemplate() ─────────────────────────────────────────────────────────

  describe('getTemplate()', () => {
    it('makes GET request to /admin/reports/templates/{id}', async () => {
      mockGet.mockResolvedValue({ data: makeTemplate() })
      await reportService.getTemplate('tpl-42')
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates/tpl-42')
    })

    it('returns the report template from response data', async () => {
      const template = makeTemplate({ id: 'tpl-42', name: 'Detail Report' })
      mockGet.mockResolvedValue({ data: template })
      const result = await reportService.getTemplate('tpl-42')
      expect(result).toEqual(template)
    })
  })

  // ── createTemplate() ──────────────────────────────────────────────────────

  describe('createTemplate()', () => {
    it('makes POST request to /admin/reports/templates with the request body', async () => {
      const request = makeCreateRequest({ name: 'New Report', layoutType: 'summary' })
      mockPost.mockResolvedValue({ data: makeTemplate() })
      await reportService.createTemplate(request)
      expect(mockPost).toHaveBeenCalledWith('/admin/reports/templates', request)
    })

    it('returns the created template from response data', async () => {
      const request = makeCreateRequest()
      const created = makeTemplate({ id: 'tpl-new', name: 'New Report' })
      mockPost.mockResolvedValue({ data: created })
      const result = await reportService.createTemplate(request)
      expect(result).toEqual(created)
    })
  })

  // ── updateTemplate() ──────────────────────────────────────────────────────

  describe('updateTemplate()', () => {
    it('makes PUT request to /admin/reports/templates/{id} with the request body', async () => {
      const id = 'tpl-10'
      const request = makeCreateRequest({ name: 'Updated Report', isPublic: true })
      mockPut.mockResolvedValue({ data: makeTemplate() })
      await reportService.updateTemplate(id, request)
      expect(mockPut).toHaveBeenCalledWith(`/admin/reports/templates/${id}`, request)
    })

    it('returns the updated template from response data', async () => {
      const updated = makeTemplate({ id: 'tpl-10', name: 'Updated Report', isPublic: true })
      mockPut.mockResolvedValue({ data: updated })
      const result = await reportService.updateTemplate('tpl-10', makeCreateRequest({ name: 'Updated Report' }))
      expect(result).toEqual(updated)
    })
  })

  // ── deleteTemplate() ──────────────────────────────────────────────────────

  describe('deleteTemplate()', () => {
    it('makes DELETE request to /admin/reports/templates/{id}', async () => {
      mockDelete.mockResolvedValue({})
      await reportService.deleteTemplate('tpl-99')
      expect(mockDelete).toHaveBeenCalledWith('/admin/reports/templates/tpl-99')
    })

    it('resolves with undefined (void)', async () => {
      mockDelete.mockResolvedValue({})
      const result = await reportService.deleteTemplate('tpl-99')
      expect(result).toBeUndefined()
    })
  })

  // ── shareTemplate() ───────────────────────────────────────────────────────

  describe('shareTemplate()', () => {
    it('makes POST request to /admin/reports/templates/{id}/share', async () => {
      const shareResponse: ShareTokenResponse = { shareToken: 'tok-abc', shareUrl: 'https://example.com/share/tok-abc' }
      mockPost.mockResolvedValue({ data: shareResponse })
      await reportService.shareTemplate('tpl-5')
      expect(mockPost).toHaveBeenCalledWith('/admin/reports/templates/tpl-5/share')
    })

    it('returns the ShareTokenResponse from response data', async () => {
      const shareResponse: ShareTokenResponse = {
        shareToken: 'tok-xyz',
        shareUrl: 'https://app.example.com/reports/tok-xyz',
      }
      mockPost.mockResolvedValue({ data: shareResponse })
      const result = await reportService.shareTemplate('tpl-5')
      expect(result).toEqual(shareResponse)
    })
  })

  // ── revokeShare() ─────────────────────────────────────────────────────────

  describe('revokeShare()', () => {
    it('makes DELETE request to /admin/reports/templates/{id}/share', async () => {
      mockDelete.mockResolvedValue({})
      await reportService.revokeShare('tpl-7')
      expect(mockDelete).toHaveBeenCalledWith('/admin/reports/templates/tpl-7/share')
    })

    it('resolves with undefined (void)', async () => {
      mockDelete.mockResolvedValue({})
      const result = await reportService.revokeShare('tpl-7')
      expect(result).toBeUndefined()
    })
  })

  // ── getReportData() ───────────────────────────────────────────────────────

  describe('getReportData()', () => {
    it('makes GET request to /admin/reports/templates/{id}/data with default $top=1000 when no filter', async () => {
      mockGet.mockResolvedValue({ data: makeDataResponse() })
      await reportService.getReportData('tpl-3')
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates/tpl-3/data', {
        params: { $top: 1000 },
      })
    })

    it('includes $filter param when filter string is provided', async () => {
      mockGet.mockResolvedValue({ data: makeDataResponse() })
      await reportService.getReportData('tpl-3', "Status eq 'Active'")
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates/tpl-3/data', {
        params: { $top: 1000, $filter: "Status eq 'Active'" },
      })
    })

    it('uses custom top value when top parameter is provided', async () => {
      mockGet.mockResolvedValue({ data: makeDataResponse() })
      await reportService.getReportData('tpl-3', undefined, 500)
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates/tpl-3/data', {
        params: { $top: 500 },
      })
    })

    it('includes both $filter and custom $top when both are provided', async () => {
      mockGet.mockResolvedValue({ data: makeDataResponse() })
      await reportService.getReportData('tpl-3', 'Name ne null', 250)
      expect(mockGet).toHaveBeenCalledWith('/admin/reports/templates/tpl-3/data', {
        params: { $top: 250, $filter: 'Name ne null' },
      })
    })

    it('returns the ReportDataResponse from response data', async () => {
      const dataResponse = makeDataResponse({
        rows: [{ Name: 'Bob', Status: 'Inactive' }],
        totalCount: 1,
      })
      mockGet.mockResolvedValue({ data: dataResponse })
      const result = await reportService.getReportData('tpl-3')
      expect(result).toEqual(dataResponse)
    })
  })

  // ── triggerScheduledSend() ────────────────────────────────────────────────

  describe('triggerScheduledSend()', () => {
    it('makes POST request to /admin/reports/templates/{id}/schedule-send', async () => {
      mockPost.mockResolvedValue({ data: { message: 'Scheduled send triggered.' } })
      await reportService.triggerScheduledSend('tpl-8')
      expect(mockPost).toHaveBeenCalledWith('/admin/reports/templates/tpl-8/schedule-send')
    })

    it('returns the message object from response data', async () => {
      const messageResponse = { message: 'Report emailed to 3 recipients.' }
      mockPost.mockResolvedValue({ data: messageResponse })
      const result = await reportService.triggerScheduledSend('tpl-8')
      expect(result).toEqual(messageResponse)
    })
  })

  // ── getPublicReport() ─────────────────────────────────────────────────────

  describe('getPublicReport()', () => {
    it('makes GET request to /reports/{shareToken}', async () => {
      mockGet.mockResolvedValue({ data: makeTemplate() })
      await reportService.getPublicReport('share-tok-abc')
      expect(mockGet).toHaveBeenCalledWith('/reports/share-tok-abc')
    })

    it('returns the ReportTemplate from response data', async () => {
      const template = makeTemplate({ id: 'tpl-public', isPublic: true, shareToken: 'share-tok-abc' })
      mockGet.mockResolvedValue({ data: template })
      const result = await reportService.getPublicReport('share-tok-abc')
      expect(result).toEqual(template)
    })
  })
})
