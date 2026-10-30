import { useState } from 'react'
import { isHttpUrl } from '@/utils/url'

interface ItemImageProps {
  imageUrl: string | null
  name: string
  className?: string
}

export function ItemImage({ imageUrl, name, className = 'w-full h-48 object-cover' }: ItemImageProps) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  const source = imageUrl && isHttpUrl(imageUrl) ? imageUrl.trim() : null

  if (!source || source === failedUrl) {
    return (
      <div className={`${className} bg-gray-100 flex items-center justify-center`}>
        <span className="text-gray-400 text-sm">Нет изображения</span>
      </div>
    )
  }

  return (
    <img
      src={source}
      alt={name}
      loading="lazy"
      referrerPolicy="no-referrer"
      onError={() => setFailedUrl(source)}
      className={className}
    />
  )
}
