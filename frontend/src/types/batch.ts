import type { BatchResponse } from './odata'

export type BatchOperationType = 'GET' | 'POST' | 'PATCH' | 'DELETE'

export interface BatchQueueItem {
  id: string
  method: BatchOperationType
  entitySet: string
  entityId?: string
  body?: Record<string, unknown>
  dependsOn?: string[]
  status: 'pending' | 'success' | 'error'
  response?: BatchResponse
}
