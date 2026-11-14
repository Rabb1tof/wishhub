import { useState, useRef, useEffect, useLayoutEffect, useMemo } from 'react'
import { useMessages, useSendMessage, useMarkMessagesAsRead } from '@/api/useMessages'
import type { Message } from '@/types/messages'
import { useAuthStore } from '@/store/auth'

interface ChatWindowProps {
  userId: string
  userName: string
}

function compareMessages(a: Message, b: Message) {
  const time = Date.parse(a.createdAt) - Date.parse(b.createdAt)
  // JS dates round to milliseconds, while PostgreSQL timestamps retain microseconds.
  return time || a.createdAt.replace(/Z$/, '').localeCompare(b.createdAt.replace(/Z$/, ''))
    || a.id.localeCompare(b.id)
}

export function ChatWindow({ userId, userName }: ChatWindowProps) {
  const sessionVersion = useAuthStore((state) => state.sessionVersion)
  const history = useMessages(userId)
  const messages = useMemo(() => {
    const byId = new Map(history.data?.pages.flatMap((page) => page.messages.map((m) => [m.id, m] as const)))
    return Array.from(byId.values()).sort(compareMessages)
  }, [history.data])
  const sendMessage = useSendMessage()
  const { mutate: markAsRead, isPending: marking, isError: readFailed, variables: readBatch } = useMarkMessagesAsRead()
  const attempted = useRef(new Set<string>())
  const [visible, setVisible] = useState(() => document.visibilityState !== 'hidden')
  const [newMessage, setNewMessage] = useState('')
  const viewportRef = useRef<HTMLDivElement>(null)
  const followLatest = useRef(true)
  const scrollAnchor = useRef<{ element: Element; top: number; pageCount: number } | null>(null)
  const pageCount = history.data?.pages.length ?? 0
  const lastMessageId = messages.at(-1)?.id

  useEffect(() => {
    const onVisibility = () => setVisible(document.visibilityState !== 'hidden')
    document.addEventListener('visibilitychange', onVisibility)
    return () => document.removeEventListener('visibilitychange', onVisibility)
  }, [])

  useLayoutEffect(() => {
    const viewport = viewportRef.current
    if (!viewport) return
    const anchor = scrollAnchor.current
    if (anchor && pageCount > anchor.pageCount) {
      viewport.scrollTop += anchor.element.getBoundingClientRect().top - anchor.top
      scrollAnchor.current = null
    } else if (!anchor && followLatest.current) {
      viewport.scrollTop = viewport.scrollHeight
    }
  }, [lastMessageId, pageCount])

  useEffect(() => {
    const root = viewportRef.current
    if (!root || !visible || marking || readFailed || !window.IntersectionObserver) return
    const unread = [...root.querySelectorAll<HTMLElement>('[data-unread-id]')]
      .filter((row) => !attempted.current.has(row.dataset.unreadId!))
    if (unread.length === 0) return

    let active = true
    const observer = new IntersectionObserver((entries) => {
      if (!active || document.visibilityState === 'hidden') return
      const messageIds = entries.filter((entry) => entry.isIntersecting && entry.intersectionRatio > 0)
        .map((entry) => (entry.target as HTMLElement).dataset.unreadId!)
        .slice(0, 100)
      if (!messageIds.length) return

      active = false
      for (const id of messageIds) attempted.current.add(id)
      markAsRead({ userId, messageIds, sessionVersion })
    }, { root })
    for (const row of unread) observer.observe(row)
    return () => {
      active = false
      observer.disconnect()
    }
  }, [markAsRead, marking, messages, readFailed, sessionVersion, userId, visible])

  const loadOlder = async () => {
    const first = viewportRef.current?.querySelector('[data-chat-message]')
    if (first) scrollAnchor.current = { element: first, top: first.getBoundingClientRect().top, pageCount }
    const result = await history.fetchNextPage({ cancelRefetch: false })
    if (result.isFetchNextPageError || result.data?.pages.length === pageCount) scrollAnchor.current = null
  }

  const handleSend = (event: React.FormEvent) => {
    event.preventDefault()
    if (!newMessage.trim() || sendMessage.isPending) return
    sendMessage.mutate({ userId, content: newMessage, sessionVersion }, { onSuccess: () => {
      followLatest.current = true
      const viewport = viewportRef.current
      if (viewport) viewport.scrollTop = viewport.scrollHeight
      setNewMessage('')
    } })
  }

  return (
    <div className="flex flex-col h-full">
      <div className="p-4 border-b">
        <h3 className="font-semibold">{userName}</h3>
      </div>

      <div
        ref={viewportRef}
        className="flex-1 overflow-y-auto p-4 space-y-4 [overflow-anchor:none]"
        onScroll={(event) => {
          const viewport = event.currentTarget
          followLatest.current = viewport.scrollHeight - viewport.clientHeight - viewport.scrollTop < 24
        }}
      >
        {history.isLoading && <p role="status">Загрузка сообщений...</p>}
        {history.isLoadingError && (
          <div>
            <p role="alert">Не удалось загрузить сообщения.</p>
            <button type="button" className="text-blue-700 underline" onClick={() => void history.refetch()}>
              Повторить загрузку
            </button>
          </div>
        )}
        {history.hasNextPage && (
          <div className="text-center">
            <button
              type="button"
              disabled={history.isFetching}
              className="text-blue-700 underline disabled:opacity-50"
              onClick={() => void loadOlder()}
            >
              {history.isFetchingNextPage ? 'Загрузка...' : 'Загрузить более ранние сообщения'}
            </button>
            {history.isFetchNextPageError && <p role="alert">Не удалось загрузить предыдущие сообщения.</p>}
          </div>
        )}
        {messages.map((message) => (
          <div
            key={message.id}
            data-chat-message
            data-unread-id={message.senderId === userId && !message.isRead ? message.id : undefined}
            className={`flex ${message.senderId === userId ? 'justify-start' : 'justify-end'}`}
          >
            <div
              className={`max-w-[70%] px-4 py-2 rounded-lg ${
                message.senderId === userId
                  ? 'bg-gray-200 text-gray-800'
                  : 'bg-blue-600 text-white'
              }`}
            >
              <p>{message.content}</p>
              <p className="text-xs opacity-70 mt-1">
                {new Date(message.createdAt).toLocaleTimeString()}
                {message.senderId !== userId && message.isRead && ' ✓'}
              </p>
            </div>
          </div>
        ))}
      </div>

      {readFailed && readBatch && (
        <div className="px-4">
          <p role="alert" className="text-red-600">Не удалось отметить сообщения прочитанными.</p>
          <button type="button" className="text-blue-700 underline" onClick={() => { if (visible) markAsRead(readBatch) }} disabled={!visible}>
            Повторить отметку
          </button>
        </div>
      )}
      {sendMessage.isError && <p role="alert" className="text-red-600 px-4">Не удалось отправить сообщение. Попробуйте ещё раз.</p>}
      <form onSubmit={handleSend} className="p-4 border-t flex gap-2">
        <input
          type="text"
          aria-label="Текст сообщения"
          maxLength={2000}
          disabled={sendMessage.isPending}
          value={newMessage}
          onChange={(event) => setNewMessage(event.target.value)}
          placeholder="Написать сообщение..."
          className="flex-1 px-4 py-2 border rounded-md"
        />
        <button
          type="submit"
          disabled={sendMessage.isPending || !newMessage.trim()}
          className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
        >
          Отправить
        </button>
      </form>
    </div>
  )
}
