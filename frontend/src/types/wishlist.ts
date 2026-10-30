export interface WishlistItem {
  id: string
  url: string
  name: string
  imageUrl: string | null
  price: number | null
  currency: string
  source: string
  sortOrder: number
  createdAt: string
  isReserved: boolean
  isReservedByMe: boolean
  reservedBy: { id: string; username: string; displayName: string; avatarUrl?: string } | null
}

export interface AddWishlistItemRequest {
  url: string
  name: string
  imageUrl?: string | null
  price?: number | null
  currency?: string
}

export interface UpdateWishlistItemRequest extends AddWishlistItemRequest {
  sortOrder?: number
}
