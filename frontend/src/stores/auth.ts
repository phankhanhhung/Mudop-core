import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authService, tokenManager } from '@/services'
import { cancelAllPending } from '@/utils/requestDedup'
import type { User, LoginRequest, RegisterRequest } from '@/types/auth'

// Shared promise that resolves once auth initialization is complete.
// The router guard awaits this before making routing decisions on first load.
let _authReadyResolve: () => void
const authReady = new Promise<void>((resolve) => {
  _authReadyResolve = resolve
})

/** Wait for auth initialization to complete (used by the router guard). */
export function waitForAuthReady(): Promise<void> {
  return authReady
}

export const useAuthStore = defineStore('auth', () => {
  // State
  const user = ref<User | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Getters
  const isAuthenticated = computed(() => !!user.value && tokenManager.hasTokens())
  const userRoles = computed(() => user.value?.roles ?? [])
  const displayName = computed(() => {
    if (!user.value) return ''
    return user.value.username || user.value.email
  })

  // Actions
  async function login(credentials: LoginRequest): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const response = await authService.login(credentials)
      user.value = response.user
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Login failed'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function register(data: RegisterRequest): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const response = await authService.register(data)
      user.value = response.user
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Registration failed'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function logout(): Promise<void> {
    isLoading.value = true
    // Cancel all in-flight API requests to prevent stale responses
    cancelAllPending()
    try {
      await authService.logout()
    } finally {
      user.value = null
      isLoading.value = false
    }
  }

  async function fetchUser(): Promise<void> {
    if (!tokenManager.hasTokens()) {
      user.value = null
      return
    }

    isLoading.value = true
    error.value = null
    try {
      user.value = await authService.getCurrentUser()
    } catch (e) {
      // getCurrentUser failed — the axios interceptor already attempted
      // a token refresh (and retried) for 401 responses.  If we still
      // ended up here the tokens are genuinely invalid.
      user.value = null
      tokenManager.clearTokens()
      error.value = e instanceof Error ? e.message : 'Failed to fetch user'
    } finally {
      isLoading.value = false
    }
  }

  async function initialize(): Promise<void> {
    try {
      if (tokenManager.hasTokens()) {
        await fetchUser()
      }
    } finally {
      // Always resolve — the router guard will check isAuthenticated
      _authReadyResolve()
    }
  }

  function hasRole(role: string): boolean {
    return userRoles.value.includes(role)
  }

  function hasAnyRole(roles: string[]): boolean {
    return roles.some((role) => userRoles.value.includes(role))
  }

  function clearError(): void {
    error.value = null
  }

  return {
    // State
    user,
    isLoading,
    error,
    // Getters
    isAuthenticated,
    userRoles,
    displayName,
    // Actions
    login,
    register,
    logout,
    fetchUser,
    initialize,
    hasRole,
    hasAnyRole,
    clearError
  }
})
