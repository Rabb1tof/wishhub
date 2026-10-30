import axios, { AxiosError, CanceledError } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { useAuthStore, mapAuthResponseFromApi } from '@/store/auth'
import type { AuthResponse, AuthResponseRaw } from '@/types/auth'

interface SessionRequest extends InternalAxiosRequestConfig {
  sessionVersion?: number
  retried?: boolean
}

const sessionEndpoints = new Set(['/auth/login', '/auth/register', '/auth/telegram', '/auth/refresh', '/auth/complete', '/auth/logout'])

const api = axios.create({ baseURL: '/api', withCredentials: false })
let sessionController = new AbortController()
let refreshing: { version: number; promise: Promise<void> } | null = null

useAuthStore.subscribe((current, previous) => {
  if (current.sessionVersion === previous.sessionVersion && current.user?.id === previous.user?.id) return
  const previousController = sessionController
  sessionController = new AbortController()
  refreshing = null
  previousController.abort()
})

function requireCurrentSession(config: SessionRequest) {
  if (config.sessionVersion !== useAuthStore.getState().sessionVersion) throw new CanceledError('Session changed')
}

api.interceptors.request.use((config: SessionRequest) => {
  const auth = useAuthStore.getState()
  config.sessionVersion ??= auth.sessionVersion
  requireCurrentSession(config)
  config.signal = config.signal
    ? AbortSignal.any([config.signal as AbortSignal, sessionController.signal])
    : sessionController.signal
  if (auth.accessToken) config.headers.set('Authorization', `Bearer ${auth.accessToken}`)
  else config.headers.delete('Authorization')
  return config
}, (error: unknown) => { throw error }, { synchronous: true })

async function refreshSession(version: number) {
  const auth = useAuthStore.getState()
  try {
    // This request must never enter this instance's 401 interceptor.
    const response = await axios.post<AuthResponseRaw | AuthResponse>('/api/auth/refresh', {
      refreshToken: auth.refreshToken,
    }, { signal: sessionController.signal, timeout: 10000 })
    if (!useAuthStore.getState().refreshAuth(mapAuthResponseFromApi(response.data), version)) {
      throw new CanceledError('Session changed')
    }
  } catch (error) {
    if (useAuthStore.getState().sessionVersion !== version) throw new CanceledError('Session changed')
    useAuthStore.getState().clearAuth()
    throw error
  } finally {
    if (refreshing?.version === version) refreshing = null
  }
}

api.interceptors.response.use(
  response => {
    requireCurrentSession(response.config as SessionRequest)
    return response
  },
  async (error: AxiosError) => {
    const request = error.config as SessionRequest | undefined
    if (!request) return Promise.reject(error)
    requireCurrentSession(request)
    const auth = useAuthStore.getState()
    if (error.response?.status !== 401 || request.retried || sessionEndpoints.has(request.url?.split('?')[0] ?? '') || !auth.refreshToken) {
      return Promise.reject(error)
    }
    request.retried = true
    // Another request may already have refreshed this same session.
    if (request.headers.get('Authorization') !== `Bearer ${auth.accessToken}`) return api(request)
    const version = auth.sessionVersion
    refreshing ??= { version, promise: refreshSession(version) }
    await refreshing.promise
    requireCurrentSession(request)
    return api(request)
  },
)

export default api
