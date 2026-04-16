import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import api from './client'
import { useAuthStore, mapAuthResponseFromApi } from '@/store/auth'
import type { AuthResponseRaw } from '@/types/auth'
import type { TelegramAuthData } from '@/components/TelegramLoginButton'

export interface OAuthProviders {
  vk: boolean
  telegramBotUsername: string | null
}

export interface ExternalLogin {
  provider: string
  externalId: string
  linkedAt: string
}

export interface ExternalLoginsResponse {
  logins: ExternalLogin[]
  hasPassword: boolean
}

const PROVIDERS_KEY = ['oauth', 'providers']
const EXTERNAL_LOGINS_KEY = ['oauth', 'external-logins']

export function useOAuthProviders() {
  return useQuery({
    queryKey: PROVIDERS_KEY,
    queryFn: async () => {
      const res = await api.get<OAuthProviders>('/auth/providers')
      return res.data
    },
    staleTime: 5 * 60 * 1000,
  })
}

export function useExternalLogins() {
  return useQuery({
    queryKey: EXTERNAL_LOGINS_KEY,
    queryFn: async () => {
      const res = await api.get<ExternalLoginsResponse>('/auth/external-logins')
      return res.data
    },
  })
}

/**
 * Запускает link flow VK: бэк ставит куки и отдаёт URL авторизации.
 * Фронт редиректит окно на этот URL.
 */
export function useStartVkLink() {
  return useMutation({
    mutationFn: async () => {
      const res = await api.post<{ url: string }>('/auth/link/vk/start')
      return res.data
    },
    onSuccess: (data) => {
      window.location.href = data.url
    },
  })
}

export function useLinkTelegram() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: TelegramAuthData) => {
      await api.post('/auth/link/telegram', mapTelegramPayload(payload))
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: EXTERNAL_LOGINS_KEY })
    },
  })
}

export function useTelegramLogin() {
  const setAuth = useAuthStore((s) => s.setAuth)
  return useMutation({
    mutationFn: async (payload: TelegramAuthData) => {
      const res = await api.post<AuthResponseRaw>('/auth/telegram', mapTelegramPayload(payload))
      return mapAuthResponseFromApi(res.data)
    },
    onSuccess: (data) => {
      setAuth(data)
    },
  })
}

export function useUnlinkProvider() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (provider: string) => {
      await api.delete(`/auth/unlink/${provider}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: EXTERNAL_LOGINS_KEY })
    },
  })
}

function mapTelegramPayload(data: TelegramAuthData) {
  return {
    id: data.id,
    firstName: data.first_name,
    lastName: data.last_name,
    username: data.username,
    photoUrl: data.photo_url,
    authDate: String(data.auth_date),
    hash: data.hash,
  }
}
