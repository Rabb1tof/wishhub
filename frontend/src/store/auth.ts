import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, AuthResponseRaw, User } from '@/types/auth'

interface AuthState {
  user: User | null
  accessToken: string | null
  refreshToken: string | null
  sessionVersion: number
  setAuth: (payload: AuthResponse) => void
  clearAuth: () => void
  refreshAuth: (payload: AuthResponse, expectedVersion: number) => boolean
}

export const mapAuthResponseFromApi = (raw: AuthResponseRaw | AuthResponse): AuthResponse => {
  if ('accessToken' in raw) return raw
  return {
    accessToken: raw.AccessToken,
    refreshToken: raw.RefreshToken,
    expiresAt: raw.ExpiresAt,
    user: { id: raw.User.Id, username: raw.User.Username, displayName: raw.User.DisplayName, avatarUrl: raw.User.AvatarUrl },
  }
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      sessionVersion: 0,
      // A new login is a boundary even when the account id is unchanged.
      setAuth: ({ user, accessToken, refreshToken }) => set(state => ({
        user, accessToken, refreshToken, sessionVersion: state.sessionVersion + 1,
      })),
      clearAuth: () => set(state => ({
        user: null, accessToken: null, refreshToken: null, sessionVersion: state.sessionVersion + 1,
      })),
      refreshAuth: ({ user, accessToken, refreshToken }, expectedVersion) => {
        const current = get()
        if (current.sessionVersion !== expectedVersion || current.user?.id !== user.id) return false
        set({ accessToken, refreshToken })
        return true
      },
    }),
    {
      name: 'wishhub-auth',
      partialize: ({ user, accessToken, refreshToken }) => ({ user, accessToken, refreshToken }),
    },
  ),
)
