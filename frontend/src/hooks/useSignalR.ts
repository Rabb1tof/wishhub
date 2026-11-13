import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import type { NavigateFunction } from 'react-router-dom'
import { useAuthStore } from '@/store/auth'
import { addLiveMessage, applyReadReceipt, refreshActiveMessages } from '@/api/useMessages'
import { useNotificationStore } from '@/store/notifications'
import { showBrowserNotification } from '@/utils/notifications'
import type { Message } from '@/types/messages'
import type { Notification } from '@/types/notifications'

interface SessionConnection {
  hub: HubConnection
  userId: string
  sessionVersion: number
}

// One connection across route changes, owned by one login session.
let connection: SessionConnection | null = null
let lastStartedVersion: number | undefined
let context: { queryClient: QueryClient; navigate: NavigateFunction } | null = null

function isCurrent(session: SessionConnection): boolean {
  const auth = useAuthStore.getState()
  return connection === session && auth.user?.id === session.userId
    && auth.sessionVersion === session.sessionVersion && !!auth.accessToken
}

async function stopHub(hub: HubConnection): Promise<void> {
  try {
    await hub.stop()
  } catch (error) {
    console.error('[SignalR] Stop failed:', error)
  }
}

export async function stopConnection(): Promise<void> {
  const previous = connection
  connection = null
  if (previous) await stopHub(previous.hub)
}

function ensureConnection(): void {
  if (connection && isCurrent(connection)) return
  void stopConnection()
  const { user, accessToken, sessionVersion } = useAuthStore.getState()
  if (!context || !user || !accessToken) return

  const hub = new HubConnectionBuilder()
    .withUrl('/hubs/chat', {
      accessTokenFactory: () => isCurrent(session) ? useAuthStore.getState().accessToken ?? '' : '',
      transport: 1 | 2 | 4,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  const session: SessionConnection = { hub, userId: user.id, sessionVersion }
  // Publish before start so logout also owns and stops a pending handshake.
  connection = session

  hub.on('ReceiveMessage', (message: Message) => {
    if (!isCurrent(session) || !context) return
    const { queryClient } = context
    void addLiveMessage(queryClient, message.senderId, { ...message, isRead: false }, session.sessionVersion)
    void queryClient.invalidateQueries({ queryKey: ['dialogs'] })
    void queryClient.invalidateQueries({ queryKey: ['messages', 'unread'] })
    showBrowserNotification('Новое сообщение', message.content, () => {
      if (isCurrent(session)) void context?.navigate(`/messages?user=${encodeURIComponent(message.senderId)}`)
    })
  })
  hub.on('MessageSent', (message: Message) => {
    if (!isCurrent(session) || !context) return
    const { queryClient } = context
    void addLiveMessage(queryClient, message.receiverId, message, session.sessionVersion)
    void queryClient.invalidateQueries({ queryKey: ['dialogs'] })
  })
  hub.on('MessageRead', ({ readerId, messageIds }: { readerId: string; messageIds: string[] }) => {
    if (!isCurrent(session) || !context) return
    void applyReadReceipt(context.queryClient, readerId, messageIds, session.sessionVersion)
    void context.queryClient.invalidateQueries({ queryKey: ['dialogs'] })
  })
  hub.on('ReceiveNotification', (notification: Notification) => {
    if (!isCurrent(session)) return
    useNotificationStore.getState().addNotification(notification)
    void context?.queryClient.invalidateQueries({ queryKey: ['messages', 'unread'] })
    if (notification.type === 'friend_request') {
      showBrowserNotification('Запрос в друзья', 'Новый запрос в друзья')
    } else if (notification.type === 'friend_accepted') {
      showBrowserNotification('Заявка принята', 'Ваша заявка в друзья принята')
    }
  })
  const recoverMessages = () => {
    if (!isCurrent(session) || !context) return
    refreshActiveMessages(context.queryClient, session.sessionVersion)
    void context.queryClient.invalidateQueries({ queryKey: ['dialogs'] })
  }
  hub.onreconnected(recoverMessages)
  hub.onclose(() => {
    if (connection === session) connection = null
  })

  void hub.start().then(async () => {
    // A cancelled start may settle after a new session has already connected.
    if (!isCurrent(session)) {
      await stopHub(hub)
      return
    }
    if (lastStartedVersion === session.sessionVersion) recoverMessages()
    lastStartedVersion = session.sessionVersion
  }).catch((error: unknown) => {
    if (!isCurrent(session)) return
    connection = null
    console.error('[SignalR] Connection failed:', error)
  })
}

// Logout and account changes must stop delivery even while Layout is unmounted.
const unsubscribeAuth = useAuthStore.subscribe((auth, previous) => {
  if (auth.sessionVersion !== previous.sessionVersion || auth.user?.id !== previous.user?.id) {
    lastStartedVersion = undefined
  }
  if (auth.sessionVersion !== previous.sessionVersion || auth.user?.id !== previous.user?.id
    || auth.accessToken !== previous.accessToken) ensureConnection()
})

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    unsubscribeAuth()
    void stopConnection()
  })
}

export function useSignalR() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  useEffect(() => {
    context = { queryClient, navigate }
    ensureConnection()
  }, [queryClient, navigate])
}
