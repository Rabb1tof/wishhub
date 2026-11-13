import { useInfiniteQuery, useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { InfiniteData, QueryClient } from '@tanstack/react-query'
import { CanceledError } from 'axios'
import api from './client'
import { useAuthStore } from '@/store/auth'
import type { Message, Dialog } from '@/types/messages'

const DIALOGS_KEY = ['dialogs'] as const
const UNREAD_KEY = ['messages', 'unread'] as const
const historyKey = (userId: string) => ['messages', userId] as const

interface HistoryPage {
  messages: Message[]
  nextBefore?: string
  pendingReceipts?: string[]
}

type History = InfiniteData<HistoryPage, string | undefined>

function requireCurrentSession(sessionVersion: number) {
  if (useAuthStore.getState().sessionVersion !== sessionVersion) throw new CanceledError('Session changed')
}

export function useDialogs() {
  return useQuery({
    queryKey: DIALOGS_KEY,
    queryFn: async ({ signal }) => {
      const res = await api.get<Dialog[]>('/messages/dialogs', { signal })
      return res.data
    },
  })
}

export function useMessages(userId: string) {
  return useInfiniteQuery({
    queryKey: historyKey(userId),
    initialPageParam: undefined as string | undefined,
    queryFn: async ({ pageParam, signal }): Promise<HistoryPage> => {
      const res = await api.get<Message[]>(`/messages/${userId}`, {
        params: { before: pageParam },
        signal,
      })
      const cursor = res.headers['x-next-before']
      return { messages: res.data, nextBefore: typeof cursor === 'string' ? cursor : undefined }
    },
    getNextPageParam: (page) => page.nextBefore,
    refetchOnMount: 'always',
  })
}

// An older-page fetch commits a snapshot of all pages. Apply events after it settles.
async function afterMessageFetch(
  client: QueryClient,
  queryKey: readonly string[],
  sessionVersion: number,
  update: () => void,
) {
  for (;;) {
    if (useAuthStore.getState().sessionVersion !== sessionVersion) return
    const query = client.getQueryCache().find({ queryKey })
    if (!query) return
    if (query.state.fetchStatus === 'idle' || !query.promise) {
      update()
      return
    }
    await query.promise.catch(() => undefined)
  }
}

export function refreshActiveMessages(client: QueryClient, sessionVersion: number) {
  for (const query of client.getQueryCache().findAll({ queryKey: ['messages'], type: 'active' })) {
    const queryKey = query.queryKey as readonly string[]
    void afterMessageFetch(client, queryKey, sessionVersion, () => {
      void client.invalidateQueries({ queryKey, exact: true }, { cancelRefetch: false })
    })
  }
}

function updateHistory(client: QueryClient, userId: string, sessionVersion: number, update: (history: History) => History) {
  const queryKey = historyKey(userId)
  return afterMessageFetch(client, queryKey, sessionVersion, () => {
    client.setQueryData<History>(queryKey, (history) => history && update(history))
  })
}

export function addLiveMessage(client: QueryClient, userId: string, message: Message, sessionVersion: number) {
  return updateHistory(client, userId, sessionVersion, (history) => {
    if (!history.pages.length || history.pages.some((page) => page.messages.some((item) => item.id === message.id))) {
      return history
    }
    const [newest, ...older] = history.pages
    const received = newest.pendingReceipts?.includes(message.id) ? { ...message, isRead: true } : message
    return {
      ...history,
      pages: [{
        ...newest,
        pendingReceipts: newest.pendingReceipts?.filter((id) => id !== message.id),
        messages: [...newest.messages, received],
      }, ...older],
    }
  })
}

export function applyReadReceipt(client: QueryClient, userId: string, messageIds: string[], sessionVersion: number) {
  const read = new Set(messageIds)
  return updateHistory(client, userId, sessionVersion, (history) => {
    const unseen = new Set([...history.pages[0]?.pendingReceipts ?? [], ...read])
    const pages = history.pages.map((page) => ({
      ...page,
      messages: page.messages.map((message) => {
        unseen.delete(message.id)
        return !message.isRead && read.has(message.id) ? { ...message, isRead: true } : message
      }),
    }))
    // A recipient can read the message before the sender's POST completes.
    if (pages[0]) pages[0].pendingReceipts = Array.from(unseen).slice(-100)
    return { ...history, pages }
  })
}

export function useSendMessage() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId, content, sessionVersion }: { userId: string; content: string; sessionVersion: number }) => {
      requireCurrentSession(sessionVersion)
      const res = await api.post<Message>(`/messages/${userId}`, { content })
      return res.data
    },
    onSuccess: (message, { userId, sessionVersion }) => {
      if (sessionVersion !== useAuthStore.getState().sessionVersion) return
      void addLiveMessage(client, userId, message, sessionVersion)
      void client.invalidateQueries({ queryKey: DIALOGS_KEY })
    },
  })
}

export function useMarkMessagesAsRead() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId, messageIds, sessionVersion }: { userId: string; messageIds: string[]; sessionVersion: number }) => {
      requireCurrentSession(sessionVersion)
      const res = await api.post<string[]>(`/messages/${userId}/read-batch`, { messageIds })
      return res.data
    },
    onSuccess: (accepted, { userId, sessionVersion }) => {
      if (sessionVersion !== useAuthStore.getState().sessionVersion) return
      void applyReadReceipt(client, userId, accepted, sessionVersion)
      void client.invalidateQueries({ queryKey: DIALOGS_KEY })
      void client.invalidateQueries({ queryKey: UNREAD_KEY })
    },
  })
}

export function useUnreadCount(enabled = true) {
  return useQuery({
    queryKey: UNREAD_KEY,
    enabled,
    queryFn: async ({ signal }) => {
      const res = await api.get<number>('/messages/unread/count', { signal })
      return res.data
    },
    refetchInterval: 30000,
  })
}
