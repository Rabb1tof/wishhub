import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import type { Notification } from '@/types/notifications'

const NOTIFICATIONS_KEY = 'notifications'

// Raw response from API
interface NotificationRaw {
  id: string
  type: string
  payload: string
  createdAt: string
  isRead: boolean
}

const mapNotification = (raw: NotificationRaw): Notification => ({
  id: raw.id,
  type: raw.type as Notification['type'],
  title: getNotificationTitle(raw.type),
  message: raw.payload,
  isRead: raw.isRead,
  createdAt: raw.createdAt,
})

const getNotificationTitle = (type: string): string => {
  switch (type) {
    case 'friend_request': return 'New Friend Request'
    case 'friend_accepted': return 'Friend Request Accepted'
    case 'item_reserved': return 'Item Reserved'
    case 'new_message': return 'New Message'
    default: return 'Notification'
  }
}

export function useNotifications() {
  return useQuery({
    queryKey: [NOTIFICATIONS_KEY],
    queryFn: async () => {
      const res = await api.get<NotificationRaw[]>('/notifications')
      return res.data.map(mapNotification)
    },
  })
}

export function useUnreadNotificationsCount() {
  return useQuery({
    queryKey: [NOTIFICATIONS_KEY, 'count'],
    queryFn: async () => {
      const res = await api.get<number>('/notifications/count')
      return res.data
    },
    refetchInterval: 30000,
  })
}

export function useMarkNotificationAsRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      await api.post(`/notifications/${id}/read`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [NOTIFICATIONS_KEY] })
    },
  })
}

export function useMarkAllNotificationsAsRead() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      const res = await api.post<{ markedAsRead: number }>('/notifications/read-all')
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [NOTIFICATIONS_KEY] })
    },
  })
}
