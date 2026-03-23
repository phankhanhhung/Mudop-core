import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Mock external dependencies
const mockLogin = vi.fn()
const mockLogout = vi.fn()
const mockGetCurrentUser = vi.fn()
const mockRefreshToken = vi.fn()
const mockRegister = vi.fn()
const mockHasTokens = vi.fn()
const mockClearTokens = vi.fn()
const mockCancelAllPending = vi.fn()

vi.mock('@/services', () => ({
  authService: {
    login: (...args: unknown[]) => mockLogin(...args),
    logout: (...args: unknown[]) => mockLogout(...args),
    getCurrentUser: (...args: unknown[]) => mockGetCurrentUser(...args),
    refreshToken: (...args: unknown[]) => mockRefreshToken(...args),
    register: (...args: unknown[]) => mockRegister(...args)
  },
  tokenManager: {
    hasTokens: () => mockHasTokens(),
    clearTokens: () => mockClearTokens()
  }
}))

vi.mock('@/utils/requestDedup', () => ({
  cancelAllPending: () => mockCancelAllPending()
}))

import { useAuthStore } from '../auth'

describe('useAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockHasTokens.mockReturnValue(false)
  })

  describe('initial state', () => {
    it('has null user', () => {
      const store = useAuthStore()
      expect(store.user).toBeNull()
    })

    it('has isLoading false', () => {
      const store = useAuthStore()
      expect(store.isLoading).toBe(false)
    })

    it('has null error', () => {
      const store = useAuthStore()
      expect(store.error).toBeNull()
    })

    it('isAuthenticated is false when no user and no tokens', () => {
      const store = useAuthStore()
      expect(store.isAuthenticated).toBe(false)
    })

    it('userRoles is empty array when no user', () => {
      const store = useAuthStore()
      expect(store.userRoles).toEqual([])
    })

    it('displayName is empty string when no user', () => {
      const store = useAuthStore()
      expect(store.displayName).toBe('')
    })
  })

  describe('login', () => {
    it('sets user on successful login', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'alice@test.com', roles: ['admin'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.user).toEqual(mockUser)
      expect(store.isLoading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('sets isLoading during login', async () => {
      let resolveLogin!: (v: unknown) => void
      mockLogin.mockReturnValue(new Promise((r) => { resolveLogin = r }))

      const store = useAuthStore()
      const promise = store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.isLoading).toBe(true)

      resolveLogin({ user: { id: '1', username: 'alice', email: 'a@t.com', roles: [] } })
      await promise

      expect(store.isLoading).toBe(false)
    })

    it('sets error on failed login', async () => {
      mockLogin.mockRejectedValue(new Error('Invalid credentials'))

      const store = useAuthStore()
      await expect(store.login({ usernameOrEmail: 'alice', password: 'wrong' }))
        .rejects.toThrow('Invalid credentials')

      expect(store.user).toBeNull()
      expect(store.error).toBe('Invalid credentials')
      expect(store.isLoading).toBe(false)
    })

    it('computes isAuthenticated as true after login with tokens', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: [] }
      mockLogin.mockResolvedValue({ user: mockUser })
      mockHasTokens.mockReturnValue(true)

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.isAuthenticated).toBe(true)
    })

    it('computes displayName from username', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'alice@test.com', roles: [] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.displayName).toBe('alice')
    })

    it('computes displayName from email when username is empty', async () => {
      const mockUser = { id: '1', username: '', email: 'alice@test.com', roles: [] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice@test.com', password: 'pass' })

      expect(store.displayName).toBe('alice@test.com')
    })

    it('computes userRoles from user', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: ['admin', 'editor'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.userRoles).toEqual(['admin', 'editor'])
    })
  })

  describe('logout', () => {
    it('clears user on logout', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: [] }
      mockLogin.mockResolvedValue({ user: mockUser })
      mockLogout.mockResolvedValue(undefined)

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })
      expect(store.user).toBeTruthy()

      await store.logout()
      expect(store.user).toBeNull()
    })

    it('cancels all pending requests on logout', async () => {
      mockLogout.mockResolvedValue(undefined)

      const store = useAuthStore()
      await store.logout()

      expect(mockCancelAllPending).toHaveBeenCalledOnce()
    })

    it('clears user even if authService.logout throws', async () => {
      mockLogout.mockRejectedValue(new Error('network error'))

      const store = useAuthStore()
      // Set user manually
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: [] }
      mockLogin.mockResolvedValue({ user: mockUser })
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      // logout will re-throw the error, but user should still be cleared via finally
      await store.logout().catch(() => {})
      expect(store.user).toBeNull()
      expect(store.isLoading).toBe(false)
    })
  })

  describe('hasRole', () => {
    it('returns true if user has the role', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: ['admin', 'editor'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.hasRole('admin')).toBe(true)
    })

    it('returns false if user does not have the role', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: ['editor'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.hasRole('admin')).toBe(false)
    })
  })

  describe('hasAnyRole', () => {
    it('returns true if user has any of the specified roles', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: ['editor'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.hasAnyRole(['admin', 'editor'])).toBe(true)
    })

    it('returns false if user has none of the specified roles', async () => {
      const mockUser = { id: '1', username: 'alice', email: 'a@t.com', roles: ['viewer'] }
      mockLogin.mockResolvedValue({ user: mockUser })

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'alice', password: 'pass' })

      expect(store.hasAnyRole(['admin', 'editor'])).toBe(false)
    })
  })

  describe('clearError', () => {
    it('clears the error state', async () => {
      mockLogin.mockRejectedValue(new Error('fail'))

      const store = useAuthStore()
      await store.login({ usernameOrEmail: 'a', password: 'b' }).catch(() => {})

      expect(store.error).toBe('fail')
      store.clearError()
      expect(store.error).toBeNull()
    })
  })
})
