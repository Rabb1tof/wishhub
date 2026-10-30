import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import api from '@/api/client'
import { useChangePassword } from '@/api/useProfile'
import type { ReactNode } from 'react'
import { useAuthStore } from '@/store/auth'
import { SettingsPage } from './SettingsPage'
import { MessagesPage } from './MessagesPage'
import { FriendsPage } from './FriendsPage'
import { ProfilePage } from './ProfilePage'
import { WishlistPage } from './WishlistPage'

vi.mock('@/hooks/useSignalR', () => ({ useSignalR: vi.fn(), stopConnection: vi.fn() }))
vi.mock('@/utils/notifications', () => ({ requestNotificationPermission: vi.fn() }))
vi.mock('@/components/ConnectedAccountsSection', () => ({ ConnectedAccountsSection: () => null }))
const owner = { id: 'owner', username: 'owner', displayName: 'Owner' }
const friend = { id: 'friend', username: 'friend', displayName: 'Friend' }
const second = { id: 'second', username: 'second', displayName: 'Second' }
const originalAdapter = api.defaults.adapter
let client: QueryClient
let profile: typeof owner & { bio: string; wishlistPrivacy: string; avatarUrl?: string; wishlistItemCount: number; friendshipStatus: null }
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>
function response(config: InternalAxiosRequestConfig, data: unknown, status = 200) {
  return { config, data, status, statusText: String(status), headers: {} }
}
function fail(config: InternalAxiosRequestConfig, status = 503): never {
  throw new AxiosError('Unavailable', 'ERR_BAD_RESPONSE', config, undefined, response(config, {}, status))
}
function Location() { return <output data-testid="location">{useLocation().pathname}</output> }
function mount(path: string) {
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/settings" element={<SettingsPage />} />
    <Route path="/messages" element={<MessagesPage />} />
    <Route path="/friends" element={<FriendsPage />} />
    <Route path="/profile/:username" element={<ProfilePage />} />
    <Route path="/wishlist/:username" element={<WishlistPage />} />
    <Route path="/login" element={<p>Login page</p>} />
  </Routes><Location /></MemoryRouter></QueryClientProvider>)
}
beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } })
  profile = { ...owner, bio: 'Saved bio', wishlistPrivacy: 'friendsOnly', wishlistItemCount: 0, friendshipStatus: null }
  useAuthStore.setState({ user: owner, accessToken: 'test-token', refreshToken: 'test-refresh' })
  Element.prototype.scrollIntoView = vi.fn()
  adapter = vi.fn<AxiosAdapter>(async (config) => {
    if (config.url === '/messages/unread/count') return response(config, 0)
    if (config.url === '/users/me' && config.method === 'get') return response(config, { ...profile })
    if (config.url === '/messages/dialogs') return response(config, [])
    if (config.url === '/friends') return response(config, [{ id: 'relationship', user: friend }])
    if (config.url?.startsWith('/friends/requests')) return response(config, [])
    if (config.url?.startsWith('/messages/') && config.method === 'get') return response(config, [])
    if (config.url?.endsWith('/read-all')) return response(config, null, 204)
    throw new Error(`Unexpected request: ${config.method} ${config.url}`)
  })
  api.defaults.adapter = adapter
})
afterEach(() => { cleanup(); client.clear(); api.defaults.adapter = originalAdapter; useAuthStore.getState().clearAuth(); vi.restoreAllMocks() })

