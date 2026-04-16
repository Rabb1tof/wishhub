import type { User } from './auth'

export interface Message {
  id: string
  senderId: string
  receiverId: string
  content: string
  isRead: boolean
  createdAt: string
}

export interface Dialog {
  user: User
  lastMessage?: Message
  unreadCount: number
}

export interface SendMessageRequest {
  content: string
}
