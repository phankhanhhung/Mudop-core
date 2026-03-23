import axios, { type AxiosInstance, type AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import type { AuthResponse } from '@/types/auth'
import { parseODataError, type ParsedODataError } from '@/utils/odataErrorParser'

/**
 * Custom error class for OData API errors.
 * Extends Error with structured fields parsed from OData error responses.
 */
export class ODataApiError extends Error {
  readonly code?: string
  readonly fieldErrors: Record<string, string[]>
  readonly isValidationError: boolean
  readonly isNetworkError: boolean
  readonly isTimeout: boolean
  readonly status?: number

  constructor(parsed: ParsedODataError) {
    super(parsed.message)
    this.name = 'ODataApiError'
    this.code = parsed.code
    this.fieldErrors = parsed.fieldErrors
    this.isValidationError = parsed.isValidationError
    this.isNetworkError = parsed.isNetworkError
    this.isTimeout = parsed.isTimeout
    this.status = parsed.status

    // Preserve proper stack trace in V8 environments
    if (Error.captureStackTrace) {
      Error.captureStackTrace(this, ODataApiError)
    }
  }
}

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

// Token storage keys
const ACCESS_TOKEN_KEY = 'bmmdl_access_token'
const REFRESH_TOKEN_KEY = 'bmmdl_refresh_token'

// Create axios instance
export const api: AxiosInstance = axios.create({
  baseURL: BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  },
  timeout: 30000
})

// Migrate tokens from localStorage to sessionStorage (one-time upgrade helper)
function migrateTokenStorage(): void {
  for (const key of [ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY]) {
    const legacy = localStorage.getItem(key)
    if (legacy) {
      if (!sessionStorage.getItem(key)) {
        sessionStorage.setItem(key, legacy)
      }
      localStorage.removeItem(key)
    }
  }
}
migrateTokenStorage()

// Token management
export const tokenManager = {
  getAccessToken(): string | null {
    return sessionStorage.getItem(ACCESS_TOKEN_KEY)
  },

  getRefreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_TOKEN_KEY)
  },

  setTokens(accessToken: string, refreshToken: string): void {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken)
    sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken)
  },

  clearTokens(): void {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY)
    sessionStorage.removeItem(REFRESH_TOKEN_KEY)
  },

  hasTokens(): boolean {
    return !!this.getAccessToken() && !!this.getRefreshToken()
  }
}

// Tenant ID management
const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const TENANT_ID_KEY = 'bmmdl_tenant_id'
let currentTenantId: string | null = null

// Migrate tenant ID from localStorage to sessionStorage (one-time upgrade helper)
function migrateTenantStorage(): void {
  const legacy = localStorage.getItem(TENANT_ID_KEY)
  if (legacy) {
    if (!sessionStorage.getItem(TENANT_ID_KEY)) {
      sessionStorage.setItem(TENANT_ID_KEY, legacy)
    }
    localStorage.removeItem(TENANT_ID_KEY)
  }
}
migrateTenantStorage()

export const tenantManager = {
  getTenantId(): string | null {
    if (!currentTenantId) {
      const stored = sessionStorage.getItem(TENANT_ID_KEY)
      if (stored && UUID_REGEX.test(stored)) {
        currentTenantId = stored
      } else if (stored) {
        // Invalid format in storage, remove it
        sessionStorage.removeItem(TENANT_ID_KEY)
      }
    }
    return currentTenantId
  },

  setTenantId(tenantId: string | null): void {
    if (tenantId && !UUID_REGEX.test(tenantId)) {
      console.warn('Invalid tenant ID format, ignoring')
      return
    }
    currentTenantId = tenantId
    if (tenantId) {
      sessionStorage.setItem(TENANT_ID_KEY, tenantId)
    } else {
      sessionStorage.removeItem(TENANT_ID_KEY)
    }
  },

  clearTenantId(): void {
    currentTenantId = null
    sessionStorage.removeItem(TENANT_ID_KEY)
  }
}

// Request interceptor - add auth token and tenant ID
api.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = tokenManager.getAccessToken()
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }

    const tenantId = tenantManager.getTenantId()
    if (tenantId) {
      config.headers['X-Tenant-Id'] = tenantId
    }

    return config
  },
  (error) => Promise.reject(error)
)

// Response interceptor - handle token refresh
// Shared promise eliminates race conditions: all concurrent 401s await the same refresh
let refreshPromise: Promise<string> | null = null

function refreshAccessToken(): Promise<string> {
  const refreshToken = tokenManager.getRefreshToken()
  if (!refreshToken) {
    tokenManager.clearTokens()
    tenantManager.clearTenantId()
    return Promise.reject(new Error('No refresh token available'))
  }

  return axios.post<AuthResponse>(`${BASE_URL}/auth/refresh`, {
    refreshToken
  }).then((response) => {
    const { accessToken, refreshToken: newRefreshToken } = response.data

    // Validate token structure before storing
    if (!accessToken || accessToken.split('.').length !== 3 || !newRefreshToken) {
      throw new Error('Invalid token format in refresh response')
    }

    tokenManager.setTokens(accessToken, newRefreshToken)
    return accessToken
  }).catch((err) => {
    tokenManager.clearTokens()
    tenantManager.clearTenantId()
    throw err
  })
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean }

    // If error is 401 and we haven't retried yet
    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true

      try {
        if (!refreshPromise) {
          refreshPromise = refreshAccessToken().finally(() => { refreshPromise = null })
        }
        const newToken = await refreshPromise

        originalRequest.headers.Authorization = `Bearer ${newToken}`
        return api(originalRequest)
      } catch (refreshError) {
        return Promise.reject(refreshError)
      }
    }

    // Parse non-401 errors into structured ODataApiError
    const parsed = parseODataError(error)
    return Promise.reject(new ODataApiError(parsed))
  }
)

// Auto-retry for transient failures (GET only)
const MAX_RETRIES = 2
const BACKOFF_MS = [1000, 3000]

function isRetryable(error: AxiosError): boolean {
  // Only retry GET requests
  if (error.config?.method && error.config.method.toLowerCase() !== 'get') {
    return false
  }
  // Retry on 503 Service Unavailable
  if (error.response?.status === 503) {
    return true
  }
  // Retry on network errors (no response)
  if (!error.response && (error.code === 'ECONNREFUSED' || error.code === 'ECONNABORTED' || error.message?.includes('Network Error'))) {
    return true
  }
  return false
}

api.interceptors.response.use(
  (response: AxiosResponse) => response,
  async (error: AxiosError) => {
    const config = error.config as InternalAxiosRequestConfig & { _retryCount?: number }
    if (!config || !isRetryable(error)) {
      return Promise.reject(error)
    }

    config._retryCount = config._retryCount || 0
    if (config._retryCount >= MAX_RETRIES) {
      return Promise.reject(error)
    }

    config._retryCount += 1
    const delay = BACKOFF_MS[config._retryCount - 1] || BACKOFF_MS[BACKOFF_MS.length - 1]
    await new Promise(resolve => setTimeout(resolve, delay))
    return api(config)
  }
)

export default api
