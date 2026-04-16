export type NotificationType = 'friend_request' | 'friend_accepted' | 'item_reserved' | 'new_message'

export interface Notification {
  id: string
  type: NotificationType
  title: string
  message: string
  relatedId?: string
  isRead: boolean
  createdAt: string
}
