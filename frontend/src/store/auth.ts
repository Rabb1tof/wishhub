import { create } from 'zustand'
import type { AuthResponse, AuthResponseRaw, User } from '@/types/auth'

interface StoredAuth {
  user: User | null
  accessToken: string | null
  refreshToken: string | null
  sessionId: string | null
}

interface AuthState extends StoredAuth {
  sessionVersion: number
  setAuth: (payload: AuthResponse) => void
  clearAuth: (expectedVersion?: number) => boolean
  refreshAuth: (payload: AuthResponse, expectedVersion: number) => boolean
  expireAuth: (expectedVersion: number) => boolean
}

const AUTH_KEY = 'wishhub-auth'
let locallyExpiredSessionId: string | null = null
const tokenKey = (sessionId: string) => `${AUTH_KEY}:tokens:${sessionId}`
const anonymous: StoredAuth = { user: null, accessToken: null, refreshToken: null, sessionId: null }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const nonblank = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0

function parseAuth(raw: string | null): StoredAuth {
  if (!raw) return anonymous
  try {
    const envelope: unknown = JSON.parse(raw)
    const state = record(envelope) && record(envelope.state) ? envelope.state : null
    if (!state || !record(state.user) || !nonblank(state.sessionId)
      || !nonblank(state.accessToken) || !nonblank(state.refreshToken)
      || !nonblank(state.user.id) || typeof state.user.username !== 'string' || typeof state.user.displayName !== 'string'
      || (state.user.avatarUrl != null && typeof state.user.avatarUrl !== 'string')) return anonymous
    return {
      sessionId: state.sessionId, accessToken: state.accessToken, refreshToken: state.refreshToken,
      user: { id: state.user.id, username: state.user.username, displayName: state.user.displayName,
        avatarUrl: typeof state.user.avatarUrl === 'string' ? state.user.avatarUrl : undefined },
    }
  } catch {
    return anonymous
  }
}

// The base key alone decides which login is current. Refresh never writes it.
function readBase(): StoredAuth | undefined {
  try { return parseAuth(localStorage.getItem(AUTH_KEY)) } catch { return undefined }
}

function readStoredAuth(): StoredAuth | undefined {
  try {
    const base = readBase()
    if (!base) return undefined
    if (!base.sessionId || !base.user) return base
    if (base.sessionId === locallyExpiredSessionId) return anonymous
    const raw = localStorage.getItem(tokenKey(base.sessionId))
    if (!raw) return base
    try {
      const tokens: unknown = JSON.parse(raw)
      if (record(tokens) && tokens.sessionId === base.sessionId && tokens.userId === base.user.id) {
        if (tokens.expired === true) return anonymous
        if (nonblank(tokens.accessToken) && nonblank(tokens.refreshToken)) {
          return { ...base, accessToken: tokens.accessToken, refreshToken: tokens.refreshToken }
        }
      }
    } catch { /* An invalid token record cannot replace the current login. */ }
    return base
  } catch {
    return undefined
  }
}

function removeTokens(sessionId: string | null): void {
  if (!sessionId) return
  try { localStorage.removeItem(tokenKey(sessionId)) } catch { /* Storage may be unavailable. */ }
}

function writeLogin(auth: StoredAuth): boolean {
  try {
    localStorage.setItem(AUTH_KEY, JSON.stringify({ state: auth, version: 0 }))
    return true
  } catch {
    return false
  }
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

export const useAuthStore = create<AuthState>()((set, get) => ({
  ...(readStoredAuth() ?? anonymous),
  sessionVersion: 0,
  setAuth: ({ user, accessToken, refreshToken }) => {
    const previous = get()
    const sessionId = Array.from(crypto.getRandomValues(new Uint8Array(16)), value => value.toString(16).padStart(2, '0')).join('')
    const next = { user, accessToken, refreshToken, sessionId }
    const previousId = readBase()?.sessionId ?? previous.sessionId
    if (!writeLogin(next)) throw new Error('Не удалось сохранить сессию: хранилище браузера недоступно или заполнено.')
    set({ ...next, sessionVersion: previous.sessionVersion + 1 })
    removeTokens(previousId)
  },
  clearAuth: (expectedVersion) => {
    const origin = get()
    syncAuthFromStorage()
    const current = get()
    if (current.sessionVersion !== origin.sessionVersion || current.sessionId !== origin.sessionId
      || (expectedVersion !== undefined && origin.sessionVersion !== expectedVersion)) return false
    const saved = writeLogin(anonymous)
    if (!saved) locallyExpiredSessionId = origin.sessionId
    set({ ...anonymous, sessionVersion: origin.sessionVersion + 1 })
    if (saved) removeTokens(origin.sessionId)
    return true
  },
  refreshAuth: ({ user, accessToken, refreshToken }, expectedVersion) => {
    if (!syncAuthFromStorage()) return false
    const current = get()
    if (current.sessionVersion !== expectedVersion || current.user?.id !== user.id || !current.sessionId) return false
    try {
      localStorage.setItem(tokenKey(current.sessionId), JSON.stringify({ sessionId: current.sessionId, userId: user.id, accessToken, refreshToken }))
    } catch {
      return false
    }
    const readable = syncAuthFromStorage()
    const latest = get()
    if (!readable || latest.sessionVersion !== expectedVersion || latest.sessionId !== current.sessionId) {
      removeTokens(current.sessionId)
      return false
    }
    return true
  },
  expireAuth: (expectedVersion) => {
    const clearLocal = () => {
      const current = get()
      if (current.sessionVersion !== expectedVersion || !current.sessionId) return false
      // A readable but unwritable old record must not restore this local session.
      locallyExpiredSessionId = current.sessionId
      set({ ...anonymous, sessionVersion: current.sessionVersion + 1 })
      return true
    }
    if (!syncAuthFromStorage()) return clearLocal()
    const current = get()
    if (current.sessionVersion !== expectedVersion || !current.user || !current.sessionId) return false
    try {
      localStorage.setItem(tokenKey(current.sessionId), JSON.stringify({ sessionId: current.sessionId, userId: current.user.id, expired: true }))
    } catch {
      return clearLocal()
    }
    if (!syncAuthFromStorage()) return clearLocal()
    if (readBase()?.sessionId !== current.sessionId) removeTokens(current.sessionId)
    return get().user === null
  },
}))

export function syncAuthFromStorage(): boolean {
  const stored = readStoredAuth()
  if (!stored) return false
  const current = useAuthStore.getState()
  const changedSession = stored.sessionId !== current.sessionId || stored.user?.id !== current.user?.id
  if (changedSession) {
    useAuthStore.setState({ ...stored, sessionVersion: current.sessionVersion + 1 })
  } else if (stored.accessToken !== current.accessToken || stored.refreshToken !== current.refreshToken) {
    // Keep local profile edits and the existing cache during a token rotation.
    useAuthStore.setState({ accessToken: stored.accessToken, refreshToken: stored.refreshToken })
  }
  return true
}

if (typeof window !== 'undefined') {
  const onStorage = (event: StorageEvent) => {
    try { if (event.storageArea !== window.localStorage) return } catch { return }
    const sessionId = useAuthStore.getState().sessionId
    if (event.key === null || event.key === AUTH_KEY || (sessionId && event.key === tokenKey(sessionId))) syncAuthFromStorage()
  }
  window.addEventListener('storage', onStorage)
  if (import.meta.hot) import.meta.hot.dispose(() => window.removeEventListener('storage', onStorage))
}
