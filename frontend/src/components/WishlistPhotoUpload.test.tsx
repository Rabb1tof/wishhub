import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { AxiosAdapter } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import api from '@/api/client'
import * as images from '@/api/wishlistImages'
import * as metadata from '@/api/metadata'
import { WishlistItemModal } from './WishlistItemModal'

const managed = '/api/wishlist-images/12345678-1234-1234-1234-123456789abc/abcdef12-1234-5678-9012-abcdef123456.jpg'
const originalAdapter = api.defaults.adapter
const originalCreate = URL.createObjectURL
const originalRevoke = URL.revokeObjectURL
let client: QueryClient
const adapter = vi.fn<AxiosAdapter>()

function pending<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}
function open() {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const close = vi.fn()
  const view = render(<QueryClientProvider client={client}><WishlistItemModal onClose={close} /></QueryClientProvider>)
  fireEvent.change(screen.getByLabelText('Ссылка на товар'), { target: { value: 'https://shop.example/wish?variant=red' } })
  fireEvent.change(screen.getByLabelText('Название'), { target: { value: 'My wish' } })
  return { ...view, close }
}
function choose(file: File) { fireEvent.change(screen.getByLabelText('Фото из файла'), { target: { files: [file] } }) }
function saveButton() { return screen.getByRole('button', { name: 'Сохранить' }) as HTMLButtonElement }
function file(name = 'photo.jpg', type = 'image/jpeg', size = 10) { return new File([new Uint8Array(size)], name, { type }) }
function imageValue() { return (screen.getByLabelText('Фото: URL (необязательно)') as HTMLInputElement).value }

beforeEach(() => {
  adapter.mockReset().mockImplementation(async (config) => ({ config, data: {}, status: 200, statusText: 'OK', headers: {} }))
  api.defaults.adapter = adapter
  let serial = 0
  URL.createObjectURL = vi.fn(() => `blob:preview-${++serial}`)
  URL.revokeObjectURL = vi.fn()
})
afterEach(() => {
  cleanup(); client?.clear(); api.defaults.adapter = originalAdapter
  URL.createObjectURL = originalCreate; URL.revokeObjectURL = originalRevoke
  vi.restoreAllMocks()
})

