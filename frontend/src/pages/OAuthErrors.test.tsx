import { cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { AxiosAdapter } from 'axios'
import api from '@/api/client'
import { useAuthStore } from '@/store/auth'
import { LoginPage } from './LoginPage'
import { AuthCallbackPage } from './AuthCallbackPage'

const originalAdapter = api.defaults.adapter
let client: QueryClient
let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  useAuthStore.getState().clearAuth()
  adapter = vi.fn<AxiosAdapter>(async config => {
    if (config.url !== '/auth/providers') throw new Error(`Unexpected request: ${config.url}`)
    return { config, data: { vk: false, telegramBotUsername: null }, status: 200, statusText: 'OK', headers: {} }
  })
  api.defaults.adapter = adapter
})

afterEach(() => {
  cleanup()
  client.clear()
  api.defaults.adapter = originalAdapter
})

it.each([
  { page: 'login', path: '/login', Component: LoginPage },
  { page: 'callback', path: '/auth/callback', Component: AuthCallbackPage },
])('explains required VK linking on the $page page without completing a session', async ({ path, Component }) => {
  render(<QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[`${path}?error=account_link_required`]}><Component /></MemoryRouter>
  </QueryClientProvider>)

  expect((await screen.findByText('Войдите в существующий аккаунт и привяжите VK в настройках')).textContent)
    .toBe('Войдите в существующий аккаунт и привяжите VK в настройках')
  expect(screen.queryByText('account_link_required')).toBeNull()
  expect(adapter.mock.calls.filter(([config]) => config.method === 'post')).toHaveLength(0)
  expect(useAuthStore.getState().user).toBeNull()
})
