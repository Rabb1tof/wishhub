import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import api from './client'
import { resolveMetadata } from './metadata'
import { useAddItem, useUpdateItem, useWishlist } from './useWishlist'
import type { WishlistItem } from '@/types/wishlist'

const item: WishlistItem = {
  id: 'item-1', url: 'https://shop.example/item?sku=blue', name: 'My gift',
  imageUrl: null, price: null, currency: 'RUB', source: 'shop.example',
  sortOrder: 4, createdAt: '2026-10-30T00:00:00Z',
  isReserved: false, isReservedByMe: false, reservedBy: null,
}
const originalAdapter = api.defaults.adapter
let client: QueryClient
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

function response(config: InternalAxiosRequestConfig, data: unknown, status = 200) {
  return { config, data, status, statusText: String(status), headers: {} }
}
function Wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } })
  adapter = vi.fn<AxiosAdapter>(async (config) => response(config, [item]))
  api.defaults.adapter = adapter
})
afterEach(() => {
  cleanup()
  client.clear()
  api.defaults.adapter = originalAdapter
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('wishlist API', () => {
  it('loads flat items without metadata or periodic polling', async () => {
    vi.useFakeTimers()
    const { result } = renderHook(() => useWishlist(), { wrapper: Wrapper })
    await act(async () => { await vi.advanceTimersByTimeAsync(10) })
    expect(result.current.data).toEqual({ items: [item], hidden: false })
    await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'get', url: '/wishlist' })
  })

  it('waits for a profile id without requesting the current users wishlist', async () => {
    const { result, rerender } = renderHook(({ userId, enabled }: { userId?: string; enabled: boolean }) => useWishlist(userId, enabled), {
      wrapper: Wrapper, initialProps: { userId: undefined as string | undefined, enabled: false },
    })
    await act(async () => {})
    expect(adapter).not.toHaveBeenCalled()
    rerender({ userId: 'friend-1', enabled: true })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0].url).toBe('/wishlist/user/friend-1')
  })

  it('maps a private wishlist response to the hidden state', async () => {
    adapter.mockImplementation(async (config) => { throw new AxiosError('Private', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 403)) })
    const { result } = renderHook(() => useWishlist('friend-1'), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data).toEqual({ items: [], hidden: true })
  })

  it('keeps server failures as errors instead of showing an empty private wishlist', async () => {
    adapter.mockImplementation(async (config) => { throw new AxiosError('Server failed', 'ERR_BAD_RESPONSE', config, undefined, response(config, {}, 500)) })
    const { result } = renderHook(() => useWishlist(), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(result.current.data).toBeUndefined()
  })

  it('posts only url and name and invalidates all wishlist views', async () => {
    adapter.mockImplementation(async (config) => response(config, item))
    client.setQueryData(['wishlist', 'me'], [item])
    client.setQueryData(['wishlist', 'owner-1'], [item])
    client.setQueryData(['profile', 'owner'], { id: 'owner-1' })
    const { result } = renderHook(() => useAddItem(), { wrapper: Wrapper })
    const data = { url: item.url, name: item.name }
    let saved: WishlistItem | undefined
    await act(async () => { saved = await result.current.mutateAsync(data) })
    expect(saved).toEqual(item)
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'post', url: '/wishlist' })
    expect(JSON.parse(adapter.mock.calls[0][0].data)).toEqual(data)
    expect(client.getQueryState(['wishlist', 'me'])?.isInvalidated).toBe(true)
    expect(client.getQueryState(['wishlist', 'owner-1'])?.isInvalidated).toBe(true)
    expect(client.getQueryState(['profile', 'owner'])?.isInvalidated).toBe(false)
  })

  it('puts all editable fields preserving zero and explicit null without adding sort order', async () => {
    const data = { url: 'https://shop.example/new?variant=small', name: 'New name', imageUrl: null, price: 0, currency: 'RUB' }
    const updated = { ...item, ...data }
    adapter.mockImplementation(async (config) => response(config, updated))
    client.setQueryData(['wishlist', 'me'], [item])
    const { result } = renderHook(() => useUpdateItem(), { wrapper: Wrapper })
    await act(async () => { expect(await result.current.mutateAsync({ id: item.id, data })).toEqual(updated) })
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'put', url: '/wishlist/item-1' })
    expect(JSON.parse(adapter.mock.calls[0][0].data)).toEqual(data)
    expect(client.getQueryState(['wishlist', 'me'])?.isInvalidated).toBe(true)
  })

  it('does not invalidate saved items after a conflicting update', async () => {
    adapter.mockImplementation(async (config) => { throw new AxiosError('Duplicate', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 409)) })
    client.setQueryData(['wishlist', 'me'], [item])
    const { result } = renderHook(() => useUpdateItem(), { wrapper: Wrapper })
    await act(async () => {
      await expect(result.current.mutateAsync({ id: item.id, data: { url: item.url, name: 'Rejected', imageUrl: null, price: null } })).rejects.toMatchObject({ response: { status: 409 } })
    })
    expect(client.getQueryData(['wishlist', 'me'])).toEqual([item])
    expect(client.getQueryState(['wishlist', 'me'])?.isInvalidated).toBe(false)
  })

  it('resolves metadata through the typed endpoint with the caller abort signal', async () => {
    const metadata = { status: 'partial', name: 'Found', imageUrl: null, source: 'shop.example', reason: null }
    adapter.mockImplementation(async (config) => response(config, metadata))
    const controller = new AbortController()
    await expect(resolveMetadata(item.url, controller.signal)).resolves.toEqual(metadata)
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'post', url: '/metadata/resolve' })
    expect(adapter.mock.calls[0][0].signal).toBe(controller.signal)
    expect(JSON.parse(adapter.mock.calls[0][0].data)).toEqual({ url: item.url })
  })
})
