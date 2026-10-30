import { Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { WishlistItemCard } from '@/components/WishlistItemCard'
import { useFriends } from '@/api/useFriends'
import { useWishlist } from '@/api/useWishlist'

export function FeedPage() {
  const { data: friends, isLoading: friendsLoading, isError, refetch } = useFriends()

  return (
    <Layout>
      <section className="wh-wishlist">
        <header className="wh-wishlist-heading">
          <div><h1 className="wh-page-heading">Лента друзей</h1><p className="wh-muted wh-wishlist-subline">Идеи подарков для близких</p></div>
          <Link to="/friends" className="wh-button wh-button--quiet">Друзья</Link>
        </header>
        {friendsLoading ? (
          <div className="wh-wishlist-state" role="status"><p>Загрузка...</p></div>
        ) : isError ? (
          <div className="wh-wishlist-state"><p role="alert">Не удалось загрузить друзей.</p><button type="button" className="wh-button wh-button--quiet" onClick={() => void refetch()}>Повторить</button></div>
        ) : !friends?.length ? (
          <div className="wh-wishlist-state">
            <h2>У вас пока нет друзей.</h2>
            <p className="wh-muted">Добавьте друзей, чтобы видеть их вишлисты в ленте.</p>
            <Link to="/friends" className="wh-button wh-button--quiet">Найти друзей</Link>
          </div>
        ) : (
          <div className="wh-friends-feed">{friends.map((friend) => <FriendWishlist key={friend.id} userId={friend.user.id} user={friend.user} />)}</div>
        )}
      </section>
    </Layout>
  )
}

function FriendWishlist({ userId, user }: { userId: string; user: { username: string; displayName: string; avatarUrl?: string } }) {
  const { data: wishlist, isLoading, isError, refetch } = useWishlist(userId)
  if (wishlist?.hidden) return null

  return (
    <section className="wh-friend-shelf">
      <header className="wh-wishlist-section-heading">
        <Link to={`/profile/${user.username}`} className="wh-person-link">
          <UserAvatar displayName={user.displayName} avatarUrl={user.avatarUrl} size="md" />
          <div><h2>{user.displayName}</h2><p className="wh-muted">@{user.username}</p></div>
        </Link>
        {!!wishlist && wishlist.items.length > 4 && <Link to={`/wishlist/${user.username}`} className="wh-button wh-button--quiet">Весь вишлист</Link>}
      </header>
      {isLoading ? (
        <div className="wh-wishlist-state" role="status"><p>Загрузка...</p></div>
      ) : isError ? (
        <div className="wh-wishlist-state"><p role="alert">Не удалось загрузить вишлист.</p><button type="button" className="wh-button wh-button--quiet" onClick={() => void refetch()}>Повторить</button></div>
      ) : wishlist?.items.length === 0 ? (
        <div className="wh-wishlist-state"><p>Вишлист пока пуст</p></div>
      ) : (
        <div className="wh-wishlist-grid wh-wishlist-grid--compact">
          {wishlist?.items.slice(0, 4).map((item) => <WishlistItemCard key={item.id} item={item} isOwner={false} wishlistPath={`/wishlist/${user.username}`} />)}
        </div>
      )}
    </section>
  )
}