it('preserves dirty settings and selected privacy after an avatar refetch', async () => {
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => {
    if (config.url === '/users/me/avatar') { profile.avatarUrl = '/avatars/new.jpg'; return response(config, { avatarUrl: profile.avatarUrl }) }
    if (config.url === '/users/me' && config.method === 'put') return response(config, profile)
    return normal(config)
  })
  mount('/settings')
  const name = await screen.findByLabelText('Отображаемое имя') as HTMLInputElement
  expect((screen.getByLabelText('Приватность вишлиста') as HTMLSelectElement).value).toBe('friendsOnly')
  expect(name.maxLength).toBe(100)
  expect((screen.getByLabelText('О себе') as HTMLTextAreaElement).maxLength).toBe(500)
  fireEvent.change(name, { target: { value: 'Unsaved name' } })
  fireEvent.change(screen.getByLabelText('О себе'), { target: { value: 'Unsaved bio' } })
  fireEvent.change(screen.getByLabelText('Приватность вишлиста'), { target: { value: 'private' } })
  fireEvent.change(screen.getByLabelText('Фото профиля'), { target: { files: [new File(['photo'], 'avatar.jpg', { type: 'image/jpeg' })] } })
  await waitFor(() => expect(adapter.mock.calls.filter(([c]) => c.url === '/users/me' && c.method === 'get')).toHaveLength(2))
  await waitFor(() => expect(within(screen.getByRole('main')).getByRole('img', { name: 'Owner' }).getAttribute('src')).toBe('/avatars/new.jpg'))
  expect(name.value).toBe('Unsaved name')
  expect((screen.getByLabelText('О себе') as HTMLTextAreaElement).value).toBe('Unsaved bio')
  expect((screen.getByLabelText('Приватность вишлиста') as HTMLSelectElement).value).toBe('private')
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
  await screen.findByText('Профиль обновлён')
  const sent = adapter.mock.calls.find(([c]) => c.url === '/users/me' && c.method === 'put')![0]
  expect(JSON.parse(sent.data)).toEqual({ displayName: 'Unsaved name', bio: 'Unsaved bio', wishlistPrivacy: 'private' })
})

it.each([
  { label: 'profile', status: 503, endpoint: '/users/me', message: 'Не удалось сохранить профиль. Попробуйте ещё раз.' },
  { label: 'password', status: 400, endpoint: '/users/me/password', message: 'Не удалось изменить пароль. Проверьте текущий пароль и повторите попытку.' },
  { label: 'avatar', status: 503, endpoint: '/users/me/avatar', message: 'Не удалось загрузить аватар. Попробуйте ещё раз.' },
])('shows a retryable settings error for $label without clearing inputs', async ({ label, endpoint, message, status }) => {
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => config.url === endpoint && config.method !== 'get' ? fail(config, status) : normal(config))
  mount('/settings')
  await screen.findByLabelText('Отображаемое имя')
  if (label === 'profile') {
    fireEvent.change(screen.getByLabelText('Отображаемое имя'), { target: { value: 'Keep me' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
  } else if (label === 'password') {
    expect((screen.getByLabelText('Новый пароль') as HTMLInputElement).minLength).toBe(8)
    expect((screen.getByLabelText('Новый пароль') as HTMLInputElement).maxLength).toBe(100)
    expect((screen.getByLabelText('Текущий пароль') as HTMLInputElement).required).toBe(true)
    fireEvent.change(screen.getByLabelText('Текущий пароль'), { target: { value: 'Old-password1!' } })
    fireEvent.change(screen.getByLabelText('Новый пароль'), { target: { value: 'New-password1!' } })
    fireEvent.change(screen.getByLabelText('Подтвердите новый пароль'), { target: { value: 'New-password1!' } })
    fireEvent.click(screen.getByRole('button', { name: 'Изменить пароль' }))
  } else {
    fireEvent.change(screen.getByLabelText('Фото профиля'), { target: { files: [new File(['photo'], 'avatar.jpg', { type: 'image/jpeg' })] } })
  }
  expect((await screen.findByRole('alert')).textContent).toBe(message)
  if (label === 'profile') expect((screen.getByLabelText('Отображаемое имя') as HTMLInputElement).value).toBe('Keep me')
  if (label === 'password') expect((screen.getByLabelText('Новый пароль') as HTMLInputElement).value).toBe('New-password1!')
})

it('starts the first conversation with a selected friend who has no dialog', async () => {
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => config.url === '/messages/friend' && config.method === 'post'
    ? response(config, { id: 'message', senderId: owner.id, receiverId: friend.id, content: 'Hello', isRead: false, createdAt: '2026-10-30T00:00:00Z' }) : normal(config))
  mount('/messages?user=friend')
  fireEvent.change(await screen.findByPlaceholderText('Написать сообщение...'), { target: { value: 'Hello' } })
  fireEvent.click(screen.getByRole('button', { name: 'Отправить' }))
  await waitFor(() => expect(adapter.mock.calls.some(([c]) => c.url === '/messages/friend' && c.method === 'post')).toBe(true))
  expect(JSON.parse(adapter.mock.calls.find(([c]) => c.url === '/messages/friend' && c.method === 'post')![0].data)).toEqual({ content: 'Hello' })
})

it('keeps recipient drafts isolated while the previous send completes', async () => {
  let finish!: () => void
  const pending = new Promise<void>((resolve) => { finish = resolve })
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => {
    if (config.url === '/messages/dialogs') return response(config, [friend, second].map((user) => ({ user, lastMessage: null, unreadCount: 0 })))
    if (config.url === '/messages/friend' && config.method === 'post') { await pending; return response(config, {}) }
    return normal(config)
  })
  mount('/messages?user=friend')
  fireEvent.change(await screen.findByPlaceholderText('Написать сообщение...'), { target: { value: 'For friend only' } })
  fireEvent.click(screen.getByRole('button', { name: 'Отправить' }))
  fireEvent.click(screen.getByRole('button', { name: /Second/ }))
  const input = await screen.findByPlaceholderText('Написать сообщение...') as HTMLInputElement
  expect(input.value).toBe('')
  fireEvent.change(input, { target: { value: 'For second only' } })
  await act(async () => { finish() })
  expect(input.value).toBe('For second only')
  expect(adapter.mock.calls.filter(([c]) => c.method === 'post' && !c.url?.endsWith('/read-all')).map(([c]) => c.url)).toEqual(['/messages/friend'])
})

