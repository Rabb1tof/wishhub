import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import type { Message, Dialog } from '@/types/messages'

const MESSAGES_KEY = 'messages'
const DIALOGS_KEY = 'dialogs'

export function useDialogs() {
  return useQuery({
    queryKey: [DIALOGS_KEY],
    queryFn: async () => {
      const res = await api.get<Dialog[]>('/messages/dialogs')
      return res.data
    },
  })
}

export function useMessages(userId: string | null) {
  return useQuery({
    queryKey: [MESSAGES_KEY, userId],
    queryFn: async () => {
      if (!userId) return []
      const res = await api.get<Message[]>(`/messages/${userId}`)
      return res.data
    },
    enabled: !!userId,
  })
}

export function useSendMessage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId, content }: { userId: string; content: string }) => {
      const res = await api.post<Message>(`/messages/${userId}`, { content })
      return res.data
    },
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: [MESSAGES_KEY, variables.userId] })
      queryClient.invalidateQueries({ queryKey: [DIALOGS_KEY] })
    },
  })
}

export function useMarkAsRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (messageId: string) => {
      await api.post(`/messages/${messageId}/read`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [MESSAGES_KEY] })
      queryClient.invalidateQueries({ queryKey: [DIALOGS_KEY] })
      queryClient.invalidateQueries({ queryKey: [MESSAGES_KEY, 'unread'] })
    },
  })
}

export function useMarkDialogAsRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (userId: string) => {
      await api.post(`/messages/${userId}/read-all`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [MESSAGES_KEY] })
      queryClient.invalidateQueries({ queryKey: [DIALOGS_KEY] })
      queryClient.invalidateQueries({ queryKey: [MESSAGES_KEY, 'unread'] })
    },
  })
}

export function useUnreadCount() {
  return useQuery({
    queryKey: [MESSAGES_KEY, 'unread'],
    queryFn: async () => {
      const res = await api.get<number>('/messages/unread/count')
      return res.data
    },
    refetchInterval: 30000, // 30 seconds
  })
}
