import type { User } from './auth'

export interface Friendship {
  id: string
  user: User  // includes id, username, displayName, avatarUrl
  status: 'pending' | 'accepted' | 'blocked'
  createdAt: string
}

export interface FriendRequest {
  id: string
  from: User  // includes id, username, displayName, avatarUrl
  createdAt: string
}

export interface SendFriendRequestRequest {
  username: string
}
