import { describe, it, expect, vi, beforeEach } from 'vitest'
import { aiService } from '../aiService'

vi.mock('../api', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

import api from '../api'

describe('aiService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  // ── getStatus() ──────────────────────────────────────────────────────────────

  describe('getStatus()', () => {
    it('returns configured status with model from successful GET /ai/status', async () => {
      vi.mocked(api.get).mockResolvedValue({ data: { configured: true, model: 'claude-haiku-3' } })
      const result = await aiService.getStatus()
      expect(result).toEqual({ configured: true, model: 'claude-haiku-3' })
    })

    it('returns { configured: false, model: "" } when GET throws (graceful fallback)', async () => {
      vi.mocked(api.get).mockRejectedValue(new Error('Network Error'))
      const result = await aiService.getStatus()
      expect(result).toEqual({ configured: false, model: '' })
    })

    it('calls api.get with /ai/status', async () => {
      vi.mocked(api.get).mockResolvedValue({ data: { configured: true, model: 'claude-haiku-3' } })
      await aiService.getStatus()
      expect(api.get).toHaveBeenCalledWith('/ai/status')
    })
  })

  // ── assist() ─────────────────────────────────────────────────────────────────

  describe('assist()', () => {
    it('calls api.post with /ai/assist and the request body', async () => {
      const request = { operation: 'generate' as const, context: 'module Test {}', prompt: 'Create entity' }
      vi.mocked(api.post).mockResolvedValue({ data: { result: 'entity Foo {}' } })
      await aiService.assist(request)
      expect(api.post).toHaveBeenCalledWith('/ai/assist', request)
    })

    it('returns the response data from api.post', async () => {
      const responseData = { result: 'entity Bar {}', suggestions: ['Fix indentation'] }
      vi.mocked(api.post).mockResolvedValue({ data: responseData })
      const result = await aiService.assist({ operation: 'generate', context: 'module Test {}' })
      expect(result).toEqual(responseData)
    })

    it('operation = generate: passes prompt and context', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { result: 'entity Customer {}' } })
      const request = {
        operation: 'generate' as const,
        context: 'module CRM {}',
        prompt: 'Create a Customer entity with name and email',
      }
      await aiService.assist(request)
      expect(api.post).toHaveBeenCalledWith('/ai/assist', expect.objectContaining({
        operation: 'generate',
        context: 'module CRM {}',
        prompt: 'Create a Customer entity with name and email',
      }))
    })

    it('operation = review: passes context only (no prompt or error)', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { result: '', suggestions: ['Add a key field'] } })
      const request = { operation: 'review' as const, context: 'entity Foo {}' }
      await aiService.assist(request)
      const callArg = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(callArg.operation).toBe('review')
      expect(callArg.context).toBe('entity Foo {}')
      expect(callArg.prompt).toBeUndefined()
      expect(callArg.error).toBeUndefined()
    })

    it('operation = explain-error: passes error and context', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { result: 'The entity keyword is missing' } })
      const request = {
        operation: 'explain-error' as const,
        context: 'module Test {}',
        error: 'Line 5, Col 3: Expected entity keyword',
      }
      await aiService.assist(request)
      expect(api.post).toHaveBeenCalledWith('/ai/assist', expect.objectContaining({
        operation: 'explain-error',
        error: 'Line 5, Col 3: Expected entity keyword',
        context: 'module Test {}',
      }))
    })

    it('operation = complete: passes cursorLine, cursorColumn, and context', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { result: 'key ID: UUID;' } })
      const request = {
        operation: 'complete' as const,
        context: 'entity Customer {\n  ',
        cursorLine: 2,
        cursorColumn: 2,
      }
      await aiService.assist(request)
      expect(api.post).toHaveBeenCalledWith('/ai/assist', expect.objectContaining({
        operation: 'complete',
        cursorLine: 2,
        cursorColumn: 2,
        context: 'entity Customer {\n  ',
      }))
    })

    it('propagates errors thrown by api.post (does not swallow them)', async () => {
      const err = new Error('Server error 500')
      vi.mocked(api.post).mockRejectedValue(err)
      await expect(
        aiService.assist({ operation: 'generate', context: 'module Test {}', prompt: 'Create entity' }),
      ).rejects.toThrow('Server error 500')
    })

    it('returns response with suggestions array as-is', async () => {
      const suggestions = ['Add key field', 'Use UUID for primary key', 'Add Auditable aspect']
      vi.mocked(api.post).mockResolvedValue({ data: { result: '', suggestions } })
      const result = await aiService.assist({ operation: 'review', context: 'entity Foo {}' })
      expect(result.suggestions).toEqual(suggestions)
      expect(result.suggestions).toHaveLength(3)
    })

    it('cursorLine and cursorColumn are optional and can be omitted', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { result: 'entity X {}' } })
      // Should not throw when cursorLine and cursorColumn are absent
      const request = { operation: 'complete' as const, context: 'entity X' }
      await expect(aiService.assist(request)).resolves.toEqual({ result: 'entity X {}' })
      const callArg = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(callArg.cursorLine).toBeUndefined()
      expect(callArg.cursorColumn).toBeUndefined()
    })
  })
})