it('removes an accepted friend by user id and refreshes the list', async () => {
  let removed = false
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => {
    if (config.url === '/friends/friend' && config.method === 'delete') { removed = true; return response(config, null, 204) }
    if (config.url === '/friends' && removed) return response(config, [])
    return normal(config)
  })
  mount('/friends')
  fireEvent.click(await screen.findByRole('button', { name: 'Удалить' }))
  await screen.findByText('У вас пока нет друзей')
  expect(adapter.mock.calls.filter(([c]) => c.method === 'delete').map(([c]) => c.url)).toEqual(['/friends/friend'])
})

it('renders a public profile without protected unread requests or login redirection', async () => {
  useAuthStore.getState().clearAuth()
  adapter.mockImplementation(async (config) => {
    if (config.url === '/users/friend') return response(config, { ...profile, ...friend })
    if (config.url === '/wishlist/user/friend') return response(config, [{ id: 'gift', url: 'https://shop.example/gift', name: 'Public gift', imageUrl: null, price: null, currency: 'RUB', source: 'Unknown', sortOrder: 0, createdAt: '2026-10-30T00:00:00Z', isReserved: false, isReservedByMe: false, reservedBy: null }])
    throw new Error(`Protected request: ${config.url}`)
  })
  mount('/profile/friend')
  await screen.findByRole('heading', { name: 'Friend' })
  await waitFor(() => expect(adapter.mock.calls.some(([c]) => c.url === '/wishlist/user/friend')).toBe(true))
  expect(screen.getByTestId('location').textContent).toBe('/profile/friend')
  expect(adapter.mock.calls.map(([c]) => c.url).sort()).toEqual(['/users/friend', '/wishlist/user/friend'])
  expect(screen.queryByRole('button', { name: 'Добавить в друзья' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Зарезервировать' })).toBeNull()
})



it('returns to login and removes credentials after a successful password change', async () => {
  const normal = adapter.getMockImplementation()!
  adapter.mockImplementation(async (config) => config.url === '/users/me/password'
    ? response(config, '', 204) : normal(config))
  mount('/settings')
  await screen.findByLabelText('Текущий пароль')
  fireEvent.change(screen.getByLabelText('Текущий пароль'), { target: { value: 'Old-password1!' } })
  fireEvent.change(screen.getByLabelText('Новый пароль'), { target: { value: 'New-password1!' } })
  fireEvent.change(screen.getByLabelText('Подтвердите новый пароль'), { target: { value: 'New-password1!' } })
  fireEvent.click(screen.getByRole('button', { name: 'Изменить пароль' }))
  await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login'))
  expect(useAuthStore.getState()).toMatchObject({ user: null, accessToken: null, refreshToken: null })
  expect(screen.queryByLabelText('Новый пароль')).toBeNull()
  expect(JSON.parse(adapter.mock.calls.find(([c]) => c.url === '/users/me/password')![0].data)).toEqual({
    currentPassword: 'Old-password1!', newPassword: 'New-password1!',
  })
})

it('ignores a completed password request from the previous session', async () => {
  let finish!: () => void
  const pending = new Promise<void>((resolve) => { finish = resolve })
  adapter.mockImplementation(async (config) => { await pending; return response(config, '', 204) })
  const { result } = renderHook(() => useChangePassword(), {
    wrapper: ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
  })
  let outcome!: Promise<unknown>
  act(() => {
    outcome = result.current.mutateAsync({ currentPassword: 'Old-password1!', newPassword: 'New-password1!' })
      .then(() => null, (error: unknown) => error)
  })
  await waitFor(() => expect(adapter).toHaveBeenCalledTimes(1))
  act(() => useAuthStore.getState().setAuth({ user: second, accessToken: 'second-access', refreshToken: 'second-refresh', expiresAt: '2026-10-31T00:00:00Z' }))
  const version = useAuthStore.getState().sessionVersion
  await act(async () => { finish(); await outcome })
  expect(await outcome).toMatchObject({ code: 'ERR_CANCELED' })
  expect(useAuthStore.getState()).toMatchObject({ user: second, accessToken: 'second-access', refreshToken: 'second-refresh', sessionVersion: version })
})

it('opens all public wishlist items from an anonymous profile without protected requests', async () => {
  useAuthStore.getState().clearAuth()
  const items = Array.from({ length: 4 }, (_, index) => ({
    id: `gift-${index}`, url: `https://shop.example/gift-${index}`, name: `Public gift ${index + 1}`,
    imageUrl: null, price: null, currency: 'RUB', source: 'Unknown', sortOrder: index,
    createdAt: '2026-10-30T00:00:00Z', isReserved: false, isReservedByMe: false, reservedBy: null,
  }))
  adapter.mockImplementation(async (config) => {
    if (config.url === '/users/friend') return response(config, { ...profile, ...friend, wishlistPrivacy: 'public', wishlistItemCount: 4 })
    if (config.url === '/wishlist/user/friend') return response(config, items)
    throw new Error(`Protected request: ${config.url}`)
  })
  mount('/profile/friend')
  await screen.findByRole('heading', { name: 'Public gift 3' })
  expect(screen.queryByRole('heading', { name: 'Public gift 4' })).toBeNull()
  fireEvent.click(screen.getByRole('link', { name: 'Показать все' }))
  await screen.findByRole('heading', { name: 'Public gift 4' })
  expect(screen.getByTestId('location').textContent).toBe('/wishlist/friend')
  expect(screen.getAllByRole('heading', { name: /Public gift/ }).map((heading) => heading.textContent)).toEqual(items.map((item) => item.name))
  expect(new Set(adapter.mock.calls.map(([config]) => config.url))).toEqual(new Set(['/users/friend', '/wishlist/user/friend']))
  expect(screen.queryByRole('button', { name: 'Добавить' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Зарезервировать' })).toBeNull()
})
