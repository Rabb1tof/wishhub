import { useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import { useAuthStore, mapAuthResponseFromApi } from '@/store/auth'
import { requestNotificationPermission } from '@/utils/notifications'
import { stopConnection } from '@/hooks/useSignalR'
import type { AuthResponseRaw, LoginRequest, RegisterRequest } from '@/types/auth'

const AUTH_KEY = 'auth'

export function useLogin() {
  const queryClient = useQueryClient()
  const setAuth = useAuthStore((s) => s.setAuth)

  return useMutation({
    mutationFn: async (data: LoginRequest) => {
      const res = await api.post<AuthResponseRaw>('/auth/login', data)
      return mapAuthResponseFromApi(res.data)
    },
    onSuccess: (data) => {
      setAuth(data)
      queryClient.setQueryData([AUTH_KEY], data.user)
      requestNotificationPermission()
    },
  })
}

export function useRegister() {
  const queryClient = useQueryClient()
  const setAuth = useAuthStore((s) => s.setAuth)

  return useMutation({
    mutationFn: async (data: RegisterRequest) => {
      const res = await api.post<AuthResponseRaw>('/auth/register', data)
      return mapAuthResponseFromApi(res.data)
    },
    onSuccess: (data) => {
      setAuth(data)
      queryClient.setQueryData([AUTH_KEY], data.user)
      requestNotificationPermission()
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  const clearAuth = useAuthStore((s) => s.clearAuth)

  return useMutation({
    mutationFn: async () => {
      await api.post('/auth/logout')
    },
    onSuccess: () => {
      stopConnection()
      clearAuth()
      queryClient.clear()
    },
  })
}
