
interface UserAvatarProps {
  displayName: string
  avatarUrl?: string
  size?: 'sm' | 'md' | 'lg'
  className?: string
}

const sizeClasses = {
  sm: 'w-8 h-8 text-sm',
  md: 'w-10 h-10 text-lg',
  lg: 'w-20 h-20 text-3xl',
}

export function UserAvatar({ displayName, avatarUrl, size = 'md', className = '' }: UserAvatarProps) {
  const initials = displayName?.charAt(0).toUpperCase() || '?'

  if (avatarUrl) {
    return (
      <img
        src={avatarUrl}
        alt={displayName}
        className={`${sizeClasses[size]} rounded-full object-cover ${className}`}
      />
    )
  }

  return (
    <div className={`${sizeClasses[size]} rounded-full bg-gray-300 flex items-center justify-center font-bold text-gray-600 ${className}`}>
      {initials}
    </div>
  )
}
