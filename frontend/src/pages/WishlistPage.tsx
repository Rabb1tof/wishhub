import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { WishlistItemCard } from '@/components/WishlistItemCard'
import { WishlistItemModal } from '@/components/WishlistItemModal'
import { useWishlist } from '@/api/useWishlist'
import { useProfile } from '@/api/useProfile'
import { useAuthStore } from '@/store/auth'
import type { WishlistItem } from '@/types/wishlist'

export function WishlistPage() {
  const { username } = useParams<{ username?: string }>()
  const currentUser = useAuthStore((state) => state.user)
  const { data: profile, isLoading: profileLoading, isError: profileError, refetch: retryProfile } = useProfile(username)
  const isOwnWishlist = !username || (!!profile && profile.id === currentUser?.id)
  const { data: wishlist, isLoading, isError, refetch } = useWishlist(profile?.id, !username || !!profile)
  const [editing, setEditing] = useState<WishlistItem | 'new' | null>(null)
  const loading = isLoading || profileLoading

  return (
    <Layout>
      <section className="wh-wishlist">
        <header className="wh-wishlist-heading">
          {isOwnWishlist ? (
            <div>
              <h1 className="wh-page-heading">Мой вишлист</h1>
              <p className="wh-muted wh-wishlist-subline">{wishlist && !isError ? `Желаний в списке: ${wishlist.items.length}` : 'Вещи, которые хочется сохранить'}</p>
            </div>
          ) : profile ? (
            <Link to={`/profile/${profile.username}`} className="wh-person-link">
              <UserAvatar displayName={profile.displayName} avatarUrl={profile.avatarUrl} size="md" />
              <div>
                <h1 className="wh-page-heading">Вишлист {profile.displayName}</h1>
                <p className="wh-muted">@{profile.username}</p>
              </div>
            </Link>
          ) : <h1 className="wh-page-heading">{loading ? 'Вишлист' : 'Пользователь не найден'}</h1>}
          {isOwnWishlist ? (
            <button type="button" onClick={() => setEditing('new')} className="wh-button wh-button--primary">Добавить</button>
          ) : profile ? (
            <Link to={`/profile/${profile.username}`} className="wh-button wh-button--quiet">К профилю</Link>
          ) : null}
        </header>

        {loading ? (
          <div className="wh-wishlist-state" role="status"><p>Загрузка...</p></div>
        ) : profileError || isError ? (
          <div className="wh-wishlist-state">
            <p role="alert">{profileError ? 'Не удалось загрузить профиль.' : 'Не удалось загрузить вишлист.'}</p>
            <button type="button" className="wh-button wh-button--quiet" onClick={() => void (profileError ? retryProfile() : refetch())}>Повторить</button>
          </div>
        ) : wishlist?.hidden ? (
          <div className="wh-wishlist-state"><p>Вишлист скрыт пользователем</p></div>
        ) : wishlist?.items.length === 0 ? (
          <div className="wh-wishlist-state">
            <h2>{isOwnWishlist ? 'Ваш вишлист пуст' : 'Вишлист пользователя пуст'}</h2>
            {isOwnWishlist && <p className="wh-muted">Сохраните ссылку на первую вещь, которую хочется получить.</p>}
          </div>
        ) : (
          <div className="wh-wishlist-grid">
            {wishlist?.items.map((item) => <WishlistItemCard key={item.id} item={item} isOwner={isOwnWishlist} onEdit={() => setEditing(item)} />)}
          </div>
        )}
      </section>
      {isOwnWishlist && editing && <WishlistItemModal key={editing === 'new' ? 'new' : editing.id} item={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </Layout>
  )
}
