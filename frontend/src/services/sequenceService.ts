import api from './api'
import type { SequenceDefinition, SequenceValue } from '@/types/sequence'

export const sequenceService = {
  async listSequences(tenantId: string, module: string): Promise<SequenceDefinition[]> {
    const response = await api.get<SequenceDefinition[]>(`/v1/${encodeURIComponent(tenantId)}/${encodeURIComponent(module)}/sequences`)
    return response.data
  },

  async getSequence(tenantId: string, module: string, name: string): Promise<SequenceDefinition> {
    const response = await api.get<SequenceDefinition>(`/v1/${encodeURIComponent(tenantId)}/${encodeURIComponent(module)}/sequences/${encodeURIComponent(name)}`)
    return response.data
  },

  async getCurrentValue(tenantId: string, module: string, name: string): Promise<SequenceValue> {
    const response = await api.get<SequenceValue>(`/v1/${encodeURIComponent(tenantId)}/${encodeURIComponent(module)}/sequences/${encodeURIComponent(name)}/current`)
    return response.data
  },

  async getNextValue(tenantId: string, module: string, name: string, companyId?: string): Promise<SequenceValue> {
    const params = companyId ? { companyId } : {}
    const response = await api.post<SequenceValue>(`/v1/${encodeURIComponent(tenantId)}/${encodeURIComponent(module)}/sequences/${encodeURIComponent(name)}/next`, null, { params })
    return response.data
  },

  async resetSequence(tenantId: string, module: string, name: string): Promise<void> {
    await api.post(`/v1/${encodeURIComponent(tenantId)}/${encodeURIComponent(module)}/sequences/${encodeURIComponent(name)}/reset`)
  }
}

export default sequenceService
