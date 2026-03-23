export interface Tenant {
  id: string
  code: string
  name: string
  description?: string
  createdAt?: string
}

export interface CreateTenantRequest {
  code: string
  name: string
  description?: string
}

export interface UpdateTenantRequest {
  name?: string
  description?: string
}
