import { describe, it, expect, vi, beforeEach } from 'vitest'

// ---------------------------------------------------------------------------
// Mocks — hoisted so the factory can reference the mock fns
// ---------------------------------------------------------------------------

const { mockGet, mockPost, mockDelete } = vi.hoisted(() => {
  const mockGet = vi.fn()
  const mockPost = vi.fn()
  const mockDelete = vi.fn()
  return { mockGet, mockPost, mockDelete }
})

vi.mock('@/services/api', () => ({
  default: {
    get: mockGet,
    post: mockPost,
    delete: mockDelete,
  },
}))

// ---------------------------------------------------------------------------
// Import service AFTER mocks
// ---------------------------------------------------------------------------

import { commentService } from '../commentService'
import type { Comment, ChangeRequest, RecordLockStatus } from '../commentService'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeComment(overrides: Partial<Comment> = {}): Comment {
  return {
    id: 'c-1',
    authorId: 'user-1',
    authorName: 'Alice',
    content: 'Hello world',
    mentions: [],
    likedBy: [],
    createdAt: '2026-01-01T10:00:00Z',
    ...overrides,
  }
}

function makeChangeRequest(overrides: Partial<ChangeRequest> = {}): ChangeRequest {
  return {
    id: 'cr-1',
    proposedById: 'user-2',
    proposedByName: 'Bob',
    proposedChanges: { Name: 'New Name' },
    status: 'pending',
    createdAt: '2026-01-01T10:00:00Z',
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('commentService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  // ── listComments() ─────────────────────────────────────────────────────

  describe('listComments()', () => {
    it('makes GET to /odata/{module}/{entityType}/{entityId}/comments', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await commentService.listComments('crm', 'Customer', 'cust-1')
      expect(mockGet).toHaveBeenCalledWith('/odata/crm/Customer/cust-1/comments')
    })

    it('returns the array of comments from response data', async () => {
      const comments = [makeComment({ id: 'c-1' }), makeComment({ id: 'c-2', content: 'Second' })]
      mockGet.mockResolvedValue({ data: comments })
      const result = await commentService.listComments('crm', 'Customer', 'cust-1')
      expect(result).toEqual(comments)
    })

    it('returns empty array when response data is empty', async () => {
      mockGet.mockResolvedValue({ data: [] })
      const result = await commentService.listComments('crm', 'Customer', 'cust-1')
      expect(result).toHaveLength(0)
    })
  })

  // ── createComment() ────────────────────────────────────────────────────

  describe('createComment()', () => {
    it('makes POST to /odata/{module}/{entityType}/{entityId}/comments with content and mentions', async () => {
      const created = makeComment()
      mockPost.mockResolvedValue({ data: created })
      await commentService.createComment('crm', 'Customer', 'cust-1', 'Hello @bob', ['bob'])
      expect(mockPost).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/comments',
        { content: 'Hello @bob', mentions: ['bob'] }
      )
    })

    it('defaults mentions to empty array when not provided', async () => {
      const created = makeComment()
      mockPost.mockResolvedValue({ data: created })
      await commentService.createComment('crm', 'Customer', 'cust-1', 'No mention')
      expect(mockPost).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/comments',
        { content: 'No mention', mentions: [] }
      )
    })

    it('returns the created comment from response data', async () => {
      const created = makeComment({ id: 'c-new', content: 'New comment' })
      mockPost.mockResolvedValue({ data: created })
      const result = await commentService.createComment('crm', 'Customer', 'cust-1', 'New comment')
      expect(result).toEqual(created)
    })
  })

  // ── deleteComment() ────────────────────────────────────────────────────

  describe('deleteComment()', () => {
    it('makes DELETE to /odata/{module}/{entityType}/{entityId}/comments/{commentId}', async () => {
      mockDelete.mockResolvedValue({})
      await commentService.deleteComment('crm', 'Customer', 'cust-1', 'c-99')
      expect(mockDelete).toHaveBeenCalledWith('/odata/crm/Customer/cust-1/comments/c-99')
    })

    it('resolves with undefined (void)', async () => {
      mockDelete.mockResolvedValue({})
      const result = await commentService.deleteComment('crm', 'Customer', 'cust-1', 'c-99')
      expect(result).toBeUndefined()
    })
  })

  // ── toggleLike() ───────────────────────────────────────────────────────

  describe('toggleLike()', () => {
    it('makes POST to /odata/{module}/{entityType}/{entityId}/comments/{commentId}/like', async () => {
      const updated = makeComment({ likedBy: ['user-1'] })
      mockPost.mockResolvedValue({ data: updated })
      await commentService.toggleLike('crm', 'Customer', 'cust-1', 'c-1')
      expect(mockPost).toHaveBeenCalledWith('/odata/crm/Customer/cust-1/comments/c-1/like')
    })

    it('returns the updated comment with toggled likedBy from response data', async () => {
      const updated = makeComment({ likedBy: ['user-1', 'user-2'] })
      mockPost.mockResolvedValue({ data: updated })
      const result = await commentService.toggleLike('crm', 'Customer', 'cust-1', 'c-1')
      expect(result.likedBy).toEqual(['user-1', 'user-2'])
    })
  })

  // ── listChangeRequests() ───────────────────────────────────────────────

  describe('listChangeRequests()', () => {
    it('makes GET to /odata/{module}/{entityType}/{entityId}/change-requests with no params by default', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await commentService.listChangeRequests('crm', 'Customer', 'cust-1')
      expect(mockGet).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/change-requests',
        { params: {} }
      )
    })

    it('includes status param when status is provided', async () => {
      mockGet.mockResolvedValue({ data: [] })
      await commentService.listChangeRequests('crm', 'Customer', 'cust-1', 'pending')
      expect(mockGet).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/change-requests',
        { params: { status: 'pending' } }
      )
    })

    it('returns the array of change requests from response data', async () => {
      const requests = [makeChangeRequest({ id: 'cr-1' }), makeChangeRequest({ id: 'cr-2', status: 'approved' })]
      mockGet.mockResolvedValue({ data: requests })
      const result = await commentService.listChangeRequests('crm', 'Customer', 'cust-1')
      expect(result).toEqual(requests)
    })
  })

  // ── createChangeRequest() ─────────────────────────────────────────────

  describe('createChangeRequest()', () => {
    it('makes POST to /odata/{module}/{entityType}/{entityId}/change-requests with proposedChanges', async () => {
      const created = makeChangeRequest()
      mockPost.mockResolvedValue({ data: created })
      const changes = { Name: 'Updated Name', Email: 'new@email.com' }
      await commentService.createChangeRequest('crm', 'Customer', 'cust-1', changes)
      expect(mockPost).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/change-requests',
        { proposedChanges: changes }
      )
    })

    it('returns the created change request from response data', async () => {
      const created = makeChangeRequest({ id: 'cr-new', proposedChanges: { Name: 'X' } })
      mockPost.mockResolvedValue({ data: created })
      const result = await commentService.createChangeRequest('crm', 'Customer', 'cust-1', { Name: 'X' })
      expect(result).toEqual(created)
    })
  })

  // ── reviewChangeRequest() ─────────────────────────────────────────────

  describe('reviewChangeRequest()', () => {
    it('makes POST to /change-requests/{requestId}/review with approve decision', async () => {
      const reviewed = makeChangeRequest({ status: 'approved' })
      mockPost.mockResolvedValue({ data: reviewed })
      await commentService.reviewChangeRequest('crm', 'Customer', 'cust-1', 'cr-1', 'approve', 'Looks good')
      expect(mockPost).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/change-requests/cr-1/review',
        { decision: 'approve', comment: 'Looks good' }
      )
    })

    it('makes POST with reject decision and no comment when comment is omitted', async () => {
      const reviewed = makeChangeRequest({ status: 'rejected' })
      mockPost.mockResolvedValue({ data: reviewed })
      await commentService.reviewChangeRequest('crm', 'Customer', 'cust-1', 'cr-1', 'reject')
      expect(mockPost).toHaveBeenCalledWith(
        '/odata/crm/Customer/cust-1/change-requests/cr-1/review',
        { decision: 'reject', comment: undefined }
      )
    })

    it('returns the reviewed change request from response data', async () => {
      const reviewed = makeChangeRequest({ status: 'rejected', reviewComment: 'Not valid' })
      mockPost.mockResolvedValue({ data: reviewed })
      const result = await commentService.reviewChangeRequest('crm', 'Customer', 'cust-1', 'cr-1', 'reject', 'Not valid')
      expect(result.status).toBe('rejected')
      expect(result.reviewComment).toBe('Not valid')
    })
  })

  // ── getLockStatus() ────────────────────────────────────────────────────

  describe('getLockStatus()', () => {
    it('makes GET to /odata/{module}/{entityType}/{entityId}/lock', async () => {
      const lockStatus: RecordLockStatus = { locked: false }
      mockGet.mockResolvedValue({ data: lockStatus })
      await commentService.getLockStatus('crm', 'Customer', 'cust-1')
      expect(mockGet).toHaveBeenCalledWith('/odata/crm/Customer/cust-1/lock')
    })

    it('returns unlocked status when no one is editing', async () => {
      const lockStatus: RecordLockStatus = { locked: false }
      mockGet.mockResolvedValue({ data: lockStatus })
      const result = await commentService.getLockStatus('crm', 'Customer', 'cust-1')
      expect(result.locked).toBe(false)
      expect(result.userId).toBeUndefined()
    })

    it('returns locked status with userId and displayName when locked', async () => {
      const lockStatus: RecordLockStatus = {
        locked: true,
        userId: 'user-42',
        displayName: 'Charlie',
        startedAt: '2026-01-01T10:00:00Z',
      }
      mockGet.mockResolvedValue({ data: lockStatus })
      const result = await commentService.getLockStatus('crm', 'Customer', 'cust-1')
      expect(result.locked).toBe(true)
      expect(result.userId).toBe('user-42')
      expect(result.displayName).toBe('Charlie')
    })
  })
})
