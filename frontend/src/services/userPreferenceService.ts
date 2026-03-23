import { api } from './api'

export interface UserPreference {
  id: string
  category: string
  entityKey: string
  name: string
  isDefault: boolean
  settings: Record<string, unknown>
  createdAt: string
  updatedAt: string
}

export interface CreatePreferenceRequest {
  category: string
  entityKey: string
  name: string
  isDefault: boolean
  settings: Record<string, unknown>
}

export interface UpdatePreferenceRequest {
  name?: string
  isDefault?: boolean
  settings?: Record<string, unknown>
}

export const userPreferenceService = {
  async list(category: string, entityKey: string): Promise<UserPreference[]> {
    const { data } = await api.get<UserPreference[]>('/user-preferences', {
      params: { category, entityKey },
    })
    return data
  },

  async listByCategory(category: string): Promise<UserPreference[]> {
    const { data } = await api.get<UserPreference[]>('/user-preferences/by-category', {
      params: { category },
    })
    return data
  },

  async create(request: CreatePreferenceRequest): Promise<UserPreference> {
    const { data } = await api.post<UserPreference>('/user-preferences', request)
    return data
  },

  async update(id: string, request: UpdatePreferenceRequest): Promise<UserPreference> {
    const { data } = await api.put<UserPreference>(`/user-preferences/${encodeURIComponent(id)}`, request)
    return data
  },

  async remove(id: string): Promise<void> {
    await api.delete(`/user-preferences/${encodeURIComponent(id)}`)
  },

  async setDefault(id: string): Promise<void> {
    await api.put(`/user-preferences/${encodeURIComponent(id)}/default`)
  },
}
