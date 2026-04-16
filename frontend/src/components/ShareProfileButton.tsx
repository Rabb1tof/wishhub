import { useState, useRef, useEffect } from 'react'

interface ShareProfileButtonProps {
  username: string
  displayName: string
}

export function ShareProfileButton({ username, displayName }: ShareProfileButtonProps) {
  const [open, setOpen] = useState(false)
  const [copied, setCopied] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)

  const profileUrl = `${window.location.origin}/profile/${username}`
  const shareText = `Посмотрите профиль ${displayName} в WishHub`

  // Закрываем дропдаун при клике вне
  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const handleNativeShare = async () => {
    if (navigator.share) {
      try {
        await navigator.share({ title: shareText, url: profileUrl })
        setOpen(false)
      } catch {
        // Пользователь отменил — ничего не делаем
      }
    } else {
      setOpen((v) => !v)
    }
  }

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(profileUrl)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // fallback
      const textarea = document.createElement('textarea')
      textarea.value = profileUrl
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      document.body.removeChild(textarea)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const encodedUrl = encodeURIComponent(profileUrl)
  const encodedText = encodeURIComponent(shareText)

  const shareLinks = [
    {
      label: 'Telegram',
      url: `https://t.me/share/url?url=${encodedUrl}&text=${encodedText}`,
      color: 'text-sky-600',
    },
    {
      label: 'ВКонтакте',
      url: `https://vk.com/share.php?url=${encodedUrl}&title=${encodedText}`,
      color: 'text-blue-700',
    },
    {
      label: 'WhatsApp',
      url: `https://api.whatsapp.com/send?text=${encodedText}%20${encodedUrl}`,
      color: 'text-green-600',
    },
    {
      label: 'Email',
      url: `mailto:?subject=${encodedText}&body=${encodedUrl}`,
      color: 'text-gray-700',
    },
  ]

  return (
    <div className="relative" ref={dropdownRef}>
      <button
        onClick={handleNativeShare}
        className="px-4 py-2 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 flex items-center gap-2"
      >
        <svg xmlns="http://www.w3.org/2000/svg" className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8.684 13.342C8.886 12.938 9 12.482 9 12c0-.482-.114-.938-.316-1.342m0 2.684a3 3 0 110-2.684m0 2.684l6.632 3.316m-6.632-6l6.632-3.316m0 0a3 3 0 105.367-2.684 3 3 0 00-5.367 2.684zm0 9.316a3 3 0 105.368 2.684 3 3 0 00-5.368-2.684z" />
        </svg>
        Поделиться
      </button>

      {open && (
        <div className="absolute right-0 mt-2 w-64 bg-white rounded-md shadow-lg border z-10 py-2">
          <button
            onClick={handleCopy}
            className="w-full text-left px-4 py-2 hover:bg-gray-50 text-sm"
          >
            {copied ? '✓ Ссылка скопирована' : 'Скопировать ссылку'}
          </button>
          <div className="border-t my-1" />
          {shareLinks.map((link) => (
            <a
              key={link.label}
              href={link.url}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => setOpen(false)}
              className={`block px-4 py-2 hover:bg-gray-50 text-sm ${link.color}`}
            >
              {link.label}
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
