import { isHttpUrl } from './url'

const guid = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const emptyGuid = '00000000-0000-0000-0000-000000000000'
const managedImagePath = new RegExp(`^/api/wishlist-images/(${guid})/(${guid})\\.(?:jpg|png|webp)$`)

export function isManagedImageUrl(value: string): boolean {
  const match = managedImagePath.exec(value)
  return !!match && match[0] === value && match[1] !== emptyGuid && match[2] !== emptyGuid
}

export function isWishlistImageUrl(value: string): boolean {
  if (isManagedImageUrl(value)) return true
  if (!isHttpUrl(value)) return false
  try {
    const pathname = decodeURIComponent(new URL(value.trim()).pathname)
    return !pathname.toLowerCase().startsWith('/api/wishlist-images/')
  } catch {
    return false
  }
}
