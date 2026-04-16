import { Link, useNavigate } from 'react-router-dom'
import { useAuthStore } from '@/store/auth'
import { useLogout } from '@/api/useAuth'
import { useUnreadCount } from '@/api/useMessages'
import { useSignalR } from '@/hooks/useSignalR'
import { requestNotificationPermission } from '@/utils/notifications'
import { PrivateRoute } from './PrivateRoute'
import type { ReactNode } from 'react'

interface LayoutProps {
  children: ReactNode
  requireAuth?: boolean
}

export function Layout({ children, requireAuth = true }: LayoutProps) {
  const user = useAuthStore((s) => s.user)
  const logout = useLogout()
  const navigate = useNavigate()
  const { data: unreadCount } = useUnreadCount()

  // Глобальное SignalR-подключение для real-time обновлений
  useSignalR()

  // Запрос разрешения на уведомления при авторизации
  if (user && typeof Notification !== 'undefined' && Notification.permission === 'default') {
    requestNotificationPermission()
  }

  const handleLogout = async () => {
    await logout.mutateAsync()
    navigate('/login')
  }

  const content = (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex items-center space-x-8">
              <Link to="/" className="text-xl font-bold text-blue-600">
                WishHub
              </Link>
              {user && (
                <>
                  <Link to="/" className="text-gray-600 hover:text-gray-900">
                    Лента
                  </Link>
                  <Link to="/wishlist" className="text-gray-600 hover:text-gray-900">
                    Мой вишлист
                  </Link>
                  <Link to="/friends" className="text-gray-600 hover:text-gray-900">
                    Друзья
                  </Link>
                  <Link to="/messages" className="text-gray-600 hover:text-gray-900 relative">
                    Сообщения
                    {(unreadCount ?? 0) > 0 && (
                      <span className="absolute -top-2 -right-4 px-1.5 py-0.5 bg-red-500 text-white text-xs rounded-full leading-none">
                        {unreadCount}
                      </span>
                    )}
                  </Link>
                </>
              )}
            </div>

            <div className="flex items-center space-x-4">
              {user ? (
                <>
                  <Link
                    to={`/profile/${user.username}`}
                    className="text-gray-600 hover:text-gray-900"
                  >
                    {user.displayName}
                  </Link>
                  <Link
                    to="/settings"
                    className="text-gray-600 hover:text-gray-900"
                  >
                    Настройки
                  </Link>
                  <button
                    onClick={handleLogout}
                    disabled={logout.isPending}
                    className="text-red-600 hover:text-red-700"
                  >
                    Выйти
                  </button>
                </>
              ) : (
                <>
                  <Link to="/login" className="text-gray-600 hover:text-gray-900">
                    Вход
                  </Link>
                  <Link to="/register" className="text-blue-600 hover:text-blue-700">
                    Регистрация
                  </Link>
                </>
              )}
            </div>
          </div>
        </div>
      </nav>

      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        {children}
      </main>
    </div>
  )

  if (requireAuth) {
    return <PrivateRoute>{content}</PrivateRoute>
  }

  return content
}
