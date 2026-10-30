import { AxiosError, isCancel } from 'axios'
import type { AxiosAdapter, AxiosStatic, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '@/types/auth'

type AuthStore = typeof import('@/store/auth').useAuthStore
type Api = typeof import('./client').default
interface Tab { name: string; store: AuthStore; api: Api; axios: AxiosStatic }
interface SavedAuth {
  state: { user: AuthResponse['user'] | null; accessToken: string | null; refreshToken: string | null }
}

const session = (id = 'A', accessToken = `access-${id}`, refreshToken = `refresh-${id}`): AuthResponse => ({
  user: { id, username: id, displayName: id }, accessToken, refreshToken, expiresAt: '2099-01-01T00:00:00Z',
})
const rotated = session('A', 'rotated-access-A', 'rotated-refresh-A')
const response = (config: InternalAxiosRequestConfig, data: unknown, status = 200) => ({
  config, data, status, statusText: String(status), headers: {},
})
const denied = (config: InternalAxiosRequestConfig) => new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 401))
const saved = () => {
  const value = localStorage.getItem('wishhub-auth')
  return value ? (JSON.parse(value) as SavedAuth).state : { user: null, accessToken: null, refreshToken: null }
}

let releases: (() => void)[]
let requests: Promise<unknown>[]
let storageListeners: { listener: EventListenerOrEventListenerObject; options?: boolean | AddEventListenerOptions }[]
let rawAdapters: Map<AxiosStatic, AxiosStatic['defaults']['adapter']>
let locksDescriptor: PropertyDescriptor | undefined
let locks: ReturnType<typeof serializedLocks>
let refreshAdapter: ReturnType<typeof vi.fn<AxiosAdapter>>
let protectedCalls: { tab: string; token: unknown }[]

function gate() {
  let release!: () => void
  const promise = new Promise<void>(resolve => { release = resolve })
  releases.push(release)
  return { promise, release }
}

function serializedLocks() {
  const tails = new Map<string, Promise<void>>()
  type Job = (lock: Lock) => unknown
  const request = vi.fn((name: string, optionsOrJob: LockOptions | Job, suppliedJob?: Job) => {
    const job = typeof optionsOrJob === 'function' ? optionsOrJob : suppliedJob
    const options = typeof optionsOrJob === 'function' ? undefined : optionsOrJob
    if (!job) throw new Error('Missing lock callback')
    const previous = tails.get(name) ?? Promise.resolve()
    const done = gate()
    tails.set(name, previous.then(() => done.promise))
    return previous.then(() => {
      if (options?.signal?.aborted) throw new DOMException('Aborted lock request', 'AbortError')
      return job({ name, mode: 'exclusive' } as Lock)
    }).finally(done.release)
  })
  return { request }
}

async function loadTab(name: string): Promise<Tab> {
  vi.resetModules()
  const axios = (await import('axios')).default
  const store = (await import('@/store/auth')).useAuthStore
  const api = (await import('./client')).default
  if (!rawAdapters.has(axios)) rawAdapters.set(axios, axios.defaults.adapter)
  return { name, store, api, axios }
}

async function twoTabs() {
  const first = await loadTab('first')
  first.store.getState().setAuth(session())
  const second = await loadTab('second')
  expect(second.store).not.toBe(first.store)
  expect(second.api).not.toBe(first.api)
  expect(second.store.getState()).toMatchObject({ user: session().user, accessToken: 'access-A', refreshToken: 'refresh-A' })
  for (const tab of [first, second]) {
    tab.axios.defaults.adapter = refreshAdapter
    tab.api.defaults.adapter = async config => {
      protectedCalls.push({ tab: tab.name, token: config.headers.get('Authorization') })
      if (protectedCalls.filter(call => call.tab === tab.name).length > 2) throw new Error('Protected retry cap exceeded')
      if (config.headers.get('Authorization') !== 'Bearer rotated-access-A') throw denied(config)
      return response(config, `${tab.name}-private-data`)
    }
  }
  return [first, second] as const
}

function startRequest(tab: Tab) {
  const pending = Promise.allSettled([tab.api.get('/wishlist')]).then(([result]) => result)
  requests.push(pending)
  return pending
}

beforeEach(() => {
  localStorage.clear()
  releases = []
  requests = []
  storageListeners = []
  rawAdapters = new Map()
  protectedCalls = []
  locksDescriptor = Object.getOwnPropertyDescriptor(navigator, 'locks')
  locks = serializedLocks()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: locks })
  const addEventListener = window.addEventListener.bind(window)
  vi.spyOn(window, 'addEventListener').mockImplementation((type, listener, options) => {
    if (type === 'storage') storageListeners.push({ listener, options })
    addEventListener(type, listener, options)
  })
  refreshAdapter = vi.fn<AxiosAdapter>(async config => { throw new Error(`Unexpected refresh: ${config.url}`) })
})

