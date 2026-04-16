import { Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { useFriends } from '@/api/useFriends'
import { useWishlist } from '@/api/useWishlist'

export function FeedPage() {
  const { data: friends, isLoading: friendsLoading } = useFriends()

  if (friendsLoading) {
    return (
      <Layout>
        <div className="text-center py-8">Загрузка...</div>
      </Layout>
    )
  }

  if (!friends?.length) {
    return (
      <Layout>
        <div className="text-center py-8">
          <p className="text-gray-600">У вас пока нет друзей.</p>
          <p className="text-gray-500 mt-2">
            Добавьте друзей, чтобы видеть их вишлисты в ленте.
          </p>
        </div>
      </Layout>
    )
  }

  return (
    <Layout>
      <h1 className="text-2xl font-bold mb-6">Лента друзей</h1>
      <div className="space-y-8">
        {friends.map((friend) => (
          <FriendWishlist key={friend.id} userId={friend.user.id} user={friend.user} />
        ))}
      </div>
    </Layout>
  )
}

function FriendWishlist({ userId, user }: { userId: string; user: { username: string; displayName: string; avatarUrl?: string } }) {
  const { data: wishlist, isLoading } = useWishlist(userId)

  if (isLoading) return null
  if (!wishlist?.items.length || wishlist.hidden) return null

  const hasMore = wishlist.items.length > 4

  return (
    <div className="bg-white rounded-lg shadow p-6">
      <div className="flex items-center justify-between mb-4">
        <Link
          to={`/profile/${user.username}`}
          className="flex items-center hover:opacity-80"
        >
          <UserAvatar displayName={user.displayName} avatarUrl={user.avatarUrl} size="md" />
          <div className="ml-3">
            <p className="font-semibold">{user.displayName}</p>
            <p className="text-sm text-gray-500">@{user.username}</p>
          </div>
        </Link>
        {hasMore && (
          <Link
            to={`/wishlist/${user.username}`}
            className="text-sm text-blue-600 hover:text-blue-700"
          >
            Весь вишлист →
          </Link>
        )}
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {wishlist.items.slice(0, 4).map((item) => (
          <Link
            key={item.id}
            to={`/wishlist/${user.username}`}
            className="border rounded-lg overflow-hidden block hover:shadow-md transition-shadow"
          >
            {item.product.imageProxyUrl && (
              <img
                src={item.product.imageProxyUrl}
                alt={item.product.name}
                className="w-full h-32 object-cover"
              />
            )}
            <div className="p-2">
              <p className="text-sm font-medium truncate">{item.customName || item.product.name}</p>
              <p className="text-sm text-gray-600">
                {item.product.price} {item.product.currency}
              </p>
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}
