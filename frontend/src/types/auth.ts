export interface User {
  id: string
  username: string
  displayName: string
  avatarUrl?: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: User
}

export interface AuthResponseRaw {
  AccessToken: string
  RefreshToken: string
  ExpiresAt: string
  User: {
    Id: string
    Username: string
    DisplayName: string
    AvatarUrl?: string
  }
}

export interface LoginRequest {
  usernameOrEmail: string
  password: string
}

export interface RegisterRequest {
  username: string
  email: string
  displayName: string
  password: string
}
