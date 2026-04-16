import { useParams, Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { ShareProfileButton } from '@/components/ShareProfileButton'
import { useProfile } from '@/api/useProfile'
import { useWishlist, useReserveItem } from '@/api/useWishlist'
import { useSendRequest, useRemoveFriend } from '@/api/useFriends'
import { useAuthStore } from '@/store/auth'
import type { WishlistItem } from '@/types/wishlist'

export function ProfilePage() {
  const { username } = useParams<{ username: string }>()
  const { data: profile, isLoading: profileLoading } = useProfile(username)
  const { data: wishlist, isLoading: wishlistLoading } = useWishlist(profile?.id)
  const sendRequest = useSendRequest()
  const removeFriend = useRemoveFriend()
  const currentUser = useAuthStore((s) => s.user)
  const isOwnProfile = profile?.id === currentUser?.id

  if (profileLoading) {
    return (
      <Layout>
        <div className="text-center py-8">Загрузка...</div>
      </Layout>
    )
  }

  if (!profile) {
    return (
      <Layout>
        <div className="text-center py-8">Пользователь не найден</div>
      </Layout>
    )
  }

  const friendshipStatus = profile.friendshipStatus

  return (
    <Layout>
      <div className="max-w-4xl mx-auto">
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <div className="flex items-start justify-between">
            <div className="flex items-center">
              <UserAvatar displayName={profile.displayName} avatarUrl={profile.avatarUrl} size="lg" />
              <div className="ml-4">
                <h1 className="text-2xl font-bold">{profile.displayName}</h1>
                <p className="text-gray-500">@{profile.username}</p>
                {profile.bio && <p className="mt-2 text-gray-700">{profile.bio}</p>}
              </div>
            </div>

            <div className="flex items-center gap-3">
              {isOwnProfile && (
                <ShareProfileButton username={profile.username} displayName={profile.displayName} />
              )}
              {!isOwnProfile && friendshipStatus === null && (
                <button
                  onClick={() => sendRequest.mutateAsync(profile.id)}
                  disabled={sendRequest.isPending}
                  className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
                >
                  {sendRequest.isPending ? 'Отправка...' : 'Добавить в друзья'}
                </button>
              )}
              {!isOwnProfile && friendshipStatus === 'pending' && (
                <span className="px-4 py-2 bg-yellow-100 text-yellow-800 rounded-md inline-block">
                  Заявка отправлена
                </span>
              )}
              {!isOwnProfile && friendshipStatus === 'accepted' && (
                <>
                  <span className="px-4 py-2 bg-green-100 text-green-800 rounded-md inline-block">
                    В друзьях
                  </span>
                  <Link
                    to={`/messages?user=${profile.id}`}
                    className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 inline-block"
                  >
                    Написать сообщение
                  </Link>
                  <button
                    onClick={() => {
                      if (confirm('Удалить из друзей?')) removeFriend.mutateAsync(profile.id)
                    }}
                    disabled={removeFriend.isPending}
                    className="px-4 py-2 bg-red-100 text-red-700 rounded-md hover:bg-red-200 disabled:opacity-50"
                  >
                    Удалить из друзей
                  </button>
                </>
              )}
            </div>
          </div>
        </div>

        {wishlist?.hidden ? (
          <>
            <h2 className="text-xl font-bold mb-4">Вишлист</h2>
            <p className="text-gray-500 italic">Вишлист скрыт пользователем</p>
          </>
        ) : (
          <>
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold">
                Вишлист ({profile.wishlistItemCount})
              </h2>
              {/* Ссылка на полный вишлист */}
              {!isOwnProfile && profile.wishlistItemCount > 3 && (
                <Link
                  to={`/wishlist/${profile.username}`}
                  className="text-blue-600 hover:text-blue-700 text-sm"
                >
                  Показать все →
                </Link>
              )}
            </div>

            {wishlistLoading ? (
              <div className="text-center py-8">Загрузка...</div>
            ) : wishlist?.items.length === 0 ? (
              <p className="text-gray-600">Вишлист пуст</p>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {/* Показываем только первые 3 товара */}
                {wishlist?.items.slice(0, 3).map((item) => (
                  <PublicWishlistItem key={item.id} item={item} isOwner={isOwnProfile} />
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </Layout>
  )
}

function PublicWishlistItem({ item, isOwner }: { item: WishlistItem; isOwner?: boolean }) {
  const reserveItem = useReserveItem()

  const handleReserve = async (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    await reserveItem.mutateAsync(item.id)
  }

  // Форматируем название источника
  const sourceName = item.product.source === 'Wildberries' ? 'Wildberries'
    : item.product.source === 'Ozon' ? 'Ozon'
    : item.product.source === 'YandexMarket' ? 'Яндекс Маркет'
    : item.product.source

  return (
    <a
      href={item.product.url}
      target="_blank"
      rel="noopener noreferrer"
      className="bg-white rounded-lg shadow overflow-hidden block hover:shadow-lg transition-shadow"
    >
      {item.product.imageProxyUrl && (
        <img
          src={item.product.imageProxyUrl}
          alt={item.product.name}
          className="w-full h-48 object-cover"
        />
      )}
      <div className="p-4">
        <h3 className="font-semibold">{item.customName || item.product.name}</h3>
        <p className="text-gray-600">
          {item.product.price} {item.product.currency}
        </p>
        <p className="text-sm text-gray-500">{sourceName}</p>

        {/* Владелец не видит статус резервирования */}
        {!isOwner && (
          item.isReserved ? (
            <p className="mt-2 text-sm text-green-600">
              Зарезервировано
            </p>
          ) : (
            <button
              onClick={handleReserve}
              disabled={reserveItem.isPending}
              className="mt-2 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50"
            >
              Зарезервировать
            </button>
          )
        )}
      </div>
    </a>
  )
}
