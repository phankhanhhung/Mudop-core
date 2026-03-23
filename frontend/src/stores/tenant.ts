import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { tenantService } from '@/services'
import type { Tenant, CreateTenantRequest, UpdateTenantRequest } from '@/types/tenant'

export const useTenantStore = defineStore('tenant', () => {
  // State
  const tenants = ref<Tenant[]>([])
  const currentTenant = ref<Tenant | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Getters
  const hasTenant = computed(() => !!currentTenant.value)
  const currentTenantId = computed(() => currentTenant.value?.id ?? null)
  const currentTenantName = computed(() => currentTenant.value?.name ?? '')

  // Actions
  async function fetchTenants(): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      tenants.value = await tenantService.getAll()

      // Restore current tenant from sessionStorage
      const savedTenantId = tenantService.getCurrentTenantId()
      if (savedTenantId) {
        const tenant = tenants.value.find((t) => t.id === savedTenantId)
        if (tenant) {
          currentTenant.value = tenant
        }
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch tenants'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function fetchTenant(id: string): Promise<Tenant> {
    isLoading.value = true
    error.value = null
    try {
      const tenant = await tenantService.getById(id)
      // Update in list if exists
      const index = tenants.value.findIndex((t) => t.id === id)
      if (index >= 0) {
        tenants.value[index] = tenant
      }
      return tenant
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch tenant'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function createTenant(data: CreateTenantRequest): Promise<Tenant> {
    isLoading.value = true
    error.value = null
    try {
      const tenant = await tenantService.create(data)
      tenants.value.push(tenant)
      return tenant
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to create tenant'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function updateTenant(id: string, data: UpdateTenantRequest): Promise<Tenant> {
    isLoading.value = true
    error.value = null
    try {
      const tenant = await tenantService.update(id, data)
      const index = tenants.value.findIndex((t) => t.id === id)
      if (index >= 0) {
        tenants.value[index] = tenant
      }
      if (currentTenant.value?.id === id) {
        currentTenant.value = tenant
      }
      return tenant
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to update tenant'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function deleteTenant(id: string): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      await tenantService.delete(id)
      tenants.value = tenants.value.filter((t) => t.id !== id)
      if (currentTenant.value?.id === id) {
        currentTenant.value = null
        tenantService.clearCurrentTenant()
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to delete tenant'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  function selectTenant(tenant: Tenant): void {
    currentTenant.value = tenant
    tenantService.selectTenant(tenant.id)
  }

  function clearCurrentTenant(): void {
    currentTenant.value = null
    tenantService.clearCurrentTenant()
  }

  function clearError(): void {
    error.value = null
  }

  function reset(): void {
    tenants.value = []
    currentTenant.value = null
    error.value = null
    tenantService.clearCurrentTenant()
  }

  return {
    // State
    tenants,
    currentTenant,
    isLoading,
    error,
    // Getters
    hasTenant,
    currentTenantId,
    currentTenantName,
    // Actions
    fetchTenants,
    fetchTenant,
    createTenant,
    updateTenant,
    deleteTenant,
    selectTenant,
    clearCurrentTenant,
    clearError,
    reset
  }
})
