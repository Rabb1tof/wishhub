import axios, { AxiosError, CanceledError } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { useAuthStore, mapAuthResponseFromApi, syncAuthFromStorage } from '@/store/auth'
import type { AuthResponse, AuthResponseRaw } from '@/types/auth'

interface SessionRequest extends InternalAxiosRequestConfig {
  sessionVersion?: number
  retried?: boolean
}

const sessionEndpoints = new Set(['/auth/login', '/auth/register', '/auth/telegram', '/auth/refresh', '/auth/complete'])

const api = axios.create({ baseURL: '/api', withCredentials: false })
let sessionController = new AbortController()
let refreshing: { version: number; promise: Promise<void> } | null = null

const unsubscribeAuth = useAuthStore.subscribe((current, previous) => {
  if (current.sessionVersion === previous.sessionVersion && current.user?.id === previous.user?.id) return
  const previousController = sessionController
  sessionController = new AbortController()
  refreshing = null
  previousController.abort()
})

if (import.meta.hot) import.meta.hot.dispose(unsubscribeAuth)

function requireCurrentSession(config: SessionRequest) {
  if (config.sessionVersion !== useAuthStore.getState().sessionVersion) throw new CanceledError('Session changed')
}

api.interceptors.request.use((config: SessionRequest) => {
  config.sessionVersion ??= useAuthStore.getState().sessionVersion
  syncAuthFromStorage()
  requireCurrentSession(config)
  const auth = useAuthStore.getState()
  config.signal = config.signal
    ? AbortSignal.any([config.signal as AbortSignal, sessionController.signal])
    : sessionController.signal
  if (auth.accessToken) config.headers.set('Authorization', `Bearer ${auth.accessToken}`)
  else config.headers.delete('Authorization')
  return config
}, (error: unknown) => { throw error }, { synchronous: true })

async function refreshSession(version: number, accessToken: string | null, refreshToken: string) {
  const signal = sessionController.signal
  try {
    // Without an origin-wide lock there is no safe fallback for rotating tokens.
    if (!navigator.locks?.request) throw new Error('Повторите вход, чтобы продолжить работу.')
    await navigator.locks.request('wishhub-auth-refresh', { signal }, async () => {
      if (!syncAuthFromStorage()) throw new Error('Хранилище сессии недоступно. Повторите вход.')
      const current = useAuthStore.getState()
      if (current.sessionVersion !== version || signal.aborted) throw new CanceledError('Session changed')
      // A tab ahead of us may have already rotated this session's token pair.
      if (current.accessToken !== accessToken || current.refreshToken !== refreshToken) return
      const response = await axios.post<AuthResponseRaw | AuthResponse>('/api/auth/refresh', {
        refreshToken: current.refreshToken,
      }, { signal, timeout: 10000 })
      syncAuthFromStorage()
      if (!useAuthStore.getState().refreshAuth(mapAuthResponseFromApi(response.data), version)) {
        throw new CanceledError('Session changed')
      }
    })
  } catch (error) {
    syncAuthFromStorage()
    const current = useAuthStore.getState()
    if (current.sessionVersion !== version || signal.aborted) throw new CanceledError('Session changed')
    if (current.accessToken !== accessToken || current.refreshToken !== refreshToken) return
    // Expire only the failed login, never overwrite another tab's login pointer.
    current.expireAuth(version)
    throw error
  } finally {
    if (refreshing?.version === version) refreshing = null
  }
}

api.interceptors.response.use(
  response => {
    syncAuthFromStorage()
    requireCurrentSession(response.config as SessionRequest)
    return response
  },
  async (error: AxiosError) => {
    const request = error.config as SessionRequest | undefined
    if (!request) return Promise.reject(error)
    syncAuthFromStorage()
    requireCurrentSession(request)
    const auth = useAuthStore.getState()
    if (error.response?.status !== 401 || request.retried || sessionEndpoints.has(request.url?.split('?')[0] ?? '') || !auth.refreshToken) {
      return Promise.reject(error)
    }
    request.retried = true
    // Another request may already have refreshed this same session.
    if (request.headers.get('Authorization') !== `Bearer ${auth.accessToken}`) return api(request)
    const version = auth.sessionVersion
    refreshing ??= { version, promise: refreshSession(version, auth.accessToken, auth.refreshToken) }
    await refreshing.promise
    requireCurrentSession(request)
    return api(request)
  },
)

export default api
