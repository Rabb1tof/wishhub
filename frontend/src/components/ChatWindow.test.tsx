import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import api from '@/api/client'
import { addLiveMessage, applyReadReceipt, refreshActiveMessages, useSendMessage } from '@/api/useMessages'
import { useAuthStore } from '@/store/auth'
import type { Message } from '@/types/messages'
import { ChatWindow } from './ChatWindow'

const me = { id: 'owner', username: 'owner', displayName: 'Owner' }
const peer = { id: 'peer', username: 'peer', displayName: 'Peer' }
const originalAdapter = api.defaults.adapter
let client: QueryClient
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

class ChatObserver {
  static active = new Set<ChatObserver>()
  private rows = new Set<Element>()

  private notify: IntersectionObserverCallback

  constructor(notify: IntersectionObserverCallback, options: IntersectionObserverInit) {
    this.notify = notify
    if (!(options.root instanceof HTMLElement)) throw new Error('Chat observer must have a viewport')
    ChatObserver.active.add(this)
  }

  observe(row: Element) { this.rows.add(row) }
  disconnect() { ChatObserver.active.delete(this) }

  static intersects(...ids: string[]) {
    for (const observer of ChatObserver.active) {
      const entries = [...observer.rows].map((target) => {
        const seen = ids.includes((target as HTMLElement).dataset.unreadId ?? '')
        return { target, isIntersecting: seen, intersectionRatio: seen ? 1 : 0 } as IntersectionObserverEntry
      })
      observer.notify(entries, observer as unknown as IntersectionObserver)
    }
  }
}

function login(user = me) {
  useAuthStore.getState().setAuth({
    user, accessToken: 'test-access', refreshToken: 'test-refresh', expiresAt: '2099-01-01',
  })
}

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function mount() {
  return render(<ChatWindow userId={peer.id} userName={peer.displayName} />, { wrapper })
}

function message(id: string, minute: number, sender = peer.id): Message {
  return {
    id, senderId: sender, receiverId: sender === peer.id ? me.id : peer.id,
    content: id, isRead: false, createdAt: new Date(Date.UTC(2026, 9, 31, 0, minute)).toISOString(),
  }
}

function response(config: InternalAxiosRequestConfig, data: unknown, headers = {}) {
  return { config, data, status: 200, statusText: 'OK', headers }
}

function unavailable(config: InternalAxiosRequestConfig): never {
  throw new AxiosError('Unavailable', 'ERR_BAD_RESPONSE', config)
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  adapter = vi.fn<AxiosAdapter>()
  api.defaults.adapter = adapter
  login()
  vi.stubGlobal('IntersectionObserver', ChatObserver)
})

afterEach(() => {
  cleanup()
  client.clear()
  useAuthStore.getState().clearAuth()
  onlineManager.setOnline(true)
  ChatObserver.active.clear()
  api.defaults.adapter = originalAdapter
  vi.unstubAllGlobals()
})

it('loads earlier history with a cursor, retries a failed page, and keeps a concurrent live message', async () => {
  const newest = message('Newest', 5)
  const older = message('Older', 1)
  const live = message('Live arrival', 6)
  let olderAttempt = 0
  let finishPage!: () => void
  const pending = new Promise<void>((resolve) => { finishPage = resolve })
  adapter.mockImplementation(async (config) => {
    if (config.method !== 'get') throw new Error('Opening history must not mark an off-screen row')
    if (!config.params.before) return response(config, [newest], { 'x-next-before': newest.id })
    expect(config.params.before).toBe(newest.id)
    if (olderAttempt++ === 0) unavailable(config)
    await pending
    return response(config, [older])
  })
  mount()
  await screen.findByText(newest.content)
  const earlier = () => screen.getByRole('button', { name: 'Загрузить более ранние сообщения' })
  fireEvent.click(earlier())
  await screen.findByText('Не удалось загрузить предыдущие сообщения.')
  expect(screen.getByText(newest.content)).toBeTruthy()
  fireEvent.click(earlier())
  await waitFor(() => expect(olderAttempt).toBe(2))
  let applied!: Promise<void>
  act(() => { applied = addLiveMessage(client, peer.id, live, useAuthStore.getState().sessionVersion) })
  await act(async () => { finishPage(); await applied })
  await screen.findByText(live.content)
  expect(Array.from(document.querySelectorAll('[data-chat-message] p:first-child')).map((row) => row.textContent))
    .toEqual([older.content, newest.content, live.content])
  expect(screen.queryByRole('button', { name: 'Загрузить более ранние сообщения' })).toBeNull()
})

