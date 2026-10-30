import { useState } from 'react'

interface UserAvatarProps {
  displayName: string
  avatarUrl?: string
  size?: 'sm' | 'md' | 'lg'
  className?: string
}

const tints = ['#dde9e2', '#e0e8ed', '#ece4dc', '#eedfe0']

export function UserAvatar({ displayName, avatarUrl, size = 'md', className = '' }: UserAvatarProps) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  const characters = Array.from(displayName.trim())
  const initial = characters[0]?.toLocaleUpperCase() || '?'
  const hash = characters.reduce((value, character) => (value * 31 + (character.codePointAt(0) ?? 0)) >>> 0, 0)
  const classes = `wh-avatar wh-avatar--${size} ${className}`

  if (avatarUrl && avatarUrl !== failedUrl) {
    return <img src={avatarUrl} alt={displayName} className={classes} onError={() => setFailedUrl(avatarUrl)} />
  }

  return <span role="img" aria-label={displayName} className={classes} style={{ backgroundColor: tints[hash % tints.length] }}>{initial}</span>
}
