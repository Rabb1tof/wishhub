import { formatItemSource } from '@/utils/wishlist'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import type { ReactElement } from 'react'
import type { AxiosAdapter } from 'axios'
import api from '@/api/client'
import { useAuthStore } from '@/store/auth'
import type { WishlistItem } from '@/types/wishlist'
import { ProfilePage } from '@/pages/ProfilePage'
import { FeedPage } from '@/pages/FeedPage'
import { WishlistPage } from '@/pages/WishlistPage'
import { ItemImage } from './ItemImage'
import { ItemReservation } from './ItemReservation'

// The page tests exercise real layout and query hooks without opening a chat connection.
vi.mock('@/hooks/useSignalR', () => ({ useSignalR: () => undefined }))

const originalAdapter = api.defaults.adapter
const clients: QueryClient[] = []
const item: WishlistItem = {
  id: 'gift', url: 'https://shop.example/gift?variant=blue', name: 'Zero gift',
  imageUrl: 'https://images.example/gift.png', price: 0, currency: 'RUB',
  source: 'Example', sortOrder: 3, createdAt: '2026-10-30T00:00:00Z',
  isReserved: false, isReservedByMe: false, reservedBy: null,
}
const reserver = { id: 'reserver', username: 'secret', displayName: 'Secret reserver' }
const owner = { id: 'owner', username: 'owner', displayName: 'Wishlist owner' }

function mockApi(routes: Record<string, unknown>) {
  const adapter = vi.fn<AxiosAdapter>(async (config) => {
    const route = `${config.method} ${config.url}`
    if (!Object.hasOwn(routes, route)) throw new Error(`Unexpected API request: ${route}`)
    const data = routes[route]
    if (data instanceof Error) throw data
    return { data, status: 200, statusText: 'OK', headers: {}, config }
  })
  api.defaults.adapter = adapter
  return adapter
}

function mount(element: ReactElement, path = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  clients.push(client)
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  useAuthStore.setState({ user: owner, accessToken: 'test-token', refreshToken: null })
})

afterEach(() => {
  cleanup()
  for (const client of clients.splice(0)) client.clear()
  api.defaults.adapter = originalAdapter
  useAuthStore.setState({ user: null, accessToken: null, refreshToken: null })
})

