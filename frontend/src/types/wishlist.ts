export interface Product {
  id: string
  name: string
  price: number
  currency: string
  imageProxyUrl?: string
  source: string
  url: string
}

export interface WishlistItem {
  id: string
  product: Product
  customName?: string
  sortOrder: number
  createdAt: string
  isReserved: boolean
  reservedById?: string
  reservedByUsername?: string
  reservedByDisplayName?: string
}

export interface AddWishlistItemRequest {
  url: string
  customName?: string
}

export interface UpdateWishlistItemRequest {
  customName?: string
  sortOrder?: number
}
