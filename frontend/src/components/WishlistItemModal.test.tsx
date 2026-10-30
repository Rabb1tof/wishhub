import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import type { AxiosAdapter } from 'axios'
import api from '@/api/client'
import * as metadata from '@/api/metadata'
import { WishlistItemModal } from './WishlistItemModal'
import type { WishlistItem } from '@/types/wishlist'
import type { MetadataResult } from '@/types/metadata'

const item: WishlistItem = {
  id: 'item-1', url: 'https://shop.example/product?sku=42&variant=red', name: 'My gift',
  imageUrl: 'https://images.example/old.jpg', price: 25, currency: 'RUB', source: 'shop.example',
  sortOrder: 7, createdAt: '2026-10-30T12:00:00Z', isReserved: false, isReservedByMe: false, reservedBy: null,
}
const result: MetadataResult = { status: 'ok', name: 'Found gift', imageUrl: 'https://images.example/found.jpg', source: 'shop.example', reason: null }
const originalAdapter = api.defaults.adapter
let client: QueryClient
const adapter = vi.fn<AxiosAdapter>()

function open(existing?: WishlistItem) {
  const onClose = vi.fn()
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return { ...render(<QueryClientProvider client={client}><WishlistItemModal item={existing} onClose={onClose} /></QueryClientProvider>), onClose }
}
function input(label: string) { return screen.getByLabelText(label) as HTMLInputElement }
function change(label: string, value: string) { fireEvent.change(input(label), { target: { value } }) }
function basic() { change('Ссылка на товар', 'https://unknown.example/product?sku=42&variant=red'); change('Название', 'Manual gift') }
function save() { fireEvent.click(screen.getByRole('button', { name: 'Сохранить' })) }
function fetchData() { fireEvent.click(screen.getByRole('button', { name: 'Получить данные' })) }
function deferred() {
  let resolve!: (value: MetadataResult) => void
  let reject!: (error: Error) => void
  const promise = new Promise<MetadataResult>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}
function body(index = 0) { return JSON.parse(adapter.mock.calls[index][0].data as string) as Record<string, unknown> }

beforeEach(() => {
  adapter.mockReset()
  adapter.mockImplementation(async (config) => ({ data: item, status: 200, statusText: 'OK', headers: {}, config }))
  api.defaults.adapter = adapter
})
afterEach(() => { cleanup(); client.clear(); api.defaults.adapter = originalAdapter; vi.restoreAllMocks() })

