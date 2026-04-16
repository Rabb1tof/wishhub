import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import type { Friendship, FriendRequest } from '@/types/friends'

const FRIENDS_KEY = 'friends'
const REQUESTS_KEY = 'friend-requests'

export function useFriends() {
  return useQuery({
    queryKey: [FRIENDS_KEY],
    queryFn: async () => {
      const res = await api.get<Friendship[]>('/friends')
      return res.data
    },
  })
}

export function useFriendRequests(type: 'incoming' | 'outgoing' = 'incoming') {
  return useQuery({
    queryKey: [REQUESTS_KEY, type],
    queryFn: async () => {
      const res = await api.get<FriendRequest[]>(`/friends/requests?type=${type}`)
      return res.data
    },
  })
}

export function useSendRequest() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (userId: string) => {
      const res = await api.post<Friendship>(`/friends/request/${userId}`)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [REQUESTS_KEY, 'outgoing'] })
      queryClient.invalidateQueries({ queryKey: ['profile'] })
    },
  })
}

export function useAcceptRequest() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (friendshipId: string) => {
      const res = await api.post<Friendship>(`/friends/accept/${friendshipId}`)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [FRIENDS_KEY] })
      queryClient.invalidateQueries({ queryKey: [REQUESTS_KEY] })
    },
  })
}

export function useRejectOrCancelRequest() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (friendshipId: string) => {
      await api.delete(`/friends/request/${friendshipId}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [REQUESTS_KEY] })
    },
  })
}

export function useRemoveFriend() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (userId: string) => {
      await api.delete(`/friends/${userId}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [FRIENDS_KEY] })
      queryClient.invalidateQueries({ queryKey: ['profile'] })
    },
  })
}
