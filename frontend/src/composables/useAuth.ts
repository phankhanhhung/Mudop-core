import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { useMetadataStore } from '@/stores/metadata'
import type { LoginRequest, RegisterRequest } from '@/types/auth'

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

export function useAuth() {
  const router = useRouter()
  const route = useRoute()
  const authStore = useAuthStore()
  const tenantStore = useTenantStore()
  const metadataStore = useMetadataStore()

  const user = computed(() => authStore.user)
  const isAuthenticated = computed(() => authStore.isAuthenticated)
  const isLoading = computed(() => authStore.isLoading)
  const error = computed(() => authStore.error)

  async function login(credentials: LoginRequest): Promise<void> {
    await authStore.login(credentials)

    // Fetch tenants and module metadata after login
    try {
      await tenantStore.fetchTenants()

      // Auto-select first tenant if none is selected (ensures X-Tenant-Id header on reload)
      if (!tenantStore.hasTenant && tenantStore.tenants.length > 0) {
        tenantStore.selectTenant(tenantStore.tenants[0])
      }

      await metadataStore.fetchModules()
    } catch {
      // Ignore fetch errors, user may not have any tenants yet
    }

    // Redirect to intended destination or dashboard
    const redirect = route.query.redirect as string
    await router.push(isValidRedirect(redirect) ? redirect : '/dashboard')
  }

  async function register(data: RegisterRequest): Promise<void> {
    await authStore.register(data)

    // Redirect to tenants to create first tenant
    await router.push('/tenants')
  }

  async function logout(): Promise<void> {
    await authStore.logout()
    tenantStore.reset()
    await router.push('/auth/login')
  }

  function clearError(): void {
    authStore.clearError()
  }

  function hasRole(role: string): boolean {
    return authStore.hasRole(role)
  }

  function hasAnyRole(roles: string[]): boolean {
    return authStore.hasAnyRole(roles)
  }

  return {
    user,
    isAuthenticated,
    isLoading,
    error,
    login,
    register,
    logout,
    clearError,
    hasRole,
    hasAnyRole
  }
}
