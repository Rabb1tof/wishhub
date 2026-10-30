import api from './client'
import { isManagedImageUrl } from '@/utils/wishlistImages'
import type { UploadWishlistImageResponse } from '@/types/wishlistImages'

export async function uploadWishlistImage(file: File, signal: AbortSignal): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  const response = await api.post<UploadWishlistImageResponse>('/wishlist-images', form, { signal })
  const path = response.data?.imageUrl
  if (typeof path !== 'string' || !isManagedImageUrl(path)) {
    throw new Error('Сервер вернул некорректную ссылку на фото.')
  }
  return path
}

export async function getWishlistImage(path: string, signal: AbortSignal): Promise<Blob> {
  if (!isManagedImageUrl(path)) throw new Error('Некорректная ссылка на фото.')
  const response = await api.get<Blob>(path.slice(4), { responseType: 'blob', signal })
  return response.data
}
