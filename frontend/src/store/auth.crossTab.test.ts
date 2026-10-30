import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '@/types/auth'

const AUTH_KEY = 'wishhub-auth'
const tokenKey = (id: string) => `${AUTH_KEY}:tokens:${id}`
const auth = (id = 'A'): AuthResponse => ({
  user: { id, username: id, displayName: id }, accessToken: `access-${id}`, refreshToken: `refresh-${id}`, expiresAt: '2099-01-01',
})
const envelope = (id: string, sessionId: string) => ({ state: { ...auth(id), sessionId }, version: 0 })
const tokens = (id: string, sessionId: string, suffix = 'rotated') => ({
  sessionId, userId: id, accessToken: `access-${id}-${suffix}`, refreshToken: `refresh-${id}-${suffix}`,
})
let useAuthStore: typeof import('./auth').useAuthStore
let syncAuthFromStorage: typeof import('./auth').syncAuthFromStorage
let removeStorageListeners: () => void

function storageEvent(key: string | null, newValue: string | null = null, storageArea = localStorage) {
  window.dispatchEvent(new StorageEvent('storage', { key, newValue, storageArea }))
}

beforeEach(async () => {
  vi.resetModules()
  localStorage.clear()
  const listenerSpy = vi.spyOn(window, 'addEventListener')
  removeStorageListeners = () => {
    for (const [type, listener] of listenerSpy.mock.calls) {
      if (type === 'storage') window.removeEventListener(type, listener as EventListener)
    }
  }
  ;({ useAuthStore, syncAuthFromStorage } = await import('./auth'))
})
afterEach(() => {
  removeStorageListeners()
  vi.restoreAllMocks()
})

