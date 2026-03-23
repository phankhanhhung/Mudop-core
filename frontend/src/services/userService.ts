import api from './api'
import type {
  TenantUser,
  CreateUserRequest,
  UpdateUserRequest,
  AssignRoleRequest,
  AssignPermissionRequest
} from '@/types/user'

export const userService = {
  async listUsers(tenantId: string): Promise<TenantUser[]> {
    const response = await api.get<TenantUser[]>(`/tenants/${encodeURIComponent(tenantId)}/users`)
    return response.data
  },

  async getUser(tenantId: string, userId: string): Promise<TenantUser> {
    const response = await api.get<TenantUser>(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}`)
    return response.data
  },

  async createUser(tenantId: string, data: CreateUserRequest): Promise<TenantUser> {
    const response = await api.post<TenantUser>(`/tenants/${encodeURIComponent(tenantId)}/users`, data)
    return response.data
  },

  async updateUser(tenantId: string, userId: string, data: UpdateUserRequest): Promise<TenantUser> {
    const response = await api.put<TenantUser>(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}`, data)
    return response.data
  },

  async deleteUser(tenantId: string, userId: string): Promise<void> {
    await api.delete(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}`)
  },

  async assignRole(tenantId: string, userId: string, data: AssignRoleRequest): Promise<void> {
    await api.post(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}/roles`, data)
  },

  async removeRole(tenantId: string, userId: string, roleName: string): Promise<void> {
    await api.delete(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}/roles/${encodeURIComponent(roleName)}`)
  },

  async assignPermission(tenantId: string, userId: string, data: AssignPermissionRequest): Promise<void> {
    await api.post(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}/permissions`, data)
  },

  async removePermission(tenantId: string, userId: string, permissionName: string): Promise<void> {
    await api.delete(`/tenants/${encodeURIComponent(tenantId)}/users/${encodeURIComponent(userId)}/permissions/${encodeURIComponent(permissionName)}`)
  },

  async listPermissions(tenantId: string): Promise<{ Id: string; Name: string; Description?: string; IsSystemRole?: boolean }[]> {
    const response = await api.get(`/tenants/${encodeURIComponent(tenantId)}/users/permissions`)
    return response.data
  }
}

export default userService
