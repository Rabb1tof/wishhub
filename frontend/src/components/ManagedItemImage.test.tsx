import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AxiosError } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import api from '@/api/client'
import * as imageApi from '@/api/wishlistImages'
import { useAuthStore } from '@/store/auth'
import { isManagedImageUrl, isWishlistImageUrl } from '@/utils/wishlistImages'
import { ItemImage } from './ItemImage'

const ownerId = '11111111-2222-3333-4444-555555555555'
const imageId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
const path = `/api/wishlist-images/${ownerId}/${imageId}.jpg`
const nextPath = `/api/wishlist-images/${ownerId}/${imageId}.webp`
const originalAdapter = api.defaults.adapter
const BrowserURL = URL
const imageBlob = new Blob(['image bytes'], { type: 'image/jpeg' })
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>
let createObjectURL: ReturnType<typeof vi.fn<(blob: Blob | MediaSource) => string>>
let revokeObjectURL: ReturnType<typeof vi.fn<(url: string) => void>>

function response(config: InternalAxiosRequestConfig, data: unknown, status = 200) {
  return { config, data, status, statusText: String(status), headers: {} }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

beforeEach(() => {
  adapter = vi.fn<AxiosAdapter>(async (config) => response(config, imageBlob))
  api.defaults.adapter = adapter
  useAuthStore.setState({ user: { id: ownerId, username: 'owner', displayName: 'Owner' }, accessToken: 'image-token', refreshToken: null })
  let objectId = 0
  createObjectURL = vi.fn<(blob: Blob | MediaSource) => string>(() => `blob:managed-${++objectId}`)
  revokeObjectURL = vi.fn<(url: string) => void>()
  vi.stubGlobal('URL', class extends BrowserURL {
    static createObjectURL = createObjectURL
    static revokeObjectURL = revokeObjectURL
  })
})
afterEach(() => {
  cleanup()
  api.defaults.adapter = originalAdapter
  useAuthStore.getState().clearAuth()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('managed image URL validation', () => {
  it.each(['jpg', 'png', 'webp'])('accepts the canonical managed %s path', (extension) => {
    const value = `/api/wishlist-images/${ownerId}/${imageId}.${extension}`
    expect(isManagedImageUrl(value)).toBe(true)
    expect(isWishlistImageUrl(value)).toBe(true)
  })

  it.each([
    { label: 'external absolute URL', value: `https://evil.example${path}` },
    { label: 'protocol relative URL', value: `//evil.example${path}` },
    { label: 'query string', value: `${path}?redirect=https://evil.example` },
    { label: 'fragment', value: `${path}#photo` },
    { label: 'traversal', value: `/api/wishlist-images/${ownerId}/../${imageId}.jpg` },
    { label: 'encoded traversal', value: `/api/wishlist-images/${ownerId}/%2e%2e/${imageId}.jpg` },
    { label: 'encoded separator', value: path.replace('/aaaaaaaa', '%2faaaaaaaa') },
    { label: 'encoded filename', value: path.replace('aaaaaaaa', '%61aaaaaaa') },
    { label: 'backslash separator', value: path.replace('/aaaaaaaa', '\\aaaaaaaa') },
    { label: 'duplicate separator', value: path.replace('/aaaaaaaa', '//aaaaaaaa') },
    { label: 'prefix whitespace', value: ` ${path}` },
    { label: 'suffix newline', value: `${path}\n` },
    { label: 'suffix whitespace', value: `${path} ` },
    { label: 'uppercase owner guid', value: path.replace(ownerId, 'aaaa1111-2222-3333-4444-555555555555'.toUpperCase()) },
    { label: 'uppercase image guid', value: path.replace(imageId, imageId.toUpperCase()) },
    { label: 'empty owner guid', value: path.replace(ownerId, '00000000-0000-0000-0000-000000000000') },
    { label: 'empty image guid', value: path.replace(imageId, '00000000-0000-0000-0000-000000000000') },
    { label: 'compact guid', value: path.replaceAll('-', '') },
    { label: 'non hexadecimal guid', value: path.replace('aaaaaaaa', 'zzzzzzzz') },
    { label: 'svg extension', value: path.replace('.jpg', '.svg') },
    { label: 'double extension', value: `${path}.svg` },
    { label: 'uppercase extension', value: path.replace('.jpg', '.JPG') },
    { label: 'unrelated API path', value: '/api/users/me' },
    { label: 'blob URL', value: 'blob:https://example.test/id' },
  ])('rejects $label as a managed path', ({ value }) => {
    expect(isManagedImageUrl(value)).toBe(false)
  })

  it('continues accepting external HTTP images but rejects other relative and non HTTP sources', () => {
    expect(isWishlistImageUrl('https://images.example/picture.png?variant=blue')).toBe(true)
    expect(isWishlistImageUrl('http://images.example/picture.png')).toBe(true)
    expect(isWishlistImageUrl('/avatars/photo.png')).toBe(false)
    expect(isWishlistImageUrl('data:image/png;base64,AAAA')).toBe(false)
    expect(isWishlistImageUrl('javascript:alert(1)')).toBe(false)
  })
})

describe('managed image API', () => {
  it('uploads the file as multipart with authorization and the supplied abort signal', async () => {
    adapter.mockImplementation(async (config) => response(config, { imageUrl: path }))
    const file = new File(['photo'], 'gift.jpg', { type: 'image/jpeg' })
    const controller = new AbortController()
    await expect(imageApi.uploadWishlistImage(file, controller.signal)).resolves.toBe(path)
    expect(adapter).toHaveBeenCalledTimes(1)
    const config = adapter.mock.calls[0][0]
    expect(config).toMatchObject({ method: 'post', baseURL: '/api', url: '/wishlist-images' })
    expect(config.headers.get('Authorization')).toBe('Bearer image-token')
    expect(config.signal).toBe(controller.signal)
    expect(config.data).toBeInstanceOf(FormData)
    expect((config.data as FormData).get('file')).toBe(file)
    expect(Array.from((config.data as FormData).keys())).toEqual(['file'])
  })

  it.each([
    { label: 'external URL', data: { imageUrl: `https://evil.example${path}` } },
    { label: 'traversal path', data: { imageUrl: `/api/wishlist-images/${ownerId}/../image.jpg` } },
    { label: 'missing path', data: {} },
    { label: 'null response', data: null },
    { label: 'non string path', data: { imageUrl: 42 } },
  ])('rejects an upload response containing $label', async ({ data }) => {
    adapter.mockImplementation(async (config) => response(config, data))
    await expect(imageApi.uploadWishlistImage(new File(['photo'], 'gift.jpg'), new AbortController().signal)).rejects.toThrow('Сервер вернул некорректную ссылку на фото.')
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it('downloads a Blob through the authenticated API without duplicating the API prefix', async () => {
    const controller = new AbortController()
    await expect(imageApi.getWishlistImage(path, controller.signal)).resolves.toBe(imageBlob)
    const config = adapter.mock.calls[0][0]
    expect(config).toMatchObject({ method: 'get', baseURL: '/api', url: path.slice(4), responseType: 'blob' })
    expect(config.headers.get('Authorization')).toBe('Bearer image-token')
    expect(config.signal).toBe(controller.signal)
  })

  it.each([
    { label: 'external image', value: 'https://images.example/image.jpg' },
    { label: 'protocol relative image', value: '//images.example/image.jpg' },
    { label: 'unrelated local API', value: '/api/users/me' },
    { label: 'managed path with query', value: `${path}?other=1` },
  ])('never sends authenticated requests for $label', async ({ value }) => {
    await expect(imageApi.getWishlistImage(value, new AbortController().signal)).rejects.toThrow('Некорректная ссылка на фото.')
    expect(adapter).not.toHaveBeenCalled()
  })
})

describe('managed ItemImage', () => {
  it('shows a placeholder while loading and displays an authenticated Blob with image attributes', async () => {
    const pending = deferred<Blob>()
    adapter.mockImplementation(async (config) => response(config, await pending.promise))
    render(<ItemImage imageUrl={path} name="Managed gift" />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Загрузка фото…').textContent).toBe('Загрузка фото…')
    await act(async () => pending.resolve(imageBlob))
    const image = await screen.findByRole('img', { name: 'Managed gift' })
    expect(image.getAttribute('src')).toBe('blob:managed-1')
    expect(image.getAttribute('loading')).toBe('lazy')
    expect(image.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(image.className).toBe('wh-item-image')
    expect(createObjectURL).toHaveBeenCalledExactlyOnceWith(imageBlob)
    expect(adapter.mock.calls[0][0].headers.get('Authorization')).toBe('Bearer image-token')
  })

  it('revokes the previous Blob immediately on replacement and never shows it while the next image loads', async () => {
    const pending = deferred<Blob>()
    adapter.mockImplementation(async (config) => response(config, config.url === nextPath.slice(4) ? await pending.promise : imageBlob))
    const { rerender, unmount } = render(<ItemImage imageUrl={path} name="Gift A" />)
    await screen.findByRole('img', { name: 'Gift A' })
    const oldSignal = adapter.mock.calls[0][0].signal
    rerender(<ItemImage imageUrl={nextPath} name="Gift B" />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(oldSignal?.aborted).toBe(true)
    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:managed-1')
    await act(async () => pending.resolve(imageBlob))
    expect((await screen.findByRole('img', { name: 'Gift B' })).getAttribute('src')).toBe('blob:managed-2')
    unmount()
    expect(revokeObjectURL.mock.calls).toEqual([['blob:managed-1'], ['blob:managed-2']])
    expect(adapter.mock.calls[1][0].signal?.aborted).toBe(true)
  })

  it('ignores a late Blob even when the old download ignores cancellation', async () => {
    const old = deferred<Blob>(), current = deferred<Blob>()
    const download = vi.spyOn(imageApi, 'getWishlistImage').mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    const { rerender } = render(<ItemImage imageUrl={path} name="Gift A" />)
    rerender(<ItemImage imageUrl={nextPath} name="Gift B" />)
    expect(download.mock.calls[0][1].aborted).toBe(true)
    await act(async () => old.resolve(imageBlob))
    expect(createObjectURL).not.toHaveBeenCalled()
    expect(screen.queryByRole('img')).toBeNull()
    await act(async () => current.resolve(imageBlob))
    expect((await screen.findByRole('img', { name: 'Gift B' })).getAttribute('src')).toBe('blob:managed-1')
    expect(createObjectURL).toHaveBeenCalledTimes(1)
  })

  it('aborts an unfinished download on unmount without allocating a stale object URL', async () => {
    const pending = deferred<Blob>()
    const download = vi.spyOn(imageApi, 'getWishlistImage').mockReturnValue(pending.promise)
    const { unmount } = render(<ItemImage imageUrl={path} name="Gift" />)
    unmount()
    expect(download.mock.calls[0][1].aborted).toBe(true)
    await act(async () => pending.resolve(imageBlob))
    expect(createObjectURL).not.toHaveBeenCalled()
    expect(revokeObjectURL).not.toHaveBeenCalled()
  })

  it('revokes a private image on logout, handles anonymous not found without login, and reloads after login', async () => {
    const anonymous = deferred<void>()
    adapter.mockImplementation(async (config) => {
      if (!config.headers.get('Authorization')) {
        await anonymous.promise
        throw new AxiosError('Not found', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 404))
      }
      return response(config, imageBlob)
    })
    const { unmount } = render(<ItemImage imageUrl={path} name="Gift" />)
    await screen.findByRole('img', { name: 'Gift' })
    act(() => useAuthStore.getState().clearAuth())
    expect(screen.queryByRole('img')).toBeNull()
    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:managed-1')
    await waitFor(() => expect(adapter).toHaveBeenCalledTimes(2))
    expect(adapter.mock.calls[0][0].signal?.aborted).toBe(true)
    expect(adapter.mock.calls[1][0].headers.get('Authorization')).toBeUndefined()
    await act(async () => anonymous.resolve())
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения')).toBeTruthy()
    expect(adapter.mock.calls.map(([config]) => config.url)).toEqual([path.slice(4), path.slice(4)])
    expect(useAuthStore.getState().accessToken).toBeNull()
    act(() => useAuthStore.setState({ user: { id: 'another-user', username: 'other', displayName: 'Other' }, accessToken: 'next-token' }))
    expect(screen.queryByRole('img')).toBeNull()
    expect((await screen.findByRole('img', { name: 'Gift' })).getAttribute('src')).toBe('blob:managed-2')
    expect(adapter.mock.calls[2][0].headers.get('Authorization')).toBe('Bearer next-token')
    unmount()
    expect(revokeObjectURL.mock.calls).toEqual([['blob:managed-1'], ['blob:managed-2']])
  })

  it('resets the image when account identity changes even if the token value is unchanged', async () => {
    render(<ItemImage imageUrl={path} name="Gift" />)
    await screen.findByRole('img', { name: 'Gift' })
    act(() => useAuthStore.setState({ user: { id: 'another-user', username: 'other', displayName: 'Other' } }))
    expect(screen.queryByRole('img')).toBeNull()
    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:managed-1')
    expect((await screen.findByRole('img', { name: 'Gift' })).getAttribute('src')).toBe('blob:managed-2')
    expect(adapter).toHaveBeenCalledTimes(2)
  })

  it('renders a placeholder after access denial without retrying or allocating an object URL', async () => {
    adapter.mockImplementation(async (config) => { throw new AxiosError('Forbidden', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 403)) })
    render(<ItemImage imageUrl={path} name="Private gift" />)
    await waitFor(() => expect(adapter).toHaveBeenCalledTimes(1))
    await act(async () => {})
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения')).toBeTruthy()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('replaces a broken decoded Blob with the placeholder and revokes it on unmount', async () => {
    const { unmount } = render(<ItemImage imageUrl={path} name="Gift" />)
    fireEvent.error(await screen.findByRole('img', { name: 'Gift' }))
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения')).toBeTruthy()
    unmount()
    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:managed-1')
  })

  it('loads a public managed image anonymously without a bearer token', async () => {
    useAuthStore.getState().clearAuth()
    render(<ItemImage imageUrl={path} name="Public gift" />)
    expect((await screen.findByRole('img', { name: 'Public gift' })).getAttribute('src')).toBe('blob:managed-1')
    expect(adapter).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'get', url: path.slice(4) })
    expect(adapter.mock.calls[0][0].headers.get('Authorization')).toBeUndefined()
  })

  it('keeps external images direct and never sends the bearer token through the API to them', () => {
    render(<ItemImage imageUrl="https://images.example/external.jpg" name="External gift" />)
    expect(screen.getByRole('img', { name: 'External gift' }).getAttribute('src')).toBe('https://images.example/external.jpg')
    expect(adapter).not.toHaveBeenCalled()
    expect(createObjectURL).not.toHaveBeenCalled()
  })
})



function installIntersectionObserver() {
  const observers: ObservedImage[] = []
  class ObservedImage {
    readonly callback: IntersectionObserverCallback
    readonly options: IntersectionObserverInit | undefined
    observe = vi.fn<(target: Element) => void>()
    disconnect = vi.fn()
    constructor(callback: IntersectionObserverCallback, options?: IntersectionObserverInit) {
      this.callback = callback
      this.options = options
      observers.push(this)
    }
    emit(isIntersecting: boolean) {
      this.callback([{ isIntersecting } as IntersectionObserverEntry], this as unknown as IntersectionObserver)
    }
  }
  vi.stubGlobal('IntersectionObserver', ObservedImage)
  return observers
}

describe('managed image lazy loading', () => {
  it('downloads only after the stable wrapper intersects the 200px margin and starts once', async () => {
    const observers = installIntersectionObserver()
    render(<ItemImage imageUrl={path} name="Lazy gift" />)
    const observer = observers[0]
    expect(observer.options).toEqual({ rootMargin: '200px' })
    const wrapper = observer.observe.mock.calls[0][0]
    expect(wrapper.contains(screen.getByText('Загрузка фото…'))).toBe(true)
    await act(async () => {})
    expect(adapter).not.toHaveBeenCalled()
    act(() => observer.emit(false))
    expect(adapter).not.toHaveBeenCalled()
    act(() => observer.emit(true))
    const image = await screen.findByRole('img', { name: 'Lazy gift' })
    expect(wrapper.contains(image)).toBe(true)
    expect(image.getAttribute('src')).toBe('blob:managed-1')
    expect(observer.disconnect).toHaveBeenCalledTimes(1)
    act(() => observer.emit(true))
    await act(async () => {})
    expect(adapter).toHaveBeenCalledTimes(1)
  })

  it('disconnects an offscreen image on unmount and ignores queued visibility callbacks', async () => {
    const observers = installIntersectionObserver()
    const { unmount } = render(<ItemImage imageUrl={path} name="Offscreen gift" />)
    unmount()
    expect(observers[0].disconnect).toHaveBeenCalledTimes(1)
    act(() => observers[0].emit(true))
    await act(async () => {})
    expect(adapter).not.toHaveBeenCalled()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('ignores visibility for the previous URL after replacement and loads only the current image', async () => {
    const observers = installIntersectionObserver()
    const { rerender } = render(<ItemImage imageUrl={path} name="Gift A" />)
    rerender(<ItemImage imageUrl={nextPath} name="Gift B" />)
    expect(observers[0].disconnect).toHaveBeenCalledTimes(1)
    act(() => observers[0].emit(true))
    await act(async () => {})
    expect(adapter).not.toHaveBeenCalled()
    act(() => observers[1].emit(true))
    expect((await screen.findByRole('img', { name: 'Gift B' })).getAttribute('src')).toBe('blob:managed-1')
    expect(adapter.mock.calls.map(([config]) => config.url)).toEqual([nextPath.slice(4)])
  })

  it('falls back to immediate download when IntersectionObserver is unavailable', async () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    render(<ItemImage imageUrl={path} name="Fallback gift" />)
    expect((await screen.findByRole('img', { name: 'Fallback gift' })).getAttribute('src')).toBe('blob:managed-1')
    expect(adapter).toHaveBeenCalledTimes(1)
  })
})

describe('managed route isolation from external image URLs', () => {
  it.each([
    { label: 'literal reserved path', value: `https://images.example${path}` },
    { label: 'encoded route letters', value: `https://images.example/%61pi/%77ishlist-images/${ownerId}/${imageId}.jpg` },
    { label: 'encoded route separators', value: `https://images.example/api%2fwishlist-images%2f${ownerId}/${imageId}.jpg` },
    { label: 'uppercase reserved route', value: `https://images.example/API/WISHLIST-IMAGES/${ownerId}/${imageId}.jpg` },
    { label: 'encoded uppercase route', value: `https://images.example/%41PI/WISHLIST-IMAGES/${ownerId}/${imageId}.jpg` },
  ])('rejects $label and does not render or download it', ({ value }) => {
    expect(isWishlistImageUrl(value)).toBe(false)
    render(<ItemImage imageUrl={value} name="Disallowed image" />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Нет изображения')).toBeTruthy()
    expect(adapter).not.toHaveBeenCalled()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('allows an ordinary external image whose query contains the reserved route', () => {
    const value = `https://images.example/photo.jpg?caption=${encodeURIComponent(path)}`
    expect(isWishlistImageUrl(value)).toBe(true)
    render(<ItemImage imageUrl={value} name="Ordinary image" />)
    expect(screen.getByRole('img', { name: 'Ordinary image' }).getAttribute('src')).toBe(value)
    expect(adapter).not.toHaveBeenCalled()
    expect(createObjectURL).not.toHaveBeenCalled()
  })
})