describe('ItemImage', () => {
  it.each([
    { caseName: 'missing', imageUrl: null },
    { caseName: 'malformed', imageUrl: 'not a URL' },
    { caseName: 'relative', imageUrl: '/local.png' },
    { caseName: 'missing authority separator', imageUrl: 'https:images.example/gift.png' },
    { caseName: 'script', imageUrl: 'javascript:alert(1)' },
    { caseName: 'data', imageUrl: 'data:image/png;base64,AA==' },
  ])('renders placeholder for $caseName image', ({ imageUrl }) => {
    render(<ItemImage imageUrl={imageUrl} name="Gift" />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения').textContent).toBe('Нет изображения')
  })

  it.each([
    { caseName: 'https', url: 'https://images.example/gift.png?variant=blue' },
    { caseName: 'http', url: 'http://images.example/gift.png' },
  ])('loads $caseName image directly with lazy loading and no referrer', ({ url }) => {
    render(<ItemImage imageUrl={url} name="Gift" />)
    const image = screen.getByRole('img', { name: 'Gift' })
    expect(image.getAttribute('src')).toBe(url)
    expect(image.getAttribute('loading')).toBe('lazy')
    expect(image.getAttribute('referrerpolicy')).toBe('no-referrer')
  })

  it('replaces a broken image with placeholder and loads a changed URL', () => {
    const { rerender } = render(<ItemImage imageUrl="https://images.example/broken.png" name="Gift" />)
    fireEvent.error(screen.getByRole('img', { name: 'Gift' }))
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения').textContent).toBe('Нет изображения')
    rerender(<ItemImage imageUrl="https://images.example/replacement.png" name="Edited gift" />)
    expect(screen.getByRole('img', { name: 'Edited gift' }).getAttribute('src')).toBe('https://images.example/replacement.png')
  })
})

describe('ItemReservation', () => {
  it('hides all reservation details and actions from owner even for a reserved payload', () => {
    const adapter = mockApi({})
    const { container } = mount(<ItemReservation item={{ ...item, isReserved: true, isReservedByMe: true, reservedBy: reserver }} isOwner />)
    expect(container.innerHTML).toBe('')
    expect(adapter).not.toHaveBeenCalled()
  })

  it('shows the nested reserver without offering cancellation for another user', () => {
    const adapter = mockApi({})
    mount(<ItemReservation item={{ ...item, isReserved: true, reservedBy: reserver }} isOwner={false} />)
    expect(screen.getByText('Зарезервировано: Secret reserver').textContent).toBe('Зарезервировано: Secret reserver')
    expect(screen.queryByRole('button')).toBeNull()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('shows a reserved fallback when reserver details are absent', () => {
    mount(<ItemReservation item={{ ...item, isReserved: true }} isOwner={false} />)
    expect(screen.getByText('Зарезервировано').textContent).toBe('Зарезервировано')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('cancels only when isReservedByMe is true', async () => {
    const adapter = mockApi({ 'post /wishlist/gift/cancel-reservation': item })
    mount(<ItemReservation item={{ ...item, isReserved: true, isReservedByMe: true, reservedBy: reserver }} isOwner={false} />)
    expect(screen.getByText('Зарезервировано вами').textContent).toBe('Зарезервировано вами')
    fireEvent.click(screen.getByRole('button', { name: 'Отменить резервирование' }))
    await waitFor(() => expect(adapter).toHaveBeenCalledTimes(1))
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'post', url: '/wishlist/gift/cancel-reservation' })
  })

  it('reserves an available item through the typed wishlist API', async () => {
    const adapter = mockApi({ 'post /wishlist/gift/reserve': { ...item, isReserved: true, isReservedByMe: true } })
    mount(<ItemReservation item={item} isOwner={false} />)
    fireEvent.click(screen.getByRole('button', { name: 'Зарезервировать' }))
    await waitFor(() => expect(adapter).toHaveBeenCalledTimes(1))
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'post', url: '/wishlist/gift/reserve' })
  })

  it.each([
    { caseName: 'reserve', reserved: false, endpoint: 'reserve', label: 'Зарезервировать' },
    { caseName: 'cancel', reserved: true, endpoint: 'cancel-reservation', label: 'Отменить резервирование' },
  ])('keeps $caseName action usable after a request error', async ({ reserved, endpoint, label }) => {
    const adapter = mockApi({ [`post /wishlist/gift/${endpoint}`]: new Error('Network unavailable') })
    mount(<ItemReservation item={{ ...item, isReserved: reserved, isReservedByMe: reserved }} isOwner={false} />)
    fireEvent.click(screen.getByRole('button', { name: label }))
    expect((await screen.findByRole('alert')).textContent).toBe('Не удалось изменить резервирование. Попробуйте ещё раз.')
    expect((screen.getByRole('button', { name: label }) as HTMLButtonElement).disabled).toBe(false)
    expect(adapter).toHaveBeenCalledTimes(1)
  })
})

describe('Wishlist card pages', () => {
  const profile = { ...owner, createdAt: item.createdAt, wishlistPrivacy: 'public', friendshipStatus: null, wishlistItemCount: 2 }
  const noPriceItem = { ...item, id: 'no-price', name: 'Gift without price', price: null, imageUrl: null }

  it('renders flat profile cards with zero and absent prices while hiding owner reservations', async () => {
    const adapter = mockApi({
      'get /users/owner': profile,
      'get /wishlist/user/owner': [
        { ...item, isReserved: true, isReservedByMe: true, reservedBy: reserver }, noPriceItem,
      ],
      'get /messages/unread/count': 0,
    })
    mount(<Routes><Route path="/profile/:username" element={<ProfilePage />} /></Routes>, '/profile/owner')
    expect((await screen.findByText('Zero gift')).closest('a')?.getAttribute('href')).toBe(item.url)
    expect(screen.getByText('0 RUB').textContent).toBe('0 RUB')
    expect(screen.getByText('Gift without price').textContent).toBe(noPriceItem.name)
    expect(screen.getByText('Цена не указана').textContent).toBe('Цена не указана')
    expect(screen.getByRole('img', { name: 'Zero gift' }).getAttribute('src')).toBe(item.imageUrl)
    expect(screen.queryByText(/Зарезервировано/)).toBeNull()
    expect(screen.queryByText('Secret reserver')).toBeNull()
    expect(screen.queryByRole('button', { name: /резервирова/ })).toBeNull()
    expect(adapter.mock.calls.map(([config]) => config.url).sort()).toEqual(['/messages/unread/count', '/users/owner', '/wishlist/user/owner'])
  })

  it('renders flat feed cards with zero and absent prices without requesting metadata', async () => {
    const adapter = mockApi({
      'get /friends': [{ id: 'friendship', user: owner, status: 'accepted', createdAt: item.createdAt }],
      'get /wishlist/user/owner': [item, noPriceItem],
      'get /messages/unread/count': 0,
    })
    mount(<FeedPage />)
    expect((await screen.findByText('Zero gift')).closest('a')?.getAttribute('href')).toBe('/wishlist/owner')
    expect(screen.getByText('0 RUB').textContent).toBe('0 RUB')
    expect(screen.getByText('Gift without price').textContent).toBe(noPriceItem.name)
    expect(screen.getByText('Цена не указана').textContent).toBe('Цена не указана')
    expect(screen.getByRole('img', { name: 'Zero gift' }).getAttribute('src')).toBe(item.imageUrl)
    expect(adapter.mock.calls.map(([config]) => config.url).sort()).toEqual(['/friends', '/messages/unread/count', '/wishlist/user/owner'])
  })
})

it.each([
  { page: 'wishlist', path: '/wishlist', element: <WishlistPage /> },
  { page: 'profile', path: '/profile/owner', element: <ProfilePage /> },
  { page: 'feed', path: '/', element: <FeedPage /> },
])('shows the unknown shop domain instead of an internal source on the $page page', async ({ path, element }) => {
  const manualItem = { ...item, name: 'Handmade gift', source: 'Unknown', url: 'https://Small-Shop.example/gift?variant=green' }
  const adapter = mockApi({
    'get /users/owner': { ...owner, createdAt: item.createdAt, wishlistPrivacy: 'public', friendshipStatus: null, wishlistItemCount: 1 },
    'get /friends': [{ id: 'friendship', user: owner, status: 'accepted', createdAt: item.createdAt }],
    'get /wishlist': [manualItem],
    'get /wishlist/user/owner': [manualItem],
    'get /messages/unread/count': 0,
  })
  mount(<Routes><Route path={path === '/profile/owner' ? '/profile/:username' : path} element={element} /></Routes>, path)
  expect((await screen.findByText('small-shop.example')).textContent).toBe('small-shop.example')
  expect(screen.queryByText('Unknown')).toBeNull()
  expect(screen.getByText('Handmade gift').closest('a')?.getAttribute('href')).toBe(path === '/' ? '/wishlist/owner' : 'https://Small-Shop.example/gift?variant=green')
  expect(adapter.mock.calls.some(([config]) => config.url?.includes('metadata'))).toBe(false)
})

it('formats known marketplaces and falls back safely for invalid unknown URLs', () => {
  expect(formatItemSource('Ozon', 'not a URL')).toBe('Ozon')
  expect(formatItemSource('Wildberries', 'not a URL')).toBe('Wildberries')
  expect(formatItemSource('YandexMarket', 'https://market.yandex.ru/product/1')).toBe('Яндекс Маркет')
  expect(formatItemSource('Unknown', 'not a URL')).toBe('Другой магазин')
  expect(formatItemSource('Unknown', 'javascript:alert(1)')).toBe('Другой магазин')
  expect(formatItemSource('Unknown', 'https://user:password@Small-Shop.example:8443/gift?variant=green')).toBe('small-shop.example')
})
