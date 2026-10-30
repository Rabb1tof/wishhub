import { useEffect, useRef, useState } from 'react'
import { getWishlistImage } from '@/api/wishlistImages'
import { useAuthStore } from '@/store/auth'
import { isManagedImageUrl, isWishlistImageUrl } from '@/utils/wishlistImages'

interface ItemImageProps {
  imageUrl: string | null
  name: string
  className?: string
}

export function ItemImage({ imageUrl, name, className = 'wh-item-image' }: ItemImageProps) {
  const accessToken = useAuthStore((state) => state.accessToken)
  const userId = useAuthStore((state) => state.user?.id)
  const managed = !!imageUrl && isManagedImageUrl(imageUrl)
  const source = managed ? imageUrl : imageUrl && isWishlistImageUrl(imageUrl) ? imageUrl.trim() : null
  // Remount before rendering another resource or auth context so a previous private image cannot flash.
  const imageKey = managed ? `${source}:${userId ?? ''}:${accessToken ?? ''}` : source
  return <ImageContent key={imageKey} source={source} managed={managed} name={name} className={className} />
}

function ImageContent({ source, managed, name, className }: {
  source: string | null
  managed: boolean
  name: string
  className: string
}) {
  const container = useRef<HTMLDivElement>(null)
  const [blobUrl, setBlobUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!managed || !source) return
    const controller = new AbortController()
    let objectUrl: string | null = null
    let observer: IntersectionObserver | undefined
    let started = false
    const load = () => {
      if (started || controller.signal.aborted) return
      started = true
      observer?.disconnect()
      void getWishlistImage(source, controller.signal).then((blob) => {
        if (controller.signal.aborted) return
        objectUrl = URL.createObjectURL(blob)
        setBlobUrl(objectUrl)
      }).catch(() => {
        if (!controller.signal.aborted) setFailed(true)
      })
    }
    if (typeof IntersectionObserver === 'undefined') load()
    else {
      observer = new IntersectionObserver((entries) => {
        if (entries.some((entry) => entry.isIntersecting)) load()
      }, { rootMargin: '200px' })
      if (container.current) observer.observe(container.current)
    }
    return () => {
      controller.abort()
      observer?.disconnect()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [source, managed])

  const displayUrl = managed ? blobUrl : source
  return (
    <div ref={container}>
      {displayUrl && !failed ? (
        <img src={displayUrl} alt={name} loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} className={className} />
      ) : (
        <div className={`${className} wh-image-placeholder${!source || failed ? ' wh-image-placeholder--empty' : ''}`}>
          <svg viewBox="0 0 32 36" width="28" height="32" fill="none" stroke="currentColor" strokeWidth="1.4" aria-hidden="true">
            <path d="M7 3h18v29l-9-5-9 5V3Z" strokeLinejoin="round" />
            <path d="m16 20-5-5a3.1 3.1 0 0 1 5-3.6 3.1 3.1 0 0 1 5 3.6l-5 5Z" strokeLinejoin="round" />
          </svg>
          <span>{source && !failed ? 'Загрузка фото…' : 'Нет изображения'}</span>
        </div>
      )}
    </div>
  )
}