it('marks only intersecting incoming rows and retries exactly the failed IDs', async () => {
  const unseen = message('Off-screen', 1)
  const seen = message('Visible', 2)
  const outgoing = message('Outgoing', 3, me.id)
  const batches: string[][] = []
  adapter.mockImplementation(async (config) => {
    if (config.method === 'get') return response(config, [unseen, seen, outgoing])
    expect(config.url).toBe('/messages/peer/read-batch')
    const { messageIds } = JSON.parse(config.data) as { messageIds: string[] }
    batches.push(messageIds)
    if (batches.length === 1) unavailable(config)
    return response(config, messageIds)
  })
  mount()
  await screen.findByText(seen.content)
  expect(batches).toEqual([])
  act(() => ChatObserver.intersects(seen.id, outgoing.id))
  await screen.findByText('Не удалось отметить сообщения прочитанными.')
  expect(batches).toEqual([[seen.id]])
  fireEvent.click(screen.getByRole('button', { name: 'Повторить отметку' }))
  await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  expect(batches).toEqual([[seen.id], [seen.id]])
  expect(document.querySelector(`[data-unread-id="${unseen.id}"]`)).not.toBeNull()
  expect(document.querySelector(`[data-unread-id="${seen.id}"]`)).toBeNull()
})

it('retains a read receipt delivered before the send response through an older-page load', async () => {
  const newest = message('Newest', 5)
  const sent = message('Sent text', 6, me.id)
  let finishSend!: () => void
  const pending = new Promise<void>((resolve) => { finishSend = resolve })
  adapter.mockImplementation(async (config) => {
    if (config.method === 'post') {
      await pending
      return response(config, sent)
    }
    return config.params.before
      ? response(config, [message('Earlier', 1)])
      : response(config, [newest], { 'x-next-before': newest.id })
  })
  mount()
  await screen.findByText(newest.content)
  fireEvent.change(screen.getByRole('textbox', { name: 'Текст сообщения' }), { target: { value: sent.content } })
  fireEvent.click(screen.getByRole('button', { name: 'Отправить' }))
  await waitFor(() => expect(adapter.mock.calls.some(([config]) => config.method === 'post')).toBe(true))
  await act(() => applyReadReceipt(client, peer.id, [sent.id], useAuthStore.getState().sessionVersion))
  fireEvent.click(screen.getByRole('button', { name: 'Загрузить более ранние сообщения' }))
  await screen.findByText('Earlier')
  await act(async () => { finishSend() })
  const row = (await screen.findByText(sent.content)).closest('[data-chat-message]')
  expect(row?.textContent).toContain('✓')
  expect(screen.getByText('Earlier')).toBeTruthy()
})

it('refreshes active history after reconnect even while an older page is loading', async () => {
  const newest = message('Initial newest', 5)
  const missed = message('Missed while disconnected', 6)
  const older = message('Earlier', 1)
  let latestRequests = 0
  let olderRequests = 0
  let finishPage!: () => void
  const pending = new Promise<void>((resolve) => { finishPage = resolve })
  adapter.mockImplementation(async (config) => {
    if (!config.params.before) {
      latestRequests++
      return response(config, latestRequests === 1 ? [newest] : [newest, missed], { 'x-next-before': newest.id })
    }
    olderRequests++
    await pending
    return response(config, [older])
  })
  mount()
  await screen.findByText(newest.content)
  fireEvent.click(screen.getByRole('button', { name: 'Загрузить более ранние сообщения' }))
  await waitFor(() => expect(olderRequests).toBe(1))
  act(() => refreshActiveMessages(client, useAuthStore.getState().sessionVersion))
  expect(latestRequests).toBe(1)
  await act(async () => { finishPage() })
  await screen.findByText(missed.content)
  expect(screen.getByText(older.content)).toBeTruthy()
  expect(latestRequests).toBe(2)
  expect(olderRequests).toBe(2)
})

it('refuses an offline send queued for an earlier login after the account changes', async () => {
  onlineManager.setOnline(false)
  const { result } = renderHook(useSendMessage, { wrapper })
  const version = useAuthStore.getState().sessionVersion
  let sent!: Promise<unknown>
  act(() => {
    sent = result.current.mutateAsync({ userId: peer.id, content: 'Old draft', sessionVersion: version })
      .catch((error: unknown) => error)
  })
  await waitFor(() => expect(result.current.isPaused).toBe(true))
  act(() => login({ id: 'another', username: 'another', displayName: 'Another' }))
  await act(async () => {
    onlineManager.setOnline(true)
    await sent
  })
  expect(await sent).toMatchObject({ code: 'ERR_CANCELED' })
  expect(adapter).not.toHaveBeenCalled()
})
