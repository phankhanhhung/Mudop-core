import { odataService } from './odataService'
import type { Role, Permission, RolePermission, CreateRoleRequest, UpdateRoleRequest } from '@/types/role'

const MODULE = 'core'

export const roleService = {
  async listRoles(): Promise<Role[]> {
    const response = await odataService.query<Role>(MODULE, 'Role', {
      $orderby: 'Name asc',
      $top: 100
    })
    return response.value
  },

  async getRole(id: string): Promise<Role> {
    return odataService.getById<Role>(MODULE, 'Role', id)
  },

  async createRole(data: CreateRoleRequest): Promise<Role> {
    return odataService.create<Role>(MODULE, 'Role', data)
  },

  async updateRole(id: string, data: UpdateRoleRequest): Promise<Role> {
    return odataService.update<Role>(MODULE, 'Role', id, data)
  },

  async deleteRole(id: string): Promise<void> {
    await odataService.delete(MODULE, 'Role', id)
  },

  async listPermissions(): Promise<Permission[]> {
    const response = await odataService.query<Permission>('platform', 'Permission', {
      $orderby: 'Resource asc, ActionType asc',
      $top: 500
    })
    return response.value
  },

  async getRolePermissions(roleId: string): Promise<RolePermission[]> {
    // Escape roleId to prevent OData filter injection
    const safeId = roleId.replace(/'/g, "''")
    const response = await odataService.query<RolePermission>(MODULE, 'RolePermission', {
      $filter: `RoleId eq '${safeId}'`,
      $top: 500
    })
    return response.value
  },

  async assignPermission(roleId: string, permissionId: string): Promise<RolePermission> {
    return odataService.create<RolePermission>(MODULE, 'RolePermission', {
      RoleId: roleId,
      PermissionId: permissionId
    })
  },

  async removePermission(rolePermissionId: string): Promise<void> {
    await odataService.delete(MODULE, 'RolePermission', rolePermissionId)
  }
}

export default roleService
