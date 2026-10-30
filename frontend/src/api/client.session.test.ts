import { AxiosError, isCancel } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
let api: typeof import('./client').default
let useAuthStore: typeof import('@/store/auth').useAuthStore
let axios: typeof import('axios').default

let originalApiAdapter: typeof import('axios').default.defaults.adapter
let originalRawAdapter: typeof import('axios').default.defaults.adapter
const auth = (id = 'A', token = `access-${id}`) => ({ user: { id, username: id, displayName: id }, accessToken: token, refreshToken: `refresh-${id}`, expiresAt: '2099-01-01T00:00:00Z' })
const response = (config: InternalAxiosRequestConfig, data: unknown, status = 200) => ({config, data, status, statusText: String(status), headers: {}})
const denied = (config: InternalAxiosRequestConfig) => new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 401))
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return {promise, resolve} }
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>
beforeEach(async () => {
  vi.resetModules()
  axios = (await import('axios')).default
  useAuthStore = (await import('@/store/auth')).useAuthStore
  api = (await import('./client')).default
  originalApiAdapter = api.defaults.adapter
  originalRawAdapter = axios.defaults.adapter
  useAuthStore.getState().setAuth(auth())
  adapter = vi.fn<AxiosAdapter>(async config => response(config, {}))
  api.defaults.adapter = adapter
  axios.defaults.adapter = adapter
})
afterEach(() => {
  useAuthStore.getState().clearAuth()
  api.defaults.adapter = originalApiAdapter
  axios.defaults.adapter = originalRawAdapter
  vi.restoreAllMocks()
})

describe('session HTTP boundaries', () => {
  it('refreshes concurrent failures once and retries with the new token', async () => {
    const ready = deferred<void>()
    const refreshed = deferred<void>()
    adapter.mockImplementation(async config => {
      if (config.url?.endsWith('/auth/refresh')) { ready.resolve(); await refreshed.promise; return response(config, { ...auth('A', 'new-A'), refreshToken: 'rotated-A' }) }
      if (config.headers.Authorization !== 'Bearer new-A') throw denied(config)
      return response(config, config.url)
    })
    const one = api.get('/wishlist')
    const two = api.get('/messages/dialogs')
    await ready.promise
    refreshed.resolve()
    expect((await Promise.all([one, two])).map(r => r.data)).toEqual(['/wishlist', '/messages/dialogs'])
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'new-A', refreshToken: 'rotated-A' })
    expect(adapter.mock.calls.filter(([c]) => c.url?.endsWith('/auth/refresh'))).toHaveLength(1)
  })

  it('settles a rejected refresh and clears the session instead of queueing itself', async () => {
    adapter.mockImplementation(async config => { throw denied(config) })
    await expect(api.get('/wishlist')).rejects.toMatchObject({response: {status: 401}})
    expect(useAuthStore.getState().user).toBeNull()
    expect(adapter).toHaveBeenCalledTimes(2)
  }, 500)

  it('does not refresh a request more than once', async () => {
    let refreshes = 0
    adapter.mockImplementation(async config => {
      if (config.url?.endsWith('/auth/refresh')) {
        if (++refreshes > 1) throw new Error('Retry exceeded')
        return response(config, auth('A', 'new-A'))
      }
      throw denied(config)
    })
    await expect(api.get('/wishlist')).rejects.toMatchObject({response: {status: 401}})
    expect(refreshes).toBe(1)
    expect(adapter).toHaveBeenCalledTimes(3)
  })

  it.each([{name:'login',url:'/auth/login'},{name:'registration',url:'/auth/register'},{name:'completion',url:'/auth/complete'},{name:'telegram',url:'/auth/telegram'}])('leaves the session intact for $name rejection', async ({url}) => {
    adapter.mockImplementation(async config => { if(config.url === url) throw denied(config); throw new Error('Unexpected refresh') })
    await expect(api.post(url, {})).rejects.toMatchObject({response: {status: 401}})
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(useAuthStore.getState().user?.id).toBe('A')
  })

  it('does not refresh an anonymous request', async () => {
    useAuthStore.getState().clearAuth()
    adapter.mockImplementation(async config => { if(config.url === '/wishlist') throw denied(config); throw new Error('Unexpected refresh') })
    await expect(api.get('/wishlist')).rejects.toMatchObject({response: {status: 401}})
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it.each([{name:'logout',next:null},{name:'account switch',next:auth('B')}])('ignores refresh completion after $name', async ({next}) => {
    const ready = deferred<void>()
    const late = deferred<void>()
    adapter.mockImplementation(async config => {
      if (config.url?.endsWith('/auth/refresh')) { ready.resolve(); await late.promise; return response(config, auth('A','late-A')) }
      if (config.headers.Authorization === 'Bearer late-A') return response(config, 'private-A')
      throw denied(config)
    })
    const result = api.get('/wishlist').catch(error => error)
    await ready.promise
    if(next) useAuthStore.getState().setAuth(next); else useAuthStore.getState().clearAuth()
    late.resolve()
    expect(isCancel(await result)).toBe(true)
    expect(useAuthStore.getState().user?.id ?? null).toBe(next?.user.id ?? null)
    expect(useAuthStore.getState().accessToken).toBe(next?.accessToken ?? null)
    expect(adapter).toHaveBeenCalledTimes(2)
  })

  it('does not clear a new session when an old refresh fails', async () => {
    const ready = deferred<void>(); const late = deferred<void>()
    adapter.mockImplementation(async config => { if(config.url?.endsWith('/auth/refresh')) { ready.resolve(); await late.promise }; throw denied(config) })
    const result = api.get('/wishlist').catch(error => error)
    await ready.promise
    useAuthStore.getState().setAuth(auth('B'))
    late.resolve()
    expect(isCancel(await result)).toBe(true)
    expect(useAuthStore.getState().user?.id).toBe('B')
  })

  it('aborts in-flight requests and rejects their late data on identity change', async () => {
    const ready = deferred<InternalAxiosRequestConfig>(); const late = deferred<void>()
    adapter.mockImplementation(async config => { ready.resolve(config); await late.promise; return response(config, 'private-A') })
    const result = api.get('/messages/dialogs').catch(error => error)
    const config = await ready.promise
    useAuthStore.getState().setAuth(auth('B'))
    const aborted = config.signal?.aborted
    late.resolve()
    expect(aborted).toBe(true)
    expect(isCancel(await result)).toBe(true)
  })
  it('binds a request to the session at invocation before another login', async () => {
    const ready = deferred<InternalAxiosRequestConfig>(); const late = deferred<void>()
    adapter.mockImplementation(async config => { ready.resolve(config); await late.promise; return response(config, 'old-result') })
    const pending = api.post('/wishlist', { name: 'A draft' }).catch(error => error)
    useAuthStore.getState().setAuth(auth('B'))
    const sent = await ready.promise
    late.resolve()
    const result = await pending
    expect(sent.headers.Authorization).toBe('Bearer access-A')
    expect(isCancel(result)).toBe(true)
  })

  it('refreshes protected linked-account operations once', async () => {
    adapter.mockImplementation(async config => {
      if (config.url?.endsWith('/auth/refresh')) return response(config, auth('A', 'new-A'))
      if (config.headers.Authorization !== 'Bearer new-A') throw denied(config)
      return response(config, { url: '/normal-provider-route' })
    })
    await expect(api.post('/auth/link/vk/start')).resolves.toMatchObject({ data: { url: '/normal-provider-route' } })
    expect(adapter.mock.calls.filter(([c]) => c.url?.endsWith('/auth/refresh'))).toHaveLength(1)
  })

})
