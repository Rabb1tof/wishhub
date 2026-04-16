import { useEffect } from 'react'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '@/store/auth'
import { useNotificationStore } from '@/store/notifications'
import { showBrowserNotification } from '@/utils/notifications'
import type { Message } from '@/types/messages'

// Module-level singleton — одно соединение на всё приложение
let connection: HubConnection | null = null
let connectingPromise: Promise<void> | null = null

async function ensureConnection(
  accessToken: string,
  queryClient: ReturnType<typeof useQueryClient>,
  addNotification: (n: any) => void,
) {
  if (connection?.state === 'Connected') return
  if (connectingPromise) { await connectingPromise; return }

  connectingPromise = (async () => {
    try {
      if (connection) {
        await connection.stop()
        connection = null
      }

      const conn = new HubConnectionBuilder()
        .withUrl(`/hubs/chat?access_token=${accessToken}`, {
          transport: 1 | 2 | 4,
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build()

      conn.on('ReceiveMessage', (message: Message) => {
        queryClient.invalidateQueries({ queryKey: ['messages'] })
        queryClient.invalidateQueries({ queryKey: ['dialogs'] })
        queryClient.invalidateQueries({ queryKey: ['messages', 'unread'] })

        showBrowserNotification(
          'Новое сообщение',
          message.content,
          () => { window.location.hash = `/messages?user=${message.senderId}` }
        )
      })

      conn.on('MessageSent', () => {
        queryClient.invalidateQueries({ queryKey: ['messages'] })
        queryClient.invalidateQueries({ queryKey: ['dialogs'] })
      })

      conn.on('MessageRead', () => {
        queryClient.invalidateQueries({ queryKey: ['messages'] })
        queryClient.invalidateQueries({ queryKey: ['dialogs'] })
        queryClient.invalidateQueries({ queryKey: ['messages', 'unread'] })
      })

      conn.on('ReceiveNotification', (notification: any) => {
        addNotification(notification)
        queryClient.invalidateQueries({ queryKey: ['messages', 'unread'] })

        if (notification.type === 'friend_request') {
          showBrowserNotification('Запрос в друзья', 'Новый запрос в друзья')
        } else if (notification.type === 'friend_accepted') {
          showBrowserNotification('Заявка принята', 'Ваша заявка в друзья принята')
        } else if (notification.type === 'item_reserved') {
          showBrowserNotification('Подарок зарезервирован', 'Кто-то зарезервировал подарок из вашего вишлиста')
        }
      })

      await conn.start()
      connection = conn
    } catch (error) {
      console.error('[SignalR] Connection failed:', error)
    } finally {
      connectingPromise = null
    }
  })()

  await connectingPromise
}

async function stopConnection() {
  if (connection) {
    await connection.stop()
    connection = null
  }
}

export function useSignalR() {
  const accessToken = useAuthStore((s) => s.accessToken)
  const addNotification = useNotificationStore((s) => s.addNotification)
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!accessToken) return
    ensureConnection(accessToken, queryClient, addNotification)
  }, [accessToken, queryClient, addNotification])

  // Не отключаем при размонтировании — singleton живёт пока приложение открыто
  // Отключение только при logout (через stopConnection)
}

export { stopConnection }
