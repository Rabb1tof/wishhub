import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import axios, { AxiosError } from 'axios'
import type { AxiosResponse } from 'axios'
import api from '@/api/client'
import { useAuthStore } from '@/store/auth'
import { QueryProvider } from '@/providers/QueryProvider'
import type { AuthResponse } from '@/types/auth'
import { AuthCallbackPage } from './AuthCallbackPage'
import App from '@/App'

vi.mock('@/hooks/useSignalR', () => ({ useSignalR: vi.fn(), stopConnection: vi.fn() }))
vi.mock('@/utils/notifications', () => ({ requestNotificationPermission: vi.fn() }))

const completedSession: AuthResponse = {
  user: { id: 'completed-user', username: 'completed', displayName: 'Completed user' },
  accessToken: 'completed-access',
  refreshToken: 'completed-refresh',
  expiresAt: '2030-01-01T00:00:00Z',
}
const otherSession: AuthResponse = {
  ...completedSession,
  user: { id: 'current-user', username: 'current', displayName: 'Current user' },
  accessToken: 'current-access',
  refreshToken: 'current-refresh',
}

function response<T>(data: T) {
  return { data, status: 200, statusText: 'OK', headers: {}, config: {} } as AxiosResponse<T>
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function Location() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function mount(entry = '/auth/callback') {
  return render(
    <StrictMode>
      <MemoryRouter initialEntries={[entry]}>
        <QueryProvider><Routes>
          <Route path="/auth/callback" element={<AuthCallbackPage />} />
          <Route path="/" element={<h1>Главная</h1>} />
        </Routes>
        <Location /></QueryProvider>
      </MemoryRouter>
    </StrictMode>,
  )
}

beforeEach(() => {
  useAuthStore.getState().clearAuth()
  vi.spyOn(api, 'post').mockResolvedValue(response(completedSession))
  vi.spyOn(api, 'get').mockImplementation(async (url) => {
    if (url === '/users/me') return response(completedSession.user)
    if (url === '/friends') return response([])
    if (url === '/messages/unread/count') return response(0)
    throw new Error(`Unexpected request: ${url}`)
  })
  // The retired implementation must never make a network request during a RED run.
  vi.spyOn(axios, 'get').mockRejectedValue(new Error('Legacy profile request is not supported'))
  vi.spyOn(console, 'error').mockImplementation(() => {})
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  useAuthStore.getState().clearAuth()
})

describe('OAuth cookie completion', () => {
  it('completes the one-use cookie handoff once in StrictMode', async () => {
    const startingVersion = useAuthStore.getState().sessionVersion
    mount()
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/'))
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
    expect(useAuthStore.getState().sessionVersion).toBe(startingVersion + 1)
    expect(useAuthStore.getState().user).toEqual(completedSession.user)
    expect(axios.get).not.toHaveBeenCalled()
  })

  it('keeps the existing session when the completion cookie is missing or expired', async () => {
    useAuthStore.getState().setAuth(otherSession)
    const failure = new AxiosError('Unauthorized')
    failure.response = { status: 401 } as AxiosResponse
    vi.mocked(api.post).mockRejectedValue(failure)
    mount()
    expect((await screen.findByRole('alert')).textContent).toContain('истекла')
    expect(useAuthStore.getState().user).toEqual(otherSession.user)
    expect(useAuthStore.getState().accessToken).toBe(otherSession.accessToken)
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
    expect(screen.getByTestId('location').textContent).toBe('/auth/callback')
  })

  it('does not accept obsolete URL token parameters or fragments', async () => {
    mount('/auth/callback?accessToken=ignored&refreshToken=ignored&expiresAt=ignored#accessToken=ignored')
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/'))
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
    expect(axios.get).not.toHaveBeenCalled()
    expect(useAuthStore.getState().accessToken).toBe(completedSession.accessToken)
    expect(useAuthStore.getState().refreshToken).toBe(completedSession.refreshToken)
  })

  it('shows provider errors without consuming a completion cookie', async () => {
    mount('/auth/callback?error=invalid_state')
    expect((await screen.findByRole('alert')).textContent).toContain('Проверка состояния OAuth')
    expect(api.post).not.toHaveBeenCalled()
    expect(useAuthStore.getState().user).toBeNull()
  })

  it('ignores a successful completion after leaving the callback page', async () => {
    const pending = deferred<AxiosResponse<AuthResponse>>()
    vi.mocked(api.post).mockReturnValue(pending.promise)
    const view = mount()
    await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
    view.unmount()
    await act(async () => { pending.resolve(response(completedSession)) })
    expect(useAuthStore.getState().user).toBeNull()
  })

  it('does not replace a newer session when an earlier completion finishes', async () => {
    const pending = deferred<AxiosResponse<AuthResponse>>()
    vi.mocked(api.post).mockReturnValue(pending.promise)
    mount()
    await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
    act(() => { useAuthStore.getState().setAuth(otherSession) })
    await act(async () => { pending.resolve(response(completedSession)) })
    expect(useAuthStore.getState().user).toEqual(otherSession.user)
    expect(useAuthStore.getState().accessToken).toBe(otherSession.accessToken)
    expect(screen.getByTestId('location').textContent).toBe('/auth/callback')
  })

  it('shows a retry instruction for an unavailable completion service', async () => {
    vi.mocked(api.post).mockRejectedValue(new Error('Service unavailable'))
    mount()
    expect((await screen.findByRole('alert')).textContent).toContain('Попробуйте войти снова')
    expect(useAuthStore.getState().user).toBeNull()
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
  })
  it.each([
    { name: 'anonymous session', initial: null },
    { name: 'existing session', initial: otherSession },
  ])('leaves loading and preserves the $name when a completed handoff cannot be saved', async ({ initial }) => {
    if (initial) useAuthStore.getState().setAuth(initial)
    const starting = useAuthStore.getState()
    const saved = localStorage.getItem('wishhub-auth')
    const setItem = Storage.prototype.setItem
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key, value) {
      if (key === 'wishhub-auth') throw new DOMException('Storage write denied', 'SecurityError')
      setItem.call(this, key, value)
    })
    mount()
    expect((await screen.findByRole('alert')).textContent).toBe('Не удалось сохранить вход. Проверьте доступ к хранилищу браузера и войдите снова.')
    expect(screen.queryByText('Входим…')).toBeNull()
    expect(screen.getByRole('button', { name: 'Вернуться ко входу' })).toBeDefined()
    expect(screen.getByTestId('location').textContent).toBe('/auth/callback')
    expect(useAuthStore.getState()).toBe(starting)
    expect(localStorage.getItem('wishhub-auth')).toBe(saved)
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
  })

  it('completes once through App while the session version changes and navigates home', async () => {
    const startingVersion = useAuthStore.getState().sessionVersion
    render(<StrictMode><MemoryRouter initialEntries={['/auth/callback']}>
      <QueryProvider><App /><Location /></QueryProvider>
    </MemoryRouter></StrictMode>)
    await screen.findByRole('heading', { name: 'Лента друзей' })
    await screen.findByText('У вас пока нет друзей.')
    expect(screen.getByTestId('location').textContent).toBe('/')
    expect(useAuthStore.getState().sessionVersion).toBe(startingVersion + 1)
    expect(useAuthStore.getState().user).toEqual(completedSession.user)
    expect(api.post).toHaveBeenCalledExactlyOnceWith('/auth/complete', undefined, { headers: { 'X-WishHub-OAuth': '1' } })
    expect(screen.queryByRole('alert')).toBeNull()
    expect(axios.get).not.toHaveBeenCalled()
  })

})
