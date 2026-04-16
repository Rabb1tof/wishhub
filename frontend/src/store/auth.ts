import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import axios from 'axios'
import type { AuthResponse, AuthResponseRaw, User } from '@/types/auth'

interface AuthState {
  user: User | null
  accessToken: string | null
  refreshToken: string | null
  isRefreshing: boolean
  setAuth: (payload: AuthResponse) => void
  clearAuth: () => void
  refreshTokens: () => Promise<AuthResponse | null>
}

const mapUser = (raw: AuthResponseRaw['User'] | AuthResponse['user']): User => ({
  id: (raw as any).Id ?? (raw as any).id,
  username: (raw as any).Username ?? (raw as any).username,
  displayName: (raw as any).DisplayName ?? (raw as any).displayName,
  avatarUrl: (raw as any).AvatarUrl ?? (raw as any).avatarUrl,
})

const mapAuthResponse = (raw: AuthResponseRaw | AuthResponse): AuthResponse => ({
  accessToken: (raw as any).AccessToken ?? (raw as any).accessToken,
  refreshToken: (raw as any).RefreshToken ?? (raw as any).refreshToken,
  expiresAt: (raw as any).ExpiresAt ?? (raw as any).expiresAt,
  user: mapUser((raw as any).User ?? (raw as any).user),
})

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isRefreshing: false,
      setAuth: (payload) => set({
        user: payload.user,
        accessToken: payload.accessToken,
        refreshToken: payload.refreshToken,
      }),
      clearAuth: () => set({ user: null, accessToken: null, refreshToken: null }),
      refreshTokens: async () => {
        const { refreshToken, isRefreshing } = get()
        if (!refreshToken || isRefreshing) return null

        set({ isRefreshing: true })
        try {
          const response = await axios.post<AuthResponseRaw>('/api/auth/refresh', {
            refreshToken,
          })
          const mapped = mapAuthResponse(response.data)
          set({
            user: mapped.user,
            accessToken: mapped.accessToken,
            refreshToken: mapped.refreshToken,
          })
          return mapped
        } catch (error) {
          set({ user: null, accessToken: null, refreshToken: null })
          return null
        } finally {
          set({ isRefreshing: false })
        }
      },
    }),
    {
      name: 'wishhub-auth',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
      }),
    },
  ),
)

export const mapAuthResponseFromApi = mapAuthResponse