describe('WishlistItemModal manual saving', () => {
  it('saves URL and name alone with null optionals and default RUB without metadata', async () => {
    const resolve = vi.spyOn(metadata, 'resolveMetadata')
    const { onClose } = open()
    basic()
    change('Ссылка на товар', ' https://unknown.example/product?sku=42&variant=red ')
    change('Название', ' Manual gift ')
    expect(adapter).not.toHaveBeenCalled()
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(adapter).toHaveBeenCalledOnce()
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'post', url: '/wishlist' })
    expect(body()).toEqual({ url: 'https://unknown.example/product?sku=42&variant=red', name: 'Manual gift', imageUrl: null, price: null, currency: 'RUB' })
    expect(resolve).not.toHaveBeenCalled()
  })

  it('edits all fields and sends explicit nulls to clear photo and price while preserving sort order by omission', async () => {
    const { onClose } = open(item)
    expect(input('Название').value).toBe('My gift')
    expect(input('Цена (необязательно)').value).toBe('25')
    fireEvent.click(screen.getByRole('button', { name: 'Удалить фото' }))
    change('Цена (необязательно)', '')
    change('Название', 'Edited gift')
    change('Ссылка на товар', 'https://other.example/item?variant=blue')
    change('Валюта', 'USD')
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: 'put', url: '/wishlist/item-1' })
    expect(body()).toEqual({ url: 'https://other.example/item?variant=blue', name: 'Edited gift', imageUrl: null, price: null, currency: 'USD' })
  })

  it('sends zero as a price and allows replacing the image', async () => {
    const { onClose } = open(item)
    change('Фото: URL (необязательно)', ' https://images.example/replacement.jpg ')
    change('Цена (необязательно)', '0')
    change('Валюта', '')
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(body()).toEqual({ url: item.url, name: 'My gift', imageUrl: 'https://images.example/replacement.jpg', price: 0, currency: 'RUB' })
  })

  it.each([
    { label: 'missing URL', field: 'Ссылка на товар', value: '' },
    { label: 'relative URL', field: 'Ссылка на товар', value: '/product' },
    { label: 'script URL', field: 'Ссылка на товар', value: 'javascript:alert(1)' },
    { label: 'incomplete HTTP URL', field: 'Ссылка на товар', value: 'https:shop.example' },
    { label: 'overlong URL', field: 'Ссылка на товар', value: 'https://shop.example/' + 'a'.repeat(1980) },
    { label: 'blank name', field: 'Название', value: '   ' },
    { label: 'overlong name', field: 'Название', value: 'a'.repeat(501) },
    { label: 'negative price', field: 'Цена (необязательно)', value: '-0.01' },
    { label: 'unsafe image URL', field: 'Фото: URL (необязательно)', value: 'data:image/png;base64,abc' },
  ])('rejects $label without a save request', ({ field, value }) => {
    open(); basic(); change(field, value)
    expect((screen.getByRole('button', { name: 'Сохранить' }) as HTMLButtonElement).disabled).toBe(true)
    save()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('accepts the URL and name length boundaries and a fractional price', async () => {
    const { onClose } = open()
    change('Ссылка на товар', 'https://shop.example/' + 'a'.repeat(1979))
    change('Название', 'a'.repeat(500))
    change('Цена (необязательно)', '0.01')
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(String(body().url).length).toBe(2000)
    expect(String(body().name).length).toBe(500)
    expect(body().price).toBe(0.01)
  })

  it.each([{ label: 'duplicate', status: 409 }, { label: 'server error', status: 500 }])('retains the editable form after $label and allows retry', async ({ status }) => {
    adapter.mockImplementationOnce(async (config) => {
      throw new AxiosError('Save failed', 'ERR_BAD_RESPONSE', config, undefined, { data: { detail: 'Save failed' }, status, statusText: 'Failure', headers: {}, config })
    })
    const { onClose } = open(item)
    change('Название', 'Unsaved edit')
    save()
    expect((await screen.findByRole('alert')).textContent).toContain(status === 409 ? 'уже есть' : 'Save failed')
    expect(input('Название').value).toBe('Unsaved edit')
    expect(onClose).not.toHaveBeenCalled()
    change('Название', 'Retried edit')
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(body(1).name).toBe('Retried edit')
  })
})

