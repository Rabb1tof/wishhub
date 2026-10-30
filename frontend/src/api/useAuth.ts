import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CanceledError, isAxiosError } from 'axios'
import api from './client'
import { useAuthStore, mapAuthResponseFromApi } from '@/store/auth'
import { requestNotificationPermission } from '@/utils/notifications'
import { stopConnection } from '@/hooks/useSignalR'
import type { AuthResponseRaw, LoginRequest, RegisterRequest } from '@/types/auth'

async function signIn(endpoint: string, data: LoginRequest | RegisterRequest) {
  const version = useAuthStore.getState().sessionVersion
  const response = await api.post<AuthResponseRaw>(endpoint, data)
  if (useAuthStore.getState().sessionVersion !== version) throw new CanceledError('Session changed')
  const auth = mapAuthResponseFromApi(response.data)
  useAuthStore.getState().setAuth(auth)
  requestNotificationPermission()
  return auth
}

export function useLogin() {
  return useMutation({ mutationFn: (data: LoginRequest) => signIn('/auth/login', data) })
}

export function useRegister() {
  return useMutation({ mutationFn: (data: RegisterRequest) => signIn('/auth/register', data) })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const version = useAuthStore.getState().sessionVersion
      try {
        await api.post('/auth/logout')
      } catch (error) {
        // An expired access token must not prevent an explicit local sign-out.
        if (!isAxiosError(error) || error.response?.status !== 401) throw error
      }
      if (useAuthStore.getState().sessionVersion !== version) throw new CanceledError('Session changed')
      await stopConnection()
      if (useAuthStore.getState().sessionVersion !== version) throw new CanceledError('Session changed')
      if (!useAuthStore.getState().clearAuth(version)) throw new CanceledError('Session changed')
      queryClient.clear()
    },
  })
}
