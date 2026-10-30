import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { ItemImage } from '@/components/ItemImage'
import { ItemReservation } from '@/components/ItemReservation'
import { WishlistItemModal } from '@/components/WishlistItemModal'
import { useWishlist, useDeleteItem } from '@/api/useWishlist'
import { useProfile } from '@/api/useProfile'
import { useAuthStore } from '@/store/auth'
import type { WishlistItem } from '@/types/wishlist'

export function WishlistPage() {
  const { username } = useParams<{ username?: string }>()
  const currentUser = useAuthStore((state) => state.user)
  const { data: profile, isLoading: profileLoading } = useProfile(username)
  const isOwnWishlist = !username || (!!profile && profile.id === currentUser?.id)
  const { data: wishlist, isLoading, isError } = useWishlist(profile?.id, !username || !!profile)
  const [editing, setEditing] = useState<WishlistItem | 'new' | null>(null)

  if (isLoading || profileLoading) {
    return <Layout><div className="text-center py-8">Загрузка...</div></Layout>
  }

  return (
    <Layout>
      <div className="flex justify-between items-center mb-6 gap-3">
        {isOwnWishlist ? (
          <h1 className="text-2xl font-bold">Мой вишлист</h1>
        ) : profile ? (
          <Link to={`/profile/${profile.username}`} className="flex items-center hover:opacity-80">
            <UserAvatar displayName={profile.displayName} avatarUrl={profile.avatarUrl} size="md" />
            <div className="ml-3">
              <h1 className="text-xl font-bold">Вишлист {profile.displayName}</h1>
              <p className="text-sm text-gray-500">@{profile.username}</p>
            </div>
          </Link>
        ) : <h1 className="text-2xl font-bold">Пользователь не найден</h1>}
        {isOwnWishlist ? (
          <button onClick={() => setEditing('new')} className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700">Добавить</button>
        ) : profile ? (
          <Link to={`/profile/${profile.username}`} className="text-gray-600 hover:text-gray-900">← К профилю</Link>
        ) : null}
      </div>

      {isError ? <p role="alert" className="text-red-600">Не удалось загрузить вишлист.</p>
        : wishlist?.hidden ? <p className="text-gray-600 text-center py-8">Вишлист скрыт пользователем</p>
        : wishlist?.items.length === 0 ? (
          <p className="text-gray-600 text-center py-8">{isOwnWishlist ? 'Ваш вишлист пуст' : 'Вишлист пользователя пуст'}</p>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {wishlist?.items.map((item) => <WishlistItemCard key={item.id} item={item} isOwner={isOwnWishlist} onEdit={() => setEditing(item)} />)}
          </div>
        )}
      {isOwnWishlist && editing && <WishlistItemModal item={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </Layout>
  )
}

function WishlistItemCard({ item, isOwner, onEdit }: { item: WishlistItem; isOwner: boolean; onEdit: () => void }) {
  const deleteItem = useDeleteItem()
  return (
    <article className="bg-white rounded-lg shadow overflow-hidden hover:shadow-lg transition-shadow">
      <a href={item.url} target="_blank" rel="noopener noreferrer" className="block">
        <ItemImage imageUrl={item.imageUrl} name={item.name} />
        <div className="p-4">
          <h3 className="font-semibold break-words">{item.name}</h3>
          <p className="text-gray-600">{item.price == null ? 'Цена не указана' : `${item.price} ${item.currency}`}</p>
          <p className="text-sm text-gray-500">{item.source === 'YandexMarket' ? 'Яндекс Маркет' : item.source}</p>
        </div>
      </a>
      {isOwner ? (
        <div className="px-4 pb-4 text-sm">
          <div className="flex items-center gap-3">
            <button onClick={onEdit} className="text-blue-600 hover:text-blue-700">Редактировать</button>
            <button onClick={() => { if (confirm('Удалить этот товар?')) deleteItem.mutate(item.id) }} disabled={deleteItem.isPending} className="text-red-600 hover:text-red-700 disabled:opacity-50">Удалить</button>
          </div>
          {deleteItem.isError && <p role="alert" className="text-red-600 mt-2">Не удалось удалить товар.</p>}
        </div>
      ) : <div className="px-4 pb-4"><ItemReservation item={item} isOwner={false} /></div>}
    </article>
  )
}
