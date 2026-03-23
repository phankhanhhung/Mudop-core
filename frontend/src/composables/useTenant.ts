import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useMetadataStore } from '@/stores/metadata'
import { cancelAllPending } from '@/utils/requestDedup'
import type { Tenant, CreateTenantRequest } from '@/types/tenant'

function isValidRedirect(url: string): boolean {
  if (!url) return false
  try {
    const decoded = decodeURIComponent(url)
    if (!decoded.startsWith('/') || decoded.startsWith('//') || decoded.includes('://')) {
      return false
    }
    const parsed = new URL(decoded, window.location.origin)
    return parsed.origin === window.location.origin
  } catch {
    return false
  }
}

export function useTenant() {
  const router = useRouter()
  const route = useRoute()
  const tenantStore = useTenantStore()
  const metadataStore = useMetadataStore()

  const tenants = computed(() => tenantStore.tenants)
  const currentTenant = computed(() => tenantStore.currentTenant)
  const hasTenant = computed(() => tenantStore.hasTenant)
  const isLoading = computed(() => tenantStore.isLoading)
  const error = computed(() => tenantStore.error)

  async function fetchTenants(): Promise<void> {
    await tenantStore.fetchTenants()
  }

  async function createTenant(data: CreateTenantRequest): Promise<Tenant> {
    const tenant = await tenantStore.createTenant(data)
    return tenant
  }

  function selectTenant(tenant: Tenant): void {
    tenantStore.selectTenant(tenant)

    // Cancel in-flight OData queries for the old tenant before clearing cache
    cancelAllPending()

    // Clear metadata cache when switching tenants and reload
    metadataStore.clearCache()
    metadataStore.fetchModules().catch(() => {
      // Silently fail - sidebar will just be empty
    })

    // Redirect to intended destination or dashboard
    const redirect = route.query.redirect as string
    router.push(isValidRedirect(redirect) ? redirect : '/dashboard')
  }

  function clearTenant(): void {
    cancelAllPending()
    tenantStore.clearCurrentTenant()
    metadataStore.clearCache()
    router.push('/tenants')
  }

  function clearError(): void {
    tenantStore.clearError()
  }

  return {
    tenants,
    currentTenant,
    hasTenant,
    isLoading,
    error,
    fetchTenants,
    createTenant,
    selectTenant,
    clearTenant,
    clearError
  }
}
