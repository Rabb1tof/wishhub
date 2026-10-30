import { useParams, Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { ShareProfileButton } from '@/components/ShareProfileButton'
import { WishlistItemCard } from '@/components/WishlistItemCard'
import { useProfile } from '@/api/useProfile'
import { useWishlist } from '@/api/useWishlist'
import { useSendRequest, useRemoveFriend } from '@/api/useFriends'
import { useAuthStore } from '@/store/auth'

export function ProfilePage() {
  const { username } = useParams<{ username: string }>()
  const { data: profile, isLoading: profileLoading, isError: profileError, refetch: retryProfile } = useProfile(username)
  const { data: wishlist, isLoading: wishlistLoading, isError: wishlistError, refetch: retryWishlist } = useWishlist(profile?.id, Boolean(profile?.id))
  const sendRequest = useSendRequest()
  const removeFriend = useRemoveFriend()
  const currentUser = useAuthStore((s) => s.user)
  const isOwnProfile = profile?.id === currentUser?.id

  if (!profile) {
    return (
      <Layout>
        <section className="wh-wishlist">
          <header className="wh-wishlist-heading"><h1 className="wh-page-heading">Профиль</h1></header>
          <div className="wh-wishlist-state">
            <p role={profileError ? 'alert' : 'status'}>{profileLoading ? 'Загрузка...' : profileError ? 'Не удалось загрузить профиль.' : 'Пользователь не найден'}</p>
            {profileError && <button type="button" className="wh-button wh-button--quiet" onClick={() => void retryProfile()}>Повторить</button>}
          </div>
        </section>
      </Layout>
    )
  }

  const friendshipStatus = profile.friendshipStatus

  return (
    <Layout>
      <section className="wh-wishlist">
        <header className="wh-profile-heading">
          <div className="wh-profile-identity">
            <UserAvatar displayName={profile.displayName} avatarUrl={profile.avatarUrl} size="lg" />
            <div>
              <h1 className="wh-page-heading">{profile.displayName}</h1>
              <p className="wh-muted">@{profile.username}</p>
              {profile.bio && <p className="wh-profile-bio">{profile.bio}</p>}
            </div>
          </div>
          <div className="wh-profile-actions">
            {isOwnProfile && <ShareProfileButton username={profile.username} displayName={profile.displayName} />}
            {!isOwnProfile && friendshipStatus === null && (
              <button type="button" onClick={() => sendRequest.mutate(profile.id)} disabled={sendRequest.isPending} className="wh-button wh-button--quiet">{sendRequest.isPending ? 'Отправка...' : 'Добавить в друзья'}</button>
            )}
            {!isOwnProfile && friendshipStatus === 'pending' && <span className="wh-profile-status">Заявка отправлена</span>}
            {!isOwnProfile && friendshipStatus === 'accepted' && (
              <>
                <span className="wh-profile-status">В друзьях</span>
                <Link to={`/messages?user=${profile.id}`} className="wh-button wh-button--quiet">Написать сообщение</Link>
                <button type="button" onClick={() => { if (confirm('Удалить из друзей?')) removeFriend.mutate(profile.id) }} disabled={removeFriend.isPending} className="wh-button wh-button--danger">Удалить из друзей</button>
              </>
            )}
          </div>
          {(sendRequest.isError || removeFriend.isError) && <p role="alert" className="wh-wishlist-error">Не удалось изменить список друзей. Попробуйте ещё раз.</p>}
        </header>

        <div className="wh-wishlist-section-heading">
          <h2>Вишлист{!wishlist?.hidden ? ` (${profile.wishlistItemCount})` : ''}</h2>
          {!wishlist?.hidden && profile.wishlistItemCount > 3 && <Link to={isOwnProfile ? '/wishlist' : `/wishlist/${profile.username}`} className="wh-button wh-button--quiet">Показать все</Link>}
        </div>
        {wishlistLoading ? (
          <div className="wh-wishlist-state" role="status"><p>Загрузка...</p></div>
        ) : wishlistError ? (
          <div className="wh-wishlist-state">
            <p role="alert">Не удалось загрузить вишлист.</p>
            <button type="button" className="wh-button wh-button--quiet" onClick={() => void retryWishlist()}>Повторить</button>
          </div>
        ) : wishlist?.hidden ? (
          <div className="wh-wishlist-state"><p>Вишлист скрыт пользователем</p></div>
        ) : wishlist?.items.length === 0 ? (
          <div className="wh-wishlist-state"><p>Вишлист пуст</p>{isOwnProfile && <Link to="/wishlist" className="wh-button wh-button--quiet">Добавить желание</Link>}</div>
        ) : (
          <div className="wh-wishlist-grid">
            {wishlist?.items.slice(0, 3).map((item) => <WishlistItemCard key={item.id} item={item} isOwner={isOwnProfile} />)}
          </div>
        )}
      </section>
    </Layout>
  )
}