describe('Wishlist photo upload', () => {
  it('previews the chosen file immediately and blocks saving until its managed URL arrives', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    const { close } = open()
    const picture = file()
    choose(picture)
    expect(screen.getByRole('img', { name: 'Превью выбранного фото' }).getAttribute('src')).toBe('blob:preview-1')
    expect(upload.mock.calls[0][0]).toBe(picture)
    expect(saveButton().disabled).toBe(true)
    fireEvent.click(saveButton())
    expect(adapter).not.toHaveBeenCalled()
    await act(async () => request.resolve(managed))
    expect(imageValue()).toBe('')
    expect(screen.queryByDisplayValue(managed)).toBeNull()
    expect(screen.getByText('Используется загруженное фото; ссылка заменит его.').textContent).toContain('ссылка заменит его')
    expect(saveButton().disabled).toBe(false)
    fireEvent.click(saveButton())
    await waitFor(() => expect(close).toHaveBeenCalledOnce())
    expect(JSON.parse(adapter.mock.calls[0][0].data)).toEqual({ url: 'https://shop.example/wish?variant=red', name: 'My wish', imageUrl: managed, price: null, currency: 'RUB' })
  })

  it.each([
    { label: 'GIF', name: 'photo.gif', type: 'image/gif', size: 10, error: 'Поддерживаются только JPEG, PNG и WebP.' },
    { label: 'SVG', name: 'photo.svg', type: 'image/svg+xml', size: 10, error: 'Поддерживаются только JPEG, PNG и WebP.' },
    { label: 'empty file', name: 'photo.png', type: 'image/png', size: 0, error: 'Файл пуст. Выберите другое фото.' },
    { label: 'over five MiB', name: 'photo.webp', type: 'image/webp', size: 5 * 1024 * 1024 + 1, error: 'Фото больше 5 МБ. Выберите файл поменьше.' },
  ])('rejects $label without uploading or silently saving without the selected photo', ({ name, type, size, error }) => {
    const upload = vi.spyOn(images, 'uploadWishlistImage')
    open(); choose(file(name, type, size))
    expect(screen.getByRole('alert').textContent).toBe(error)
    expect(upload).not.toHaveBeenCalled()
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    expect(saveButton().disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Удалить фото' }))
    expect(saveButton().disabled).toBe(false)
    expect(imageValue()).toBe('')
  })

  it.each([
    { label: 'PNG at the exact size limit', name: 'photo.png', type: 'image/png', size: 5 * 1024 * 1024 },
    { label: 'WebP', name: 'photo.webp', type: 'image/webp', size: 10 },
  ])('accepts $label from the file picker', async ({ name, type, size }) => {
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockResolvedValue(managed)
    open(); const picture = file(name, type, size); choose(picture)
    await waitFor(() => expect(saveButton().disabled).toBe(false))
    expect(imageValue()).toBe('')
    expect(upload.mock.calls[0][0]).toBe(picture)
    expect(saveButton().disabled).toBe(false)
  })

  it('accepts a dropped photo and rejects dropping several files', async () => {
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockResolvedValue(managed)
    const { container } = open()
    const drop = container.querySelector('.wh-image-drop')!
    const picture = file()
    fireEvent.drop(drop, { dataTransfer: { files: [picture] } })
    await waitFor(() => expect(saveButton().disabled).toBe(false))
    expect(imageValue()).toBe('')
    expect(upload).toHaveBeenCalledOnce()
    fireEvent.drop(drop, { dataTransfer: { files: [picture, file('second.png', 'image/png')] } })
    expect(screen.getByRole('alert').textContent).toBe('Выберите одно фото.')
    expect(saveButton().disabled).toBe(true)
    expect(upload).toHaveBeenCalledOnce()
  })

  it('keeps a failed upload selected and permits an explicit retry', async () => {
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(managed)
    open(); const picture = file(); choose(picture)
    expect((await screen.findByRole('alert')).textContent).toContain('Не удалось загрузить фото')
    expect(saveButton().disabled).toBe(true)
    expect(screen.getByRole('img', { name: 'Превью выбранного фото' }).getAttribute('src')).toBe('blob:preview-1')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    await waitFor(() => expect(saveButton().disabled).toBe(false))
    expect(imageValue()).toBe('')
    expect(upload).toHaveBeenCalledTimes(2)
    expect(upload.mock.calls[1][0]).toBe(picture)
    expect(saveButton().disabled).toBe(false)
  })

  it('cancels the upload without discarding the chosen photo and retries with a new signal', async () => {
    const first = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValueOnce(first.promise).mockResolvedValueOnce(managed)
    open(); choose(file())
    fireEvent.click(screen.getByRole('button', { name: 'Отменить загрузку' }))
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    expect(saveButton().disabled).toBe(true)
    await act(async () => first.resolve('/api/wishlist-images/12345678-1234-1234-1234-123456789abc/11111111-1111-1111-1111-111111111111.png'))
    expect(imageValue()).toBe('')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    await waitFor(() => expect(saveButton().disabled).toBe(false))
    expect(imageValue()).toBe('')
    expect(upload.mock.calls[1][1]).not.toBe(upload.mock.calls[0][1])
    expect(saveButton().disabled).toBe(false)
  })

  it('aborts and revokes a replaced file and ignores its late upload response', async () => {
    const first = pending<string>(), second = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    open(); choose(file()); choose(file('second.png', 'image/png'))
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    await act(async () => first.resolve(managed))
    expect(imageValue()).toBe('')
    expect(saveButton().disabled).toBe(true)
    expect(screen.getByRole('img', { name: 'Превью выбранного фото' }).getAttribute('src')).toBe('blob:preview-2')
    await act(async () => second.resolve(managed))
    expect(imageValue()).toBe('')
  })

  it('clears a pending file explicitly and saves null even after its late response', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    const { close } = open(); choose(file())
    fireEvent.click(screen.getByRole('button', { name: 'Удалить фото' }))
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    await act(async () => request.resolve(managed))
    expect(imageValue()).toBe('')
    expect(screen.queryByDisplayValue(managed)).toBeNull()
    expect(screen.queryByText('Используется загруженное фото; ссылка заменит его.')).toBeNull()
    expect(saveButton().disabled).toBe(false)
    fireEvent.click(saveButton())
    await waitFor(() => expect(close).toHaveBeenCalledOnce())
    expect(JSON.parse(adapter.mock.calls[0][0].data).imageUrl).toBeNull()
  })

  it('replaces a pending file with a manually entered URL without overwriting it later', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    open(); choose(file())
    fireEvent.change(screen.getByLabelText('Фото: URL (необязательно)'), { target: { value: 'https://images.example/manual.png' } })
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    await act(async () => request.resolve(managed))
    expect(imageValue()).toBe('https://images.example/manual.png')
    expect(saveButton().disabled).toBe(false)
  })

  it('preserves a chosen photo against metadata and only replaces it on explicit apply', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue({ status: 'ok', name: 'Found wish', imageUrl: 'https://images.example/found.png', source: 'Unknown', reason: null })
    open(); choose(file()); fireEvent.click(screen.getByRole('button', { name: 'Получить данные' }))
    await screen.findByRole('button', { name: 'Применить найденное' })
    expect(imageValue()).toBe('')
    expect(saveButton().disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Применить найденное' }))
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    await act(async () => request.resolve(managed))
    expect(imageValue()).toBe('https://images.example/found.png')
    expect(saveButton().disabled).toBe(false)
  })

  it('keeps the manually selected file when the product URL changes', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    open(); choose(file())
    fireEvent.change(screen.getByLabelText('Ссылка на товар'), { target: { value: 'https://other.example/wish' } })
    expect(upload.mock.calls[0][1].aborted).toBe(false)
    await act(async () => request.resolve(managed))
    expect(imageValue()).toBe('')
  })

  it('aborts and revokes the file preview on unmount without applying its late response', async () => {
    const request = pending<string>()
    const upload = vi.spyOn(images, 'uploadWishlistImage').mockReturnValue(request.promise)
    const { unmount, close } = open(); choose(file()); unmount()
    expect(upload.mock.calls[0][1].aborted).toBe(true)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    await act(async () => request.resolve(managed))
    expect(close).not.toHaveBeenCalled()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('retains keyboard focus inside the modal while saving disables every control', async () => {
    const request = pending<void>()
    adapter.mockImplementation(async (config) => { await request.promise; return { config, data: {}, status: 200, statusText: 'OK', headers: {} } })
    const { close } = open(); fireEvent.click(saveButton())
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('dialog')))
    const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    screen.getByRole('dialog').dispatchEvent(tab)
    expect(tab.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(screen.getByRole('dialog'))
    await act(async () => request.resolve())
    await waitFor(() => expect(close).toHaveBeenCalledOnce())
  })
})