afterEach(async () => {
  for (const release of releases) release()
  await Promise.allSettled(requests)
  for (const { listener, options } of storageListeners) window.removeEventListener('storage', listener, options)
  for (const [axios, adapter] of rawAdapters) axios.defaults.adapter = adapter
  if (locksDescriptor) Object.defineProperty(navigator, 'locks', locksDescriptor)
  else Reflect.deleteProperty(navigator, 'locks')
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('cross-tab session HTTP boundaries', () => {
  it('shares one refresh across tabs and retries both with the rotated token', async () => {
    const [first, second] = await twoTabs()
    const delayed = gate()
    refreshAdapter.mockImplementation(async config => {
      if (refreshAdapter.mock.calls.length > 1) throw denied(config)
      await delayed.promise
      return response(config, rotated)
    })
    const firstResult = startRequest(first)
    const secondResult = startRequest(second)
    try {
      await vi.waitFor(() => expect(protectedCalls).toHaveLength(2))
      await vi.waitFor(() => expect(refreshAdapter).toHaveBeenCalledTimes(1))
      expect(JSON.parse(String(refreshAdapter.mock.calls[0][0].data))).toEqual({ refreshToken: 'refresh-A' })
      delayed.release()
      const results = await Promise.all([firstResult, secondResult])
      expect(results).toMatchObject([
        { status: 'fulfilled', value: { data: 'first-private-data' } },
        { status: 'fulfilled', value: { data: 'second-private-data' } },
      ])
      expect(refreshAdapter).toHaveBeenCalledTimes(1)
      expect(protectedCalls).toEqual(expect.arrayContaining([
        { tab: 'first', token: 'Bearer rotated-access-A' },
        { tab: 'second', token: 'Bearer rotated-access-A' },
      ]))
      expect(protectedCalls).toHaveLength(4)
      expect(locks.request.mock.calls.map(([name]) => name)).toEqual(['wishhub-auth-refresh', 'wishhub-auth-refresh'])
      expect(first.store.getState()).toMatchObject({ accessToken: rotated.accessToken, refreshToken: rotated.refreshToken })
      expect(second.store.getState()).toMatchObject({ accessToken: rotated.accessToken, refreshToken: rotated.refreshToken })
      // Rotation updates the session's token record, never the authoritative login identity.
      expect(saved().user).toEqual(session().user)
    } finally { delayed.release() }
  })

  it.each([
    { name: 'logout', next: null },
    { name: 'account switch', next: session('B') },
  ])('does not refresh or retry after $name while waiting for the lock', async ({ next }) => {
    const [first, second] = await twoTabs()
    const held = gate()
    const holding = locks.request('wishhub-auth-refresh', () => held.promise)
    requests.push(holding)
    const result = startRequest(first)
    try {
      await vi.waitFor(() => expect(locks.request).toHaveBeenCalledTimes(2))
      if (next) second.store.getState().setAuth(next)
      else second.store.getState().clearAuth()
      // JSDOM does not send StorageEvent for this shared storage write.
      expect(first.store.getState().user?.id).toBe('A')
      held.release()
      const settled = await result
      expect(settled.status).toBe('rejected')
      if (settled.status === 'rejected') expect(isCancel(settled.reason)).toBe(true)
      expect(refreshAdapter).not.toHaveBeenCalled()
      expect(protectedCalls).toEqual([{ tab: 'first', token: 'Bearer access-A' }])
      expect(saved()).toMatchObject({ user: next?.user ?? null, accessToken: next?.accessToken ?? null, refreshToken: next?.refreshToken ?? null })
      expect(first.store.getState().user?.id ?? null).toBe(next?.user.id ?? null)
    } finally { held.release() }
  })

  it('does not restore a logged-out session when a delayed refresh succeeds before its storage event', async () => {
    const [first, second] = await twoTabs()
    const delayed = gate()
    refreshAdapter.mockImplementation(async config => { await delayed.promise; return response(config, rotated) })
    const result = startRequest(first)
    try {
      await vi.waitFor(() => expect(refreshAdapter).toHaveBeenCalledTimes(1))
      second.store.getState().clearAuth()
      expect(first.store.getState().user?.id).toBe('A')
      delayed.release()
      const settled = await result
      expect(settled.status).toBe('rejected')
      if (settled.status === 'rejected') expect(isCancel(settled.reason)).toBe(true)
      expect(saved()).toMatchObject({ user: null, accessToken: null, refreshToken: null })
      expect(first.store.getState().user).toBeNull()
      expect(protectedCalls).toEqual([{ tab: 'first', token: 'Bearer access-A' }])
      expect(refreshAdapter).toHaveBeenCalledTimes(1)
    } finally { delayed.release() }
  })

  it('does not clear a newer shared session when a delayed refresh fails before its storage event', async () => {
    const [first, second] = await twoTabs()
    const delayed = gate()
    refreshAdapter.mockImplementation(async config => { await delayed.promise; throw denied(config) })
    const result = startRequest(first)
    try {
      await vi.waitFor(() => expect(refreshAdapter).toHaveBeenCalledTimes(1))
      second.store.getState().setAuth(session('B'))
      expect(first.store.getState().user?.id).toBe('A')
      delayed.release()
      const settled = await result
      expect(settled.status).toBe('rejected')
      if (settled.status === 'rejected') expect(isCancel(settled.reason)).toBe(true)
      expect(saved()).toMatchObject({ user: session('B').user, accessToken: 'access-B', refreshToken: 'refresh-B' })
      expect(first.store.getState().user?.id).toBe('B')
      expect(protectedCalls).toEqual([{ tab: 'first', token: 'Bearer access-A' }])
    } finally { delayed.release() }
  })
})
