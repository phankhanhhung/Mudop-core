export interface Role {
  Id: string
  Name: string
  Description?: string
  IsSystem?: boolean
  CreatedAt?: string
}

export interface Permission {
  Id: string
  Name: string
  Description?: string
  Resource: string
  ActionType: string
}

export interface RolePermission {
  Id: string
  RoleId: string
  PermissionId: string
}

export interface CreateRoleRequest {
  Name: string
  Description?: string
}

export interface UpdateRoleRequest {
  Name?: string
  Description?: string
}
