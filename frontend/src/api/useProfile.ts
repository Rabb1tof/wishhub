import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import { useAuthStore } from '@/store/auth'

interface UserProfileDto {
  id: string
  username: string
  displayName: string
  bio?: string
  avatarUrl?: string
  createdAt: string
  wishlistPrivacy: string
  friendshipStatus?: 'pending' | 'accepted' | 'blocked' | null
  wishlistItemCount: number
  priceRefreshIntervalHours: number
}

interface UpdateProfileRequest {
  displayName?: string
  bio?: string
  wishlistPrivacy?: 'public' | 'friendsOnly' | 'private'
  priceRefreshIntervalHours?: number
}

interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

const PROFILE_KEY = 'profile'
const ME_KEY = 'me'

export function useProfile(username?: string) {
  return useQuery({
    queryKey: [PROFILE_KEY, username],
    queryFn: async () => {
      if (!username) throw new Error('Username is required')
      const res = await api.get<UserProfileDto>(`/users/${username}`)
      return res.data
    },
    enabled: !!username,
  })
}

export function useMyProfile() {
  return useQuery({
    queryKey: [PROFILE_KEY, ME_KEY],
    queryFn: async () => {
      const res = await api.get<UserProfileDto>('/users/me')
      return res.data
    },
  })
}

export function useUpdateProfile() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: UpdateProfileRequest) => {
      const res = await api.put<UserProfileDto>('/users/me', data)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [PROFILE_KEY, ME_KEY] })
    },
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: async (data: ChangePasswordRequest) => {
      await api.post('/users/me/password', data)
    },
  })
}

export function useUploadAvatar() {
  const queryClient = useQueryClient()
  const user = useAuthStore((s) => s.user)
  const setAuth = useAuthStore((s) => s.setAuth)
  const accessToken = useAuthStore((s) => s.accessToken)
  const refreshToken = useAuthStore((s) => s.refreshToken)
  const expiresAt = String(Date.now() + 3600 * 1000)

  return useMutation({
    mutationFn: async (file: File) => {
      const formData = new FormData()
      formData.append('file', file)
      const res = await api.post<{ avatarUrl: string }>('/users/me/avatar', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      return res.data.avatarUrl
    },
    onSuccess: (avatarUrl) => {
      queryClient.invalidateQueries({ queryKey: [PROFILE_KEY, ME_KEY] })
      // Обновляем аватар в Zustand store для Layout и других компонентов
      if (user && accessToken && refreshToken) {
        setAuth({
          user: { ...user, avatarUrl },
          accessToken,
          refreshToken,
          expiresAt,
        })
      }
    },
  })
}

export function useSearchUsers() {
  return useMutation({
    mutationFn: async (query: string) => {
      if (query.length < 2) return []
      const res = await api.get<UserProfileDto[]>(`/users/search?q=${encodeURIComponent(query)}`)
      return res.data
    },
  })
}
