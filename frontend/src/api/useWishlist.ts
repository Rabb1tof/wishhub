import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import api from './client'
import type { WishlistItem, AddWishlistItemRequest, UpdateWishlistItemRequest } from '@/types/wishlist'

const WISHLIST_KEY = 'wishlist'

export function useWishlist(userId?: string) {
  const queryKey = userId ? [WISHLIST_KEY, userId] : [WISHLIST_KEY, 'me']

  return useQuery({
    queryKey,
    queryFn: async () => {
      const endpoint = userId ? `/wishlist/user/${userId}` : '/wishlist'
      try {
        const res = await api.get<WishlistItem[]>(endpoint)
        return { items: res.data, hidden: false }
      } catch (err: any) {
        if (err?.response?.status === 403) {
          return { items: [] as WishlistItem[], hidden: true }
        }
        throw err
      }
    },
  })
}

export function useAddItem() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: AddWishlistItemRequest) => {
      const res = await api.post<WishlistItem>('/wishlist', data)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}

export function useUpdateItem() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateWishlistItemRequest }) => {
      const res = await api.put<WishlistItem>(`/wishlist/${id}`, data)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}

export function useDeleteItem() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      await api.delete(`/wishlist/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}

export function useReserveItem() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await api.post<WishlistItem>(`/wishlist/${id}/reserve`)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}

export function useRefreshItem() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await api.post<WishlistItem>(`/wishlist/${id}/refresh`)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}

export function useCancelReservation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await api.post<WishlistItem>(`/wishlist/${id}/cancel-reservation`)
      return res.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WISHLIST_KEY] })
    },
  })
}
