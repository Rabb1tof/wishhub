import { useState, useRef, useEffect } from 'react'
import { useMessages, useSendMessage, useMarkDialogAsRead } from '@/api/useMessages'

interface ChatWindowProps {
  userId: string
  userName: string
}

export function ChatWindow({ userId, userName }: ChatWindowProps) {
  const { data: messages, isLoading } = useMessages(userId)
  const sendMessage = useSendMessage()
  const markDialogAsRead = useMarkDialogAsRead()
  const [newMessage, setNewMessage] = useState('')
  const messagesEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Помечаем все сообщения в диалоге как прочитанные при открытии
  useEffect(() => {
    markDialogAsRead.mutateAsync(userId)
  }, [userId])

  // Помечаем новые входящие сообщения как прочитанные, пока диалог открыт
  useEffect(() => {
    if (messages && messages.length > 0) {
      const hasUnread = messages.some((m) => m.senderId === userId && !m.isRead)
      if (hasUnread) {
        markDialogAsRead.mutateAsync(userId)
      }
    }
  }, [messages, userId])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!newMessage.trim()) return

    await sendMessage.mutateAsync({ userId, content: newMessage })
    setNewMessage('')
    // Помечаем все входящие как прочитанные при отправке ответа
    markDialogAsRead.mutateAsync(userId)
  }

  if (isLoading) {
    return <div className="flex-1 flex items-center justify-center">Загрузка...</div>
  }

  return (
    <div className="flex flex-col h-full">
      <div className="p-4 border-b">
        <h3 className="font-semibold">{userName}</h3>
      </div>

      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {messages?.map((message) => (
          <div
            key={message.id}
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
        <div ref={messagesEndRef} />
      </div>

      <form onSubmit={handleSend} className="p-4 border-t flex gap-2">
        <input
          type="text"
          value={newMessage}
          onChange={(e) => setNewMessage(e.target.value)}
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
