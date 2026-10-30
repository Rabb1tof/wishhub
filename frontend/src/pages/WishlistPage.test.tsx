import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import api from '@/api/client'
import { useAuthStore } from '@/store/auth'
import type { WishlistItem } from '@/types/wishlist'
import { WishlistPage } from './WishlistPage'

vi.mock('@/hooks/useSignalR', () => ({ useSignalR: () => undefined }))

const owner = { id: 'owner-1', username: 'owner', displayName: 'Owner' }
const profile = { ...owner, createdAt: '2026-10-30T00:00:00Z', wishlistPrivacy: 'public', wishlistItemCount: 1 }
const item: WishlistItem = {
  id: 'item-1', url: 'https://shop.example/item?sku=blue', name: 'Flat gift',
  imageUrl: 'https://images.example/gift.jpg', price: 0, currency: 'RUB', source: 'shop.example',
  sortOrder: 7, createdAt: '2026-10-30T00:00:00Z', isReserved: true, isReservedByMe: false,
  reservedBy: { id: 'friend-1', username: 'secret', displayName: 'Secret giver' },
}
const originalAdapter = api.defaults.adapter
let client: QueryClient
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

function response(config: InternalAxiosRequestConfig, data: unknown, status = 200) {
  return { config, data, status, statusText: String(status), headers: {} }
}
function renderPage(path = '/wishlist') {
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes><Route path="/wishlist/:username?" element={<WishlistPage />} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } })
  useAuthStore.setState({ user: owner, accessToken: 'test-token', refreshToken: null })
  adapter = vi.fn<AxiosAdapter>(async (config) => {
    if (config.url === '/messages/unread/count') return response(config, 0)
    if (config.url === '/users/owner') return response(config, profile)
    if (config.url === '/wishlist' || config.url === '/wishlist/user/owner-1') return response(config, [item])
    throw new Error(`Unexpected request: ${config.method} ${config.url}`)
  })
  api.defaults.adapter = adapter
})
afterEach(() => {
  cleanup()
  client.clear()
  useAuthStore.getState().clearAuth()
  api.defaults.adapter = originalAdapter
  vi.restoreAllMocks()
})

describe('WishlistPage', () => {
  it.each([
    { Name: 'default owner route', path: '/wishlist', endpoint: '/wishlist' },
    { Name: 'named owner route', path: '/wishlist/owner', endpoint: '/wishlist/user/owner-1' },
  ])('hides reservation data on the $Name', async ({ path, endpoint }) => {
    renderPage(path)
    expect(await screen.findByRole('heading', { name: 'Flat gift' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Редактировать' })).toBeTruthy()
    expect(screen.queryByText(/Зарезервировано|Secret giver/)).toBeNull()
    expect(screen.queryByRole('button', { name: /Зарезервировать|Отменить резервирование/ })).toBeNull()
    expect(screen.getByText('0 RUB')).toBeTruthy()
    expect(screen.getByRole('link', { name: /Flat gift/ }).getAttribute('href')).toBe(item.url)
    const wishlistRequests = adapter.mock.calls.map(([config]) => config.url).filter((url) => url?.startsWith('/wishlist'))
    expect(wishlistRequests).toEqual([endpoint])
    expect(adapter.mock.calls.some(([config]) => config.url?.includes('metadata'))).toBe(false)
  })

  it('opens the editable card with every existing field and no metadata request', async () => {
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Редактировать' }))
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect((screen.getByLabelText('Ссылка на товар') as HTMLInputElement).value).toBe(item.url)
    expect((screen.getByLabelText('Название') as HTMLInputElement).value).toBe(item.name)
    expect((screen.getByLabelText('Фото: URL (необязательно)') as HTMLInputElement).value).toBe(item.imageUrl)
    expect((screen.getByLabelText('Цена (необязательно)') as HTMLInputElement).value).toBe('0')
    expect((screen.getByLabelText('Валюта') as HTMLInputElement).value).toBe('RUB')
    expect(adapter.mock.calls.some(([config]) => config.url?.includes('metadata'))).toBe(false)
  })

  it('shows a missing price and image without a loading state', async () => {
    client.setQueryData(['wishlist', 'me'], { items: [{ ...item, price: null, imageUrl: null }], hidden: false })
    client.setQueryDefaults(['wishlist'], { staleTime: Infinity })
    renderPage()
    expect(screen.getByRole('heading', { name: 'Flat gift' })).toBeTruthy()
    expect(screen.getByText('Цена не указана')).toBeTruthy()
    expect(screen.getByText('Нет изображения')).toBeTruthy()
    expect(screen.queryByText(/Загрузка цены|Загрузка\.\.\./)).toBeNull()
  })

  it('preserves the card and shows an error when deletion fails', async () => {
    const normalAdapter = adapter.getMockImplementation()!
    adapter.mockImplementation(async (config) => {
      if (config.method === 'delete') throw new AxiosError('Failed', 'ERR_BAD_RESPONSE', config, undefined, response(config, {}, 500))
      return normalAdapter(config)
    })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Удалить' }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'Не удалось удалить товар.')
    expect(screen.getByRole('heading', { name: 'Flat gift' })).toBeTruthy()
    expect(adapter.mock.calls.filter(([config]) => config.method === 'delete').map(([config]) => config.url)).toEqual(['/wishlist/item-1'])
  })

  it('waits for the requested profile instead of flashing the owners wishlist', async () => {
    let completeProfile: (() => void) | undefined
    const waitingProfile = new Promise<void>((resolve) => { completeProfile = resolve })
    const normalAdapter = adapter.getMockImplementation()!
    adapter.mockImplementation(async (config) => {
      if (config.url === '/users/owner') await waitingProfile
      return normalAdapter(config)
    })
    renderPage('/wishlist/owner')
    await waitFor(() => expect(adapter.mock.calls.some(([config]) => config.url === '/users/owner')).toBe(true))
    expect(adapter.mock.calls.some(([config]) => config.url?.startsWith('/wishlist'))).toBe(false)
    expect(screen.queryByRole('heading', { name: 'Flat gift' })).toBeNull()
    await act(async () => { completeProfile?.() })
    expect(await screen.findByRole('heading', { name: 'Flat gift' })).toBeTruthy()
    expect(adapter.mock.calls.filter(([config]) => config.url?.startsWith('/wishlist')).map(([config]) => config.url)).toEqual(['/wishlist/user/owner-1'])
  })
})
