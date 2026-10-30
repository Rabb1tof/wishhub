import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { Link, NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '@/store/auth'
import { useLogout } from '@/api/useAuth'
import { useUnreadCount } from '@/api/useMessages'
import { useSignalR } from '@/hooks/useSignalR'
import { requestNotificationPermission } from '@/utils/notifications'
import { PrivateRoute } from './PrivateRoute'
import { UserAvatar } from './UserAvatar'

interface LayoutProps {
  children: ReactNode
  requireAuth?: boolean
}

const destinations = [
  { to: '/', label: 'Лента', path: 'M3 3h12v3H3zM3 9h5v6H3zM11 9h4M11 12h4M11 15h4' },
  { to: '/wishlist', label: 'Мой вишлист', path: 'M9 15.5 3.1 9.8a3.8 3.8 0 0 1 5.4-5.4L9 5l.5-.6a3.8 3.8 0 0 1 5.4 5.4Z' },
  { to: '/friends', label: 'Друзья', path: 'M6.5 8a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM2 15v-1a4.5 4.5 0 0 1 9 0v1M12 3.2a2.5 2.5 0 0 1 0 4.6M13 10a4 4 0 0 1 3 4v1' },
  { to: '/messages', label: 'Сообщения', path: 'M3 3h12v9H8l-5 3V3ZM6 6h6M6 9h4' },
]

const wordmark = <>
  <svg viewBox="0 0 32 32" aria-hidden="true" className="wh-mark" fill="none">
    <path d="M16 27C12 23 7 22 3 23V7c5-1 9 1 13 5 4-4 8-6 13-5v16c-4-1-9 0-13 4Z" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" />
    <path d="M16 12v15M8 12c2 .2 4 1.3 5 2.5M24 12c-2 .2-4 1.3-5 2.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
  </svg>
  <span>WishHub</span>
</>

export function Layout({ children, requireAuth = true }: LayoutProps) {
  const user = useAuthStore((state) => state.user)
  const userId = user?.id
  const logout = useLogout()
  const navigate = useNavigate()
  const { data: unreadCount } = useUnreadCount()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuButton = useRef<HTMLButtonElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const sidebar = useRef<HTMLElement>(null)

  useSignalR()

  useEffect(() => {
    if (userId) requestNotificationPermission()
  }, [userId])

  useEffect(() => {
    if (!menuOpen) return
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    closeButton.current?.focus()
    return () => { document.body.style.overflow = previousOverflow }
  }, [menuOpen])

  const closeMenu = () => {
    if (!menuOpen) return
    setMenuOpen(false)
    menuButton.current?.focus()
  }

  const handleMenuKey = (event: KeyboardEvent<HTMLElement>) => {
    if (!menuOpen) return
    if (event.key === 'Escape') {
      event.preventDefault()
      closeMenu()
    } else if (event.key === 'Tab') {
      const controls = sidebar.current?.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')
      if (!controls?.length) return
      const first = controls[0]
      const last = controls[controls.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
  }

  const content = (
    <div className="wh-shell">
      <a href="#wh-main" className="wh-skip-link" hidden={menuOpen}>К содержимому</a>
      <aside ref={sidebar} id="wh-navigation" className={`wh-sidebar${menuOpen ? ' is-open' : ''}`} role={menuOpen ? 'dialog' : undefined} aria-modal={menuOpen || undefined} aria-label={menuOpen ? 'Меню WishHub' : undefined} onKeyDown={handleMenuKey}>
        <div className="wh-sidebar-heading">
          <Link to="/" className="wh-wordmark" onClick={closeMenu} aria-label="WishHub">{wordmark}</Link>
          <button ref={closeButton} type="button" className="wh-button wh-button--quiet wh-close-menu" hidden={!menuOpen} onClick={closeMenu} aria-label="Закрыть меню">
            <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" fill="none"><path d="m5 5 10 10M15 5 5 15" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" /></svg>
          </button>
        </div>
        {user ? <>
          <nav className="wh-nav" aria-label="Основная навигация">
            {destinations.map(({ to, label, path }) => (
              <NavLink key={to} to={to} end={to === '/'} className="wh-nav-link" onClick={closeMenu}>
                <svg viewBox="0 0 18 18" className="wh-nav-icon" fill="none" aria-hidden="true"><path d={path} stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" /></svg>
                <span>{label}</span>
                {to === '/messages' && (unreadCount ?? 0) > 0 && <span className="wh-unread" aria-label={`${unreadCount} непрочитанных сообщений`}>{unreadCount}</span>}
              </NavLink>
            ))}
          </nav>
          <div className="wh-account">
            <NavLink to={`/profile/${user.username}`} className="wh-account-link" onClick={closeMenu}>
              <UserAvatar displayName={user.displayName} avatarUrl={user.avatarUrl} />
              <span className="wh-account-copy"><strong>{user.displayName}</strong><span>@{user.username}</span></span>
            </NavLink>
            <NavLink to="/settings" className="wh-nav-link" onClick={closeMenu}>
              <svg viewBox="0 0 18 18" className="wh-nav-icon" fill="none" aria-hidden="true"><path d="M3 5h12M3 13h12M6 3v4M12 11v4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" /></svg>
              <span>Настройки</span>
            </NavLink>
            <button type="button" className="wh-nav-link wh-logout" disabled={logout.isPending} onClick={() => logout.mutate(undefined, { onSuccess: () => navigate('/login') })}>
              <svg viewBox="0 0 18 18" className="wh-nav-icon" fill="none" aria-hidden="true"><path d="M7 3H3v12h4M7 9h8m-3-3 3 3-3 3" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" /></svg>
              <span>{logout.isPending ? 'Выход…' : 'Выйти'}</span>
            </button>
            {logout.isError && <p role="alert" className="wh-session-error">Не удалось выйти. Попробуйте ещё раз.</p>}
          </div>
        </> : <nav className="wh-nav" aria-label="Вход в аккаунт">
          <Link to="/login" className="wh-nav-link" onClick={closeMenu}>Вход</Link>
          <Link to="/register" className="wh-nav-link" onClick={closeMenu}>Регистрация</Link>
        </nav>}
      </aside>
      <header className="wh-mobile-bar" inert={menuOpen}>
        <Link to="/" className="wh-wordmark" aria-label="WishHub — лента">{wordmark}</Link>
        <button ref={menuButton} type="button" className="wh-button wh-button--quiet" aria-controls="wh-navigation" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)}>
          <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true" fill="none"><path d="M3 6h14M3 14h14" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" /></svg>
          Меню
        </button>
      </header>
      <main id="wh-main" className="wh-main" tabIndex={-1} inert={menuOpen}>{children}</main>
    </div>
  )

  return requireAuth ? <PrivateRoute>{content}</PrivateRoute> : content
}