describe('WishlistItemModal explicit metadata', () => {
  it('loads only on click, blocks duplicate fetch, and allows manual save during the request', async () => {
    const pending = deferred()
    const resolve = vi.spyOn(metadata, 'resolveMetadata').mockReturnValue(pending.promise)
    const { onClose } = open(); basic()
    expect(resolve).not.toHaveBeenCalled()
    fetchData()
    expect(resolve).toHaveBeenCalledOnce()
    const signal = resolve.mock.calls[0][1]
    expect((screen.getByRole('button', { name: 'Получение данных...' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Сохранить' }) as HTMLButtonElement).disabled).toBe(false)
    save()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(signal.aborted).toBe(true)
    expect(body().name).toBe('Manual gift')
    await act(async () => pending.resolve(result))
    expect(resolve).toHaveBeenCalledOnce()
  })

  it('ignores a late response after changing URL even when the old request ignores abort', async () => {
    const old = deferred(), current = deferred()
    const resolve = vi.spyOn(metadata, 'resolveMetadata').mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    open(); change('Ссылка на товар', 'https://shop.example/a'); fetchData()
    change('Ссылка на товар', 'https://shop.example/b')
    expect(resolve.mock.calls[0][1].aborted).toBe(true)
    fetchData()
    await act(async () => old.resolve(result))
    expect(input('Название').value).toBe('')
    expect(input('Фото: URL (необязательно)').value).toBe('')
    expect(screen.queryByText('Найдено: Found gift')).toBeNull()
    expect(screen.getByRole('button', { name: 'Получение данных...' })).toBeDefined()
    await act(async () => current.resolve({ ...result, name: 'Current gift' }))
    expect(input('Название').value).toBe('Current gift')
    expect(input('Фото: URL (необязательно)').value).toBe('https://images.example/found.jpg')
    expect(adapter).not.toHaveBeenCalled()
  })

  it('preserves manual edits and manual clearing during metadata loading until explicit apply', async () => {
    const pending = deferred()
    vi.spyOn(metadata, 'resolveMetadata').mockReturnValue(pending.promise)
    open(); change('Ссылка на товар', item.url); fetchData()
    change('Название', 'Typed during request')
    change('Фото: URL (необязательно)', 'https://images.example/manual.jpg')
    fireEvent.click(screen.getByRole('button', { name: 'Удалить фото' }))
    await act(async () => pending.resolve(result))
    expect(input('Название').value).toBe('Typed during request')
    expect(input('Фото: URL (необязательно)').value).toBe('')
    fireEvent.click(screen.getByRole('button', { name: 'Применить найденное' }))
    expect(input('Название').value).toBe('Found gift')
    expect(input('Фото: URL (необязательно)').value).toBe('https://images.example/found.jpg')
    expect(adapter).not.toHaveBeenCalled()
    change('Ссылка на товар', 'https://other.example/new')
    expect(input('Название').value).toBe('Found gift')
    expect(input('Фото: URL (необязательно)').value).toBe('https://images.example/found.jpg')
  })

  it('keeps existing populated fields until explicit apply and never changes price', async () => {
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue(result)
    open(item); fetchData()
    await screen.findByRole('button', { name: 'Применить найденное' })
    expect(input('Название').value).toBe('My gift')
    expect(input('Фото: URL (необязательно)').value).toBe('https://images.example/old.jpg')
    expect(input('Цена (необязательно)').value).toBe('25')
    fireEvent.click(screen.getByRole('button', { name: 'Применить найденное' }))
    expect(input('Название').value).toBe('Found gift')
    expect(input('Цена (необязательно)').value).toBe('25')
  })

  it('clears unconfirmed automatic fields on URL change but retains subsequent manual edits', async () => {
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue(result)
    open(); change('Ссылка на товар', item.url); fetchData()
    await waitFor(() => expect(input('Название').value).toBe('Found gift'))
    change('Название', 'My edited suggestion')
    change('Ссылка на товар', 'https://other.example/new')
    expect(input('Название').value).toBe('My edited suggestion')
    expect(input('Фото: URL (необязательно)').value).toBe('')
    expect(screen.queryByRole('button', { name: 'Применить найденное' })).toBeNull()
  })

  it('clears both automatic fields for the previous URL', async () => {
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue(result)
    open(); change('Ссылка на товар', item.url); fetchData()
    await waitFor(() => expect(input('Название').value).toBe('Found gift'))
    change('Ссылка на товар', 'https://other.example/new')
    expect(input('Название').value).toBe('')
    expect(input('Фото: URL (необязательно)').value).toBe('')
  })

  it.each([
    { label: 'unsupported source', value: { ...result, status: 'unsupported' as const } },
    { label: 'unavailable source', value: { ...result, status: 'unavailable' as const } },
    { label: 'empty result', value: { ...result, name: null, imageUrl: null } },
  ])('allows manual save after $label', async ({ value }) => {
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue(value)
    const { onClose } = open(); basic(); fetchData()
    await screen.findByRole('status')
    expect(input('Название').value).toBe('Manual gift')
    expect(input('Фото: URL (необязательно)').value).toBe('')
    save(); await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(body().name).toBe('Manual gift')
  })

  it('handles timeout failure without blocking save or automatically retrying', async () => {
    const resolve = vi.spyOn(metadata, 'resolveMetadata').mockRejectedValue(new Error('timeout'))
    const { onClose } = open(); basic(); fetchData()
    expect((await screen.findByRole('status')).textContent).toContain('Заполните поля вручную')
    expect(resolve).toHaveBeenCalledOnce()
    save(); await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(body().imageUrl).toBeNull()
  })

  it('accepts partial metadata and rejects an unsafe image suggestion', async () => {
    vi.spyOn(metadata, 'resolveMetadata').mockResolvedValue({ ...result, status: 'partial', imageUrl: 'javascript:alert(1)' })
    open(); change('Ссылка на товар', item.url); fetchData()
    await waitFor(() => expect(input('Название').value).toBe('Found gift'))
    expect(input('Фото: URL (необязательно)').value).toBe('')
    expect(screen.queryByText('Найдено фото товара')).toBeNull()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('aborts metadata on closing and ignores its late completion', async () => {
    const pending = deferred()
    const resolve = vi.spyOn(metadata, 'resolveMetadata').mockReturnValue(pending.promise)
    const { unmount, onClose } = open(); change('Ссылка на товар', item.url); fetchData()
    unmount()
    expect(resolve.mock.calls[0][1].aborted).toBe(true)
    await act(async () => pending.resolve(result))
    expect(adapter).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })
})

it('ignores metadata queued before a URL edit in the same render batch', async () => {
  const pending = deferred()
  vi.spyOn(metadata, 'resolveMetadata').mockReturnValue(pending.promise)
  open()
  change('Ссылка на товар', 'https://shop.example/a')
  fetchData()
  await act(async () => {
    pending.resolve(result)
    await Promise.resolve()
    change('Ссылка на товар', 'https://shop.example/b')
  })
  expect(input('Ссылка на товар').value).toBe('https://shop.example/b')
  expect(input('Название').value).toBe('')
  expect(input('Фото: URL (необязательно)').value).toBe('')
  expect(screen.queryByRole('button', { name: 'Применить найденное' })).toBeNull()
  expect(adapter).not.toHaveBeenCalled()
})

it.each([
  { label: 'Tab', shiftKey: false, destination: 'Закрыть форму' },
  { label: 'Shift+Tab', shiftKey: true, destination: 'Сохранить' },
])('keeps $label inside the dialog after a duplicate save error', async ({ shiftKey, destination }) => {
  let finishSave!: () => void
  const pendingSave = new Promise<void>((resolve) => { finishSave = resolve })
  adapter.mockImplementationOnce(async (config) => {
    await pendingSave
    throw new AxiosError('Duplicate', 'ERR_BAD_RESPONSE', config, undefined, {
      data: {}, status: 409, statusText: 'Conflict', headers: {}, config,
    })
  })
  const { onClose } = open()
  basic()
  save()
  const dialog = screen.getByRole('dialog')
  await waitFor(() => expect(document.activeElement).toBe(dialog))
  const whileSaving = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
  dialog.dispatchEvent(whileSaving)
  expect(whileSaving.defaultPrevented).toBe(true)
  expect(document.activeElement).toBe(dialog)

  await act(async () => { finishSave() })
  expect((await screen.findByRole('alert')).textContent).toContain('уже есть')
  expect(document.activeElement).toBe(dialog)
  const afterError = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
  dialog.dispatchEvent(afterError)
  expect(afterError.defaultPrevented).toBe(true)
  expect(document.activeElement).toBe(screen.getByRole('button', { name: destination }))
  expect(input('Название').value).toBe('Manual gift')
  expect(onClose).not.toHaveBeenCalled()
})
