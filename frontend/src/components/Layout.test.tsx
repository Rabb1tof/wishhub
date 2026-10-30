import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosAdapter } from 'axios'
import api from '@/api/client'
import { useAuthStore } from '@/store/auth'
import { stopConnection } from '@/hooks/useSignalR'
import { requestNotificationPermission } from '@/utils/notifications'
import { Layout } from './Layout'
import { UserAvatar } from './UserAvatar'

vi.mock('@/hooks/useSignalR', () => ({ useSignalR: vi.fn(), stopConnection: vi.fn() }))
vi.mock('@/utils/notifications', () => ({ requestNotificationPermission: vi.fn() }))

const user = { id: 'owner', username: 'lina', displayName: 'Лина Крылова' }
const originalAdapter = api.defaults.adapter
let client: QueryClient
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

function Location() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function mount() {
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/wishlist']}>
        <Layout><h1>Мой вишлист</h1></Layout>
        <Location />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  useAuthStore.setState({ user, accessToken: 'test-token', refreshToken: null })
  adapter = vi.fn<AxiosAdapter>(async (config) => {
    if (config.url === '/messages/unread/count') return { config, data: 3, status: 200, statusText: 'OK', headers: {} }
    throw new Error(`Unexpected request: ${config.url}`)
  })
  api.defaults.adapter = adapter
  vi.clearAllMocks()
})

afterEach(() => {
  cleanup()
  client.clear()
  api.defaults.adapter = originalAdapter
  useAuthStore.getState().clearAuth()
  document.body.style.overflow = ''
})

describe('Layout navigation', () => {
  it('opens a labelled mobile menu with real routes and live unread count then closes on navigation', async () => {
    mount()
    const menu = screen.getByRole('button', { name: 'Меню' })
    expect(menu.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(menu)
    const sheet = screen.getByRole('dialog', { name: 'Меню WishHub' })
    expect(menu.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('main', { hidden: true }).hasAttribute('inert')).toBe(true)
    expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Закрыть меню' }))
    for (const [label, href] of [['Лента', '/'], ['Мой вишлист', '/wishlist'], ['Друзья', '/friends'], ['Настройки', '/settings']]) {
      expect(within(sheet).getByRole('link', { name: label }).getAttribute('href')).toBe(href)
    }
    expect(within(sheet).getByRole('link', { name: /Лина Крылова/ }).getAttribute('href')).toBe('/profile/lina')
    expect(within(sheet).getByRole('link', { name: 'Мой вишлист' }).getAttribute('aria-current')).toBe('page')
    const messages = await within(sheet).findByRole('link', { name: /3 непрочитанных сообщений/ })
    expect(messages.getAttribute('href')).toBe('/messages')
    fireEvent.click(messages)
    expect(screen.getByTestId('location').textContent).toBe('/messages')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByRole('main').hasAttribute('inert')).toBe(false)
    expect(menu.getAttribute('aria-expanded')).toBe('false')
  })

  it('keeps keyboard focus inside the open menu and restores focus and scrolling on Escape', () => {
    document.body.style.overflow = 'scroll'
    mount()
    const menu = screen.getByRole('button', { name: 'Меню' })
    fireEvent.click(menu)
    const sheet = screen.getByRole('dialog', { name: 'Меню WishHub' })
    const first = within(sheet).getByRole('link', { name: 'WishHub' })
    const last = within(sheet).getByRole('button', { name: 'Выйти' })
    expect(document.body.style.overflow).toBe('hidden')
    first.focus()
    fireEvent.keyDown(first, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(last)
    fireEvent.keyDown(last, { key: 'Tab' })
    expect(document.activeElement).toBe(first)
    fireEvent.keyDown(first, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(menu)
    expect(document.body.style.overflow).toBe('scroll')
  })

  it('keeps the account and shows a retryable logout error before a successful retry', async () => {
    let rejectLogout = true
    const normalAdapter = adapter.getMockImplementation()!
    adapter.mockImplementation(async (config) => {
      if (config.url !== '/auth/logout') return normalAdapter(config)
      if (rejectLogout) throw new AxiosError('Unavailable', 'ERR_BAD_RESPONSE', config, undefined, { config, data: {}, status: 503, statusText: 'Unavailable', headers: {} })
      return { config, data: null, status: 204, statusText: 'No Content', headers: {} }
    })
    mount()
    fireEvent.click(screen.getByRole('button', { name: 'Меню' }))
    const sheet = screen.getByRole('dialog', { name: 'Меню WishHub' })
    fireEvent.click(within(sheet).getByRole('button', { name: 'Выйти' }))
    expect((await within(sheet).findByRole('alert')).textContent).toBe('Не удалось выйти. Попробуйте ещё раз.')
    expect(useAuthStore.getState().user).toEqual(user)
    expect(screen.getByTestId('location').textContent).toBe('/wishlist')
    expect((within(sheet).getByRole('button', { name: 'Выйти' }) as HTMLButtonElement).disabled).toBe(false)
    rejectLogout = false
    fireEvent.click(within(sheet).getByRole('button', { name: 'Выйти' }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login'))
    expect(useAuthStore.getState().user).toBeNull()
    expect(stopConnection).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls.filter(([config]) => config.url === '/auth/logout').map(([config]) => config.method)).toEqual(['post', 'post'])
  })

  it('does not request notification permission again when the same account rerenders', async () => {
    mount()
    expect(requestNotificationPermission).toHaveBeenCalledTimes(1)
    await act(async () => { useAuthStore.setState({ user: { ...user, displayName: 'Лина' } }) })
    expect(requestNotificationPermission).toHaveBeenCalledTimes(1)
  })
})

it('keeps a stable avatar tint and recovers from a broken avatar when its URL changes', () => {
  const { rerender } = render(<UserAvatar displayName="Лина Крылова" />)
  const tint = (screen.getByRole('img', { name: 'Лина Крылова' }) as HTMLElement).style.backgroundColor
  expect(screen.getByRole('img', { name: 'Лина Крылова' }).textContent).toBe('Л')
  rerender(<UserAvatar displayName="Лина Крылова" avatarUrl="/avatars/broken.png" />)
  fireEvent.error(screen.getByRole('img', { name: 'Лина Крылова' }))
  expect((screen.getByRole('img', { name: 'Лина Крылова' }) as HTMLElement).style.backgroundColor).toBe(tint)
  expect(screen.getByRole('img', { name: 'Лина Крылова' }).textContent).toBe('Л')
  rerender(<UserAvatar displayName="Лина Крылова" avatarUrl="/avatars/new.png" />)
  expect(screen.getByRole('img', { name: 'Лина Крылова' }).getAttribute('src')).toBe('/avatars/new.png')
})
