export interface LoginRequest {
  usernameOrEmail: string
  password: string
}

export interface RegisterRequest {
  email: string
  password: string
  displayName?: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number
  user: User
  isNewUser?: boolean
}

export interface User {
  id: string
  username: string
  email: string
  tenantId?: string
  roles: string[]
}

export interface RefreshTokenRequest {
  refreshToken: string
}

export interface ExternalLoginRequest {
  provider: string
  idToken: string
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}
