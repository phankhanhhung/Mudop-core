import api from './api'

export type AiOperation = 'complete' | 'generate' | 'review' | 'explain-error'

export interface AiAssistRequest {
  operation: AiOperation
  context: string
  prompt?: string
  error?: string
  cursorLine?: number
  cursorColumn?: number
}

export interface AiAssistResponse {
  result: string
  suggestions?: string[]
}

export interface AiStatus {
  configured: boolean
  model: string
}

export const aiService = {
  async getStatus(): Promise<AiStatus> {
    try {
      const resp = await api.get<AiStatus>('/ai/status')
      return resp.data
    } catch {
      return { configured: false, model: '' }
    }
  },

  async assist(request: AiAssistRequest): Promise<AiAssistResponse> {
    const response = await api.post<AiAssistResponse>('/ai/assist', request)
    return response.data
  },
}

export default aiService
