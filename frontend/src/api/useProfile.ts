import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import { CanceledError } from 'axios'
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
}

interface UpdateProfileRequest {
  displayName?: string
  bio?: string
  wishlistPrivacy?: 'public' | 'friendsOnly' | 'private'
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

export function useMyProfile(enabled = true) {
  return useQuery({
    queryKey: [PROFILE_KEY, ME_KEY],
    enabled,
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
      const version = useAuthStore.getState().sessionVersion
      await api.post('/users/me/password', data)
      if (useAuthStore.getState().sessionVersion !== version) throw new CanceledError('Session changed')
      if (!useAuthStore.getState().clearAuth(version)) throw new CanceledError('Session changed')
    },
  })
}

export function useUploadAvatar() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (file: File) => {
      const version = useAuthStore.getState().sessionVersion
      const formData = new FormData()
      formData.append('file', file)
      const res = await api.post<{ avatarUrl: string }>('/users/me/avatar', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      const current = useAuthStore.getState()
      if (current.sessionVersion !== version) throw new CanceledError('Session changed')
      if (current.user) useAuthStore.setState({ user: { ...current.user, avatarUrl: res.data.avatarUrl } })
      return res.data.avatarUrl
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [PROFILE_KEY, ME_KEY] })
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
