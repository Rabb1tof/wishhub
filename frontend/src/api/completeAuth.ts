import api from './client'
import { mapAuthResponseFromApi } from '@/store/auth'
import type { AuthResponse, AuthResponseRaw } from '@/types/auth'

export async function completeAuth(): Promise<AuthResponse> {
  const response = await api.post<AuthResponse | AuthResponseRaw>('/auth/complete', undefined, {
    headers: { 'X-WishHub-OAuth': '1' },
  })
  return mapAuthResponseFromApi(response.data)
}
