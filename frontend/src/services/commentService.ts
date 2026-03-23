import api from './api'

// ---- Types ----

export interface Comment {
  id: string
  authorId: string
  authorName: string
  content: string
  mentions: string[]
  likedBy: string[]
  createdAt: string
  updatedAt?: string
}

export interface ChangeRequest {
  id: string
  proposedById: string
  proposedByName: string
  proposedChanges: Record<string, unknown>
  status: 'pending' | 'approved' | 'rejected'
  reviewerId?: string
  reviewerName?: string
  reviewComment?: string
  reviewedAt?: string
  createdAt: string
}

export interface RecordLockStatus {
  locked: boolean
  userId?: string
  displayName?: string
  startedAt?: string
}

// ---- Service ----

export const commentService = {
  async listComments(module: string, entityType: string, entityId: string): Promise<Comment[]> {
    const r = await api.get<Comment[]>(`/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/comments`)
    return r.data
  },

  async createComment(
    module: string,
    entityType: string,
    entityId: string,
    content: string,
    mentions: string[] = []
  ): Promise<Comment> {
    const r = await api.post<Comment>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/comments`,
      { content, mentions }
    )
    return r.data
  },

  async deleteComment(
    module: string,
    entityType: string,
    entityId: string,
    commentId: string
  ): Promise<void> {
    await api.delete(`/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/comments/${encodeURIComponent(commentId)}`)
  },

  async toggleLike(
    module: string,
    entityType: string,
    entityId: string,
    commentId: string
  ): Promise<Comment> {
    const r = await api.post<Comment>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/comments/${encodeURIComponent(commentId)}/like`
    )
    return r.data
  },

  async listChangeRequests(
    module: string,
    entityType: string,
    entityId: string,
    status?: string
  ): Promise<ChangeRequest[]> {
    const params: Record<string, string> = {}
    if (status) params.status = status
    const r = await api.get<ChangeRequest[]>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/change-requests`,
      { params }
    )
    return r.data
  },

  async createChangeRequest(
    module: string,
    entityType: string,
    entityId: string,
    proposedChanges: Record<string, unknown>
  ): Promise<ChangeRequest> {
    const r = await api.post<ChangeRequest>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/change-requests`,
      { proposedChanges }
    )
    return r.data
  },

  async reviewChangeRequest(
    module: string,
    entityType: string,
    entityId: string,
    requestId: string,
    decision: 'approve' | 'reject',
    comment?: string
  ): Promise<ChangeRequest> {
    const r = await api.post<ChangeRequest>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/change-requests/${encodeURIComponent(requestId)}/review`,
      { decision, comment }
    )
    return r.data
  },

  async getLockStatus(
    module: string,
    entityType: string,
    entityId: string
  ): Promise<RecordLockStatus> {
    const r = await api.get<RecordLockStatus>(
      `/odata/${encodeURIComponent(module)}/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}/lock`
    )
    return r.data
  },
}

export default commentService
