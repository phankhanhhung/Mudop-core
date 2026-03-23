export interface TenantUser {
  id: string
  username: string
  email: string
  firstName?: string
  lastName?: string
  isActive: boolean
  roles: string[]
  permissions: string[]
}

export interface CreateUserRequest {
  username: string
  email: string
  password: string
  firstName?: string
  lastName?: string
}

export interface UpdateUserRequest {
  username?: string
  email?: string
  firstName?: string
  lastName?: string
  isActive?: boolean
}

export interface AssignRoleRequest {
  roleName: string
}

export interface AssignPermissionRequest {
  permissionName: string
}
