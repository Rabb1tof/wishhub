import { StrictMode } from 'react'
import { act, cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/store/auth'
import { showBrowserNotification } from '@/utils/notifications'
import { stopConnection, useSignalR } from './useSignalR'

const transport = vi.hoisted(() => {
  class Connection {
    state = 'Disconnected'
    handlers = new Map<string, (payload: unknown) => void>()
    finishStart: () => void = () => {}
    rejectStart: (error: Error) => void = () => {}
    url = ''
    tokenFactory?: () => string | Promise<string>
    start = vi.fn(() => {
      this.state = 'Connecting'
      return new Promise<void>((resolve, reject) => {
        this.finishStart = () => { this.state = 'Connected'; resolve() }
        this.rejectStart = reject
      })
    })
    stop = vi.fn(async () => { this.state = 'Disconnected' })
    on = vi.fn((name: string, handler: (payload: unknown) => void) => { this.handlers.set(name, handler) })
    close: (error?: Error) => void = () => {}
    onclose = vi.fn((handler: (error?: Error) => void) => { this.close = handler })
    emit(name: string, payload: unknown = {}) { this.handlers.get(name)?.(payload) }
  }
  const connections: Connection[] = []
  class Builder {
    connection = new Connection()
    withUrl(url: string, options: { accessTokenFactory?: () => string | Promise<string> }) {
      this.connection.url = url
      this.connection.tokenFactory = options.accessTokenFactory
      return this
    }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { connections.push(this.connection); return this.connection }
  }
  return { connections, Builder }
})

vi.mock('@microsoft/signalr', () => ({ HubConnectionBuilder: transport.Builder, LogLevel: { Warning: 3 } }))
vi.mock('@/utils/notifications', () => ({ showBrowserNotification: vi.fn() }))

const userA = { id: 'user-a', username: 'anna', displayName: 'Anna' }
const userB = { id: 'user-b', username: 'boris', displayName: 'Boris' }
let client: QueryClient

function Consumer() {
  useSignalR()
  const location = useLocation()
  return <output data-testid="route">{location.pathname}{location.search}</output>
}

function mount() {
  return render(<StrictMode><QueryClientProvider client={client}>
    <MemoryRouter initialEntries={['/wishlist']}><Consumer /></MemoryRouter>
  </QueryClientProvider></StrictMode>)
}

function login(user = userA, token = 'access-a') {
  useAuthStore.getState().setAuth({ user, accessToken: token, refreshToken: `refresh-${user.id}`, expiresAt: '2030-01-01T00:00:00Z' })
}

beforeEach(async () => {
  await stopConnection()
  useAuthStore.getState().clearAuth()
  transport.connections.length = 0
  vi.clearAllMocks()
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
})

afterEach(async () => {
  cleanup()
  useAuthStore.getState().clearAuth()
  await act(async () => {
    for (const connection of transport.connections) connection.finishStart()
  })
  await stopConnection()
  client.clear()
  vi.restoreAllMocks()
})

describe('SignalR session lifecycle', () => {
  it('starts one connection across strict effect remounts and reads the refreshed token at reconnect', async () => {
    login()
    mount()
    expect(transport.connections).toHaveLength(1)
    const connection = transport.connections[0]
    await act(async () => { connection.finishStart() })
    expect(connection.url).toBe('/hubs/chat')
    expect(await connection.tokenFactory?.()).toBe('access-a')
    const sessionVersion = useAuthStore.getState().sessionVersion
    await act(async () => { useAuthStore.setState({ accessToken: 'access-refreshed' }) })
    expect(useAuthStore.getState().sessionVersion).toBe(sessionVersion)
    expect(await connection.tokenFactory?.()).toBe('access-refreshed')
    expect(transport.connections).toHaveLength(1)
    expect(connection.stop).not.toHaveBeenCalled()
  })

  it('stops a pending connection on logout even after the last consumer unmounts', async () => {
    login()
    const mounted = mount()
    const connection = transport.connections[0]
    mounted.unmount()
    await act(async () => { useAuthStore.getState().clearAuth() })
    expect(connection.stop).toHaveBeenCalled()
    await act(async () => { connection.finishStart() })
    expect(connection.state).toBe('Disconnected')
    expect(await connection.tokenFactory?.()).toBe('')
    const invalidation = vi.spyOn(client, 'invalidateQueries')
    connection.emit('ReceiveMessage', { senderId: userB.id, receiverId: userA.id, content: 'Old session event' })
    expect(invalidation).not.toHaveBeenCalled()
    expect(showBrowserNotification).not.toHaveBeenCalled()
  })

  it.each([
    { label: 'different account', user: userB },
    { label: 'new login for the same account', user: userA },
  ])('replaces a pending connection for $label and ignores its late completion and events', async ({ user }) => {
    login()
    mount()
    const previous = transport.connections[0]
    await act(async () => { login(user, 'access-next') })
    expect(previous.stop).toHaveBeenCalled()
    expect(transport.connections).toHaveLength(2)
    const current = transport.connections[1]
    await act(async () => { current.finishStart(); previous.finishStart() })
    expect(previous.state).toBe('Disconnected')
    previous.close()
    expect(current.state).toBe('Connected')
    expect(await previous.tokenFactory?.()).toBe('')
    expect(await current.tokenFactory?.()).toBe('access-next')
    const invalidation = vi.spyOn(client, 'invalidateQueries')
    for (const event of ['ReceiveMessage', 'MessageSent', 'MessageRead', 'ReceiveNotification']) {
      previous.emit(event, { senderId: 'peer', content: 'Late delivery', type: 'friend_request' })
    }
    expect(invalidation).not.toHaveBeenCalled()
    expect(showBrowserNotification).not.toHaveBeenCalled()
    current.emit('ReceiveMessage', { senderId: 'peer', receiverId: user.id, content: 'Current delivery' })
    expect(invalidation).toHaveBeenCalledWith({ queryKey: ['dialogs'] })
    expect(showBrowserNotification).toHaveBeenCalledWith('Новое сообщение', 'Current delivery', expect.any(Function))
  })

  it('ignores failure of an old start without disconnecting the current session', async () => {
    login()
    mount()
    const previous = transport.connections[0]
    await act(async () => { login(userB, 'access-b') })
    const current = transport.connections[1]
    expect(current).toBeDefined()
    await act(async () => { current.finishStart(); previous.rejectStart(new Error('Start cancelled')) })
    expect(current.state).toBe('Connected')
    expect(current.stop).not.toHaveBeenCalled()
    expect(await current.tokenFactory?.()).toBe('access-b')
  })

  it('routes a current notification through the router and ignores clicks after logout', async () => {
    login()
    mount()
    const connection = transport.connections[0]
    await act(async () => { connection.finishStart() })
    connection.emit('ReceiveMessage', { senderId: userB.id, receiverId: userA.id, content: 'Hello' })
    const click = vi.mocked(showBrowserNotification).mock.calls[0][2]!
    act(() => { click() })
    expect(screen.getByTestId('route').textContent).toBe('/messages?user=user-b')
    connection.emit('ReceiveMessage', { senderId: 'other-peer', receiverId: userA.id, content: 'Later' })
    const lateClick = vi.mocked(showBrowserNotification).mock.calls[1][2]!
    await act(async () => { useAuthStore.getState().clearAuth() })
    act(() => { lateClick() })
    expect(screen.getByTestId('route').textContent).toBe('/messages?user=user-b')
  })
})
