import api, { tenantManager } from './api'
import type {
  Tenant,
  CreateTenantRequest,
  UpdateTenantRequest
} from '@/types/tenant'

export const tenantService = {
  async getAll(): Promise<Tenant[]> {
    const response = await api.get<Tenant[]>('/tenants')
    return response.data
  },

  async getById(id: string): Promise<Tenant> {
    const response = await api.get<Tenant>(`/tenants/${encodeURIComponent(id)}`)
    return response.data
  },

  async create(data: CreateTenantRequest): Promise<Tenant> {
    const response = await api.post<Tenant>('/tenants', data)
    return response.data
  },

  async update(id: string, data: UpdateTenantRequest): Promise<Tenant> {
    const response = await api.patch<Tenant>(`/tenants/${encodeURIComponent(id)}`, data)
    return response.data
  },

  async delete(id: string): Promise<void> {
    await api.delete(`/tenants/${encodeURIComponent(id)}`)
  },

  selectTenant(tenantId: string): void {
    tenantManager.setTenantId(tenantId)
  },

  getCurrentTenantId(): string | null {
    return tenantManager.getTenantId()
  },

  clearCurrentTenant(): void {
    tenantManager.clearTenantId()
  }
}

export default tenantService
