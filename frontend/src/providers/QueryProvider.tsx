import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAuthStore } from '@/store/auth'
import { useNotificationStore } from '@/store/notifications'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

// Every login, logout and account switch uses the same cache boundary.
const unsubscribeAuth = useAuthStore.subscribe((current, previous) => {
  if (current.sessionVersion !== previous.sessionVersion || current.user?.id !== previous.user?.id) {
    void queryClient.cancelQueries()
    queryClient.clear()
    useNotificationStore.setState({ notifications: [], unreadCount: 0 })
  }
})

if (import.meta.hot) import.meta.hot.dispose(unsubscribeAuth)

export function QueryProvider({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}
