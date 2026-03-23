import api, { tokenManager, tenantManager } from './api'
import { clearODataCaches } from './odataService'
import type {
  LoginRequest,
  RegisterRequest,
  AuthResponse,
  User,
  ChangePasswordRequest,
  ExternalLoginRequest
} from '@/types/auth'

export const authService = {
  async login(credentials: LoginRequest): Promise<AuthResponse> {
    const response = await api.post<AuthResponse>('/auth/login', credentials)
    const { accessToken, refreshToken } = response.data
    tokenManager.setTokens(accessToken, refreshToken)
    return response.data
  },

  async register(data: RegisterRequest): Promise<AuthResponse> {
    const response = await api.post<AuthResponse>('/auth/register', data)
    const { accessToken, refreshToken } = response.data
    tokenManager.setTokens(accessToken, refreshToken)
    return response.data
  },

  async logout(): Promise<void> {
    try {
      await api.post('/auth/logout')
    } catch {
      // Ignore logout errors
    } finally {
      tokenManager.clearTokens()
      tenantManager.clearTenantId()
      clearODataCaches()
    }
  },

  async getCurrentUser(): Promise<User> {
    const response = await api.get<User>('/auth/me')
    return response.data
  },

  async changePassword(data: ChangePasswordRequest): Promise<void> {
    await api.post('/auth/change-password', data)
  },

  async refreshToken(): Promise<AuthResponse> {
    const refreshToken = tokenManager.getRefreshToken()
    if (!refreshToken) {
      throw new Error('No refresh token available')
    }

    const response = await api.post<AuthResponse>('/auth/refresh', { refreshToken })
    const { accessToken, refreshToken: newRefreshToken } = response.data
    tokenManager.setTokens(accessToken, newRefreshToken)
    return response.data
  },

  async externalLogin(data: ExternalLoginRequest): Promise<AuthResponse> {
    const response = await api.post<AuthResponse>('/auth/external-login', data)
    const { accessToken, refreshToken } = response.data
    tokenManager.setTokens(accessToken, refreshToken)
    return response.data
  },

  async linkProvider(data: ExternalLoginRequest): Promise<void> {
    await api.post('/auth/link-provider', data)
  },

  isAuthenticated(): boolean {
    return tokenManager.hasTokens()
  }
}

export default authService