describe('cross-tab auth storage', () => {
  it('persists a fresh session marker on every explicit login including the same account', () => {
    useAuthStore.getState().setAuth(auth())
    const first = useAuthStore.getState()
    useAuthStore.getState().setAuth(auth())
    const second = useAuthStore.getState()
    expect(second.sessionId).toMatch(/^[a-f0-9]{32}$/)
    expect(second.sessionId).not.toBe(first.sessionId)
    expect(second.sessionVersion).toBe(first.sessionVersion + 1)
    expect(JSON.parse(localStorage.getItem(AUTH_KEY)!).state).toMatchObject({ sessionId: second.sessionId, user: { id: 'A' } })
  })

  it('hydrates only the token record belonging to the current persisted login', async () => {
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('B', 'session-B')))
    localStorage.setItem(tokenKey('session-A'), JSON.stringify(tokens('A', 'session-A')))
    localStorage.setItem(tokenKey('session-B'), JSON.stringify(tokens('B', 'session-B')))
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    vi.resetModules()
    const hydrated = (await import('./auth')).useAuthStore.getState()
    expect(hydrated).toMatchObject({ user: { id: 'B' }, sessionId: 'session-B', accessToken: 'access-B-rotated', refreshToken: 'refresh-B-rotated', sessionVersion: 0 })
    expect(writes).not.toHaveBeenCalled()
  })

  it('imports a switched account as one local boundary without writing storage', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState().sessionVersion
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('B', 'session-B')))
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    storageEvent(AUTH_KEY)
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'B' }, sessionId: 'session-B', accessToken: 'access-B', sessionVersion: before + 1 })
    storageEvent(AUTH_KEY)
    expect(useAuthStore.getState().sessionVersion).toBe(before + 1)
    expect(writes).not.toHaveBeenCalled()
  })

  it('treats a new marker for the same account as a new login', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState().sessionVersion
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('A', 'another-login-A')))
    storageEvent(AUTH_KEY)
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'A' }, sessionId: 'another-login-A', sessionVersion: before + 1 })
  })

  it('imports a peer token rotation without changing the session or local avatar', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    useAuthStore.setState({ user: { ...auth().user, avatarUrl: '/avatars/local.png' } })
    localStorage.setItem(tokenKey(sessionId!), JSON.stringify(tokens('A', sessionId!)))
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    storageEvent(tokenKey(sessionId!))
    expect(useAuthStore.getState()).toMatchObject({ sessionId, sessionVersion, user: { id: 'A', avatarUrl: '/avatars/local.png' }, accessToken: 'access-A-rotated', refreshToken: 'refresh-A-rotated' })
    expect(writes).not.toHaveBeenCalled()
  })

  it('reads current storage instead of replaying the older event payload', () => {
    useAuthStore.getState().setAuth(auth())
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('C', 'latest-C')))
    storageEvent(AUTH_KEY, JSON.stringify(envelope('B', 'old-B')))
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'C' }, sessionId: 'latest-C', accessToken: 'access-C' })
    expect(JSON.parse(localStorage.getItem(AUTH_KEY)!).state.sessionId).toBe('latest-C')
  })

  it.each([{ name: 'logout record', mode: 'record' }, { name: 'removed key', mode: 'remove' }, { name: 'storage clear', mode: 'clear' }])('imports $name and prevents old refresh from restoring it', ({ mode }) => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    if (mode === 'record') localStorage.setItem(AUTH_KEY, JSON.stringify({ state: { user: null, accessToken: null, refreshToken: null, sessionId: null }, version: 0 }))
    else if (mode === 'remove') localStorage.removeItem(AUTH_KEY)
    else localStorage.clear()
    storageEvent(mode === 'clear' ? null : AUTH_KEY)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionId: null, sessionVersion: sessionVersion + 1 })
    expect(useAuthStore.getState().refreshAuth({ ...auth(), accessToken: 'late-A' }, sessionVersion)).toBe(false)
    expect(localStorage.getItem(tokenKey(sessionId!))).toBeNull()
  })

  it('keeps rotations out of the authoritative login record and preserves the marker', () => {
    useAuthStore.getState().setAuth(auth())
    const original = localStorage.getItem(AUTH_KEY)
    const { sessionId, sessionVersion } = useAuthStore.getState()
    expect(useAuthStore.getState().refreshAuth({ ...auth(), accessToken: 'new-A', refreshToken: 'new-refresh-A' }, sessionVersion)).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ sessionId, sessionVersion, accessToken: 'new-A', refreshToken: 'new-refresh-A' })
    expect(localStorage.getItem(AUTH_KEY)).toBe(original)
    expect(JSON.parse(localStorage.getItem(tokenKey(sessionId!))!)).toEqual({ sessionId, userId: 'A', accessToken: 'new-A', refreshToken: 'new-refresh-A' })
  })

  it('does not let a late token write overwrite a newer login during persistence', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    const setItem = Storage.prototype.setItem
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key, value) {
      setItem.call(this, key, value)
      if (key === tokenKey(sessionId!)) {
        setItem.call(this, AUTH_KEY, JSON.stringify(envelope('B', 'session-B')))
        setItem.call(this, tokenKey('session-B'), JSON.stringify(tokens('B', 'session-B')))
      }
    })
    expect(useAuthStore.getState().refreshAuth({ ...auth(), accessToken: 'late-A' }, sessionVersion)).toBe(false)
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'B' }, accessToken: 'access-B-rotated', sessionId: 'session-B' })
    expect(localStorage.getItem(tokenKey(sessionId!))).toBeNull()
    expect(JSON.parse(localStorage.getItem(tokenKey('session-B'))!).refreshToken).toBe('refresh-B-rotated')
    expect(JSON.parse(localStorage.getItem(AUTH_KEY)!).state.sessionId).toBe('session-B')
  })

  it('does not write raw avatar updates or restore tokens from an inactive session', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId } = useAuthStore.getState()
    const original = localStorage.getItem(AUTH_KEY)
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    useAuthStore.setState({ user: { ...auth().user, avatarUrl: '/avatars/local.png' } })
    expect(writes).not.toHaveBeenCalled()
    expect(localStorage.getItem(AUTH_KEY)).toBe(original)
    useAuthStore.getState().clearAuth()
    localStorage.setItem(tokenKey(sessionId!), JSON.stringify(tokens('A', sessionId!)))
    storageEvent(tokenKey(sessionId!))
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, sessionId: null })
  })

  it('cleans only the previous session token record on explicit logout', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    useAuthStore.getState().refreshAuth({ ...auth(), refreshToken: 'new-refresh-A' }, sessionVersion)
    localStorage.setItem(tokenKey('unrelated-B'), JSON.stringify(tokens('B', 'unrelated-B')))
    useAuthStore.getState().clearAuth()
    expect(localStorage.getItem(tokenKey(sessionId!))).toBeNull()
    expect(localStorage.getItem(tokenKey('unrelated-B'))).not.toBeNull()
    expect(JSON.parse(localStorage.getItem(AUTH_KEY)!).state).toEqual({ user: null, accessToken: null, refreshToken: null, sessionId: null })
  })

  it('ignores unrelated keys and sessionStorage events', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState()
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('B', 'session-B')))
    storageEvent('unrelated-key')
    storageEvent(AUTH_KEY, null, sessionStorage)
    expect(useAuthStore.getState()).toBe(before)
  })

  it.each([
    { name: 'legacy without marker', value: JSON.stringify({ state: auth(), version: 0 }) },
    { name: 'malformed json', value: '{invalid' },
    { name: 'invalid user shape', value: JSON.stringify({ state: { ...auth(), user: { id: 'A' }, sessionId: 'session-A' } }) },
    { name: 'missing refresh token', value: JSON.stringify({ state: { ...auth(), refreshToken: null, sessionId: 'session-A' } }) },
  ])('requires a new login for $name without echoing storage', ({ value }) => {
    useAuthStore.getState().setAuth(auth())
    localStorage.setItem(AUTH_KEY, value)
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionId: null })
    expect(writes).not.toHaveBeenCalled()
    expect(localStorage.getItem(AUTH_KEY)).toBe(value)
  })

  it('reports unavailable storage without changing the current memory session', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Unavailable', 'SecurityError') })
    expect(syncAuthFromStorage()).toBe(false)
    expect(useAuthStore.getState()).toBe(before)
    expect(useAuthStore.getState().refreshAuth({ ...auth(), accessToken: 'new-A' }, before.sessionVersion)).toBe(false)
  })

  it('does not claim a refresh was persisted when its token write fails', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Full', 'QuotaExceededError') })
    expect(useAuthStore.getState().refreshAuth({ ...auth(), accessToken: 'new-A' }, before.sessionVersion)).toBe(false)
    expect(useAuthStore.getState()).toBe(before)
  })

  it('expires only its session token record and preserves the authoritative login pointer', () => {
    useAuthStore.getState().setAuth(auth())
    const base = localStorage.getItem(AUTH_KEY)
    const { sessionId, sessionVersion } = useAuthStore.getState()
    expect(useAuthStore.getState().expireAuth(sessionVersion)).toBe(true)
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
    expect(JSON.parse(localStorage.getItem(tokenKey(sessionId!))!)).toEqual({ sessionId, userId: 'A', expired: true })
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionVersion: sessionVersion + 1 })
    useAuthStore.getState().setAuth(auth('B'))
    expect(localStorage.getItem(tokenKey(sessionId!))).toBeNull()
    expect(useAuthStore.getState().user?.id).toBe('B')
  })

  it('imports a matching peer expiry event as a logout boundary', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    localStorage.setItem(tokenKey(sessionId!), JSON.stringify({ sessionId, userId: 'A', expired: true }))
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    storageEvent(tokenKey(sessionId!))
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionId: null, sessionVersion: sessionVersion + 1 })
    expect(writes).not.toHaveBeenCalled()
  })

  it('cannot erase a newer login when expiry persistence races the account change', () => {
    useAuthStore.getState().setAuth(auth())
    const { sessionId, sessionVersion } = useAuthStore.getState()
    const setItem = Storage.prototype.setItem
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key, value) {
      setItem.call(this, key, value)
      if (key === tokenKey(sessionId!)) setItem.call(this, AUTH_KEY, JSON.stringify(envelope('B', 'session-B')))
    })
    expect(useAuthStore.getState().expireAuth(sessionVersion)).toBe(false)
    expect(JSON.parse(localStorage.getItem(AUTH_KEY)!).state.sessionId).toBe('session-B')
    expect(localStorage.getItem(tokenKey(sessionId!))).toBeNull()
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'B' }, accessToken: 'access-B' })
  })

  it('ends the local session on failed expiry persistence without restoring it from the old record', () => {
    useAuthStore.getState().setAuth(auth())
    const base = localStorage.getItem(AUTH_KEY)
    const before = useAuthStore.getState().sessionVersion
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Full', 'QuotaExceededError') })
    expect(useAuthStore.getState().expireAuth(before)).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionVersion: before + 1 })
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState().user).toBeNull()
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
    write.mockRestore()
    useAuthStore.getState().setAuth(auth())
    expect(useAuthStore.getState().user?.id).toBe('A')
  })

  it('expires only local memory when storage becomes unreadable', () => {
    useAuthStore.getState().setAuth(auth())
    const version = useAuthStore.getState().sessionVersion
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Unavailable', 'SecurityError') })
    expect(useAuthStore.getState().expireAuth(version)).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionVersion: version + 1 })
    read.mockRestore()
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState().user).toBeNull()
  })

  it.each([
    { name: 'different session', value: tokens('A', 'other-session') },
    { name: 'different owner', value: tokens('B', 'session-A') },
    { name: 'incomplete tokens', value: { sessionId: 'session-A', userId: 'A', accessToken: 'partial-A' } },
  ])('ignores a token record with $name', ({ value }) => {
    localStorage.setItem(AUTH_KEY, JSON.stringify(envelope('A', 'session-A')))
    localStorage.setItem(tokenKey('session-A'), JSON.stringify(value))
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState()).toMatchObject({ user: { id: 'A' }, sessionId: 'session-A', accessToken: 'access-A', refreshToken: 'refresh-A' })
  })

  it('rejects login before changing memory when the authoritative storage write fails', () => {
    useAuthStore.getState().setAuth(auth())
    const before = useAuthStore.getState()
    const base = localStorage.getItem(AUTH_KEY)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Full', 'QuotaExceededError') })
    expect(() => useAuthStore.getState().setAuth(auth('B'))).toThrow('Не удалось сохранить сессию: хранилище браузера недоступно или заполнено.')
    expect(useAuthStore.getState()).toBe(before)
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
    expect(syncAuthFromStorage()).toBe(true)
    expect(useAuthStore.getState()).toBe(before)
  })

  it('keeps a failed persistent logout local for current login', () => {
    useAuthStore.getState().setAuth(auth())
    const version = useAuthStore.getState().sessionVersion
    const base = localStorage.getItem(AUTH_KEY)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Full', 'QuotaExceededError') })
    useAuthStore.getState().clearAuth()
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionId: null, sessionVersion: version + 1 })
    expect(syncAuthFromStorage()).toBe(true)
    storageEvent(AUTH_KEY)
    expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null, sessionId: null, sessionVersion: version + 1 })
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
  })

  it.each([{ name: 'another account', id: 'B' }, { name: 'the same account with a new marker', id: 'A' }])('refuses an old logout after an undelivered login for $name', ({ id }) => {
    useAuthStore.getState().setAuth(auth())
    const version = useAuthStore.getState().sessionVersion
    const base = JSON.stringify(envelope(id, 'peer-login'))
    const peerTokens = JSON.stringify(tokens(id, 'peer-login'))
    localStorage.setItem(AUTH_KEY, base)
    localStorage.setItem(tokenKey('peer-login'), peerTokens)
    const writes = vi.spyOn(Storage.prototype, 'setItem')
    expect(useAuthStore.getState().clearAuth()).toBe(false)
    expect(useAuthStore.getState()).toMatchObject({ user: { id }, sessionId: 'peer-login', sessionVersion: version + 1 })
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
    expect(localStorage.getItem(tokenKey('peer-login'))).toBe(peerTokens)
    expect(writes).not.toHaveBeenCalled()
  })

  it('rejects an explicitly stale logout version without changing the current session', () => {
    useAuthStore.getState().setAuth(auth())
    const oldVersion = useAuthStore.getState().sessionVersion
    useAuthStore.getState().setAuth(auth('B'))
    const current = useAuthStore.getState()
    const base = localStorage.getItem(AUTH_KEY)
    expect(useAuthStore.getState().clearAuth(oldVersion)).toBe(false)
    expect(useAuthStore.getState()).toBe(current)
    expect(localStorage.getItem(AUTH_KEY)).toBe(base)
  })
})
