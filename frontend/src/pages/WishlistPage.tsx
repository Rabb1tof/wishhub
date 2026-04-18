import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { useWishlist, useAddItem, useDeleteItem, useRefreshItem } from '@/api/useWishlist'
import { useProfile } from '@/api/useProfile'
import type { WishlistItem } from '@/types/wishlist'

export function WishlistPage() {
  const { username } = useParams<{ username?: string }>()
  const isOwnWishlist = !username
  const { data: profile } = useProfile(username)
  const { data: wishlist, isLoading } = useWishlist(profile?.id)
  const [showAddModal, setShowAddModal] = useState(false)

  if (isLoading) {
    return (
      <Layout>
        <div className="text-center py-8">Загрузка...</div>
      </Layout>
    )
  }

  return (
    <Layout>
      <div className="flex justify-between items-center mb-6">
        {isOwnWishlist ? (
          <h1 className="text-2xl font-bold">Мой вишлист</h1>
        ) : profile ? (
          <Link
            to={`/profile/${profile.username}`}
            className="flex items-center hover:opacity-80"
          >
            <UserAvatar displayName={profile.displayName} avatarUrl={profile.avatarUrl} size="md" />
            <div className="ml-3">
              <h1 className="text-xl font-bold">Вишлист {profile.displayName}</h1>
              <p className="text-sm text-gray-500">@{profile.username}</p>
            </div>
          </Link>
        ) : (
          <h1 className="text-2xl font-bold">Вишлист</h1>
        )}
        {isOwnWishlist ? (
          <button
            onClick={() => setShowAddModal(true)}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
          >
            Добавить
          </button>
        ) : profile ? (
          <Link
            to={`/profile/${profile.username}`}
            className="text-gray-600 hover:text-gray-900"
          >
            ← К профилю
          </Link>
        ) : null}
      </div>

      {wishlist?.hidden ? (
        <p className="text-gray-600 text-center py-8">Вишлист скрыт пользователем</p>
      ) : wishlist?.items.length === 0 ? (
        <p className="text-gray-600 text-center py-8">
          {isOwnWishlist ? 'Ваш вишлист пуст' : 'Вишлист пользователя пуст'}
        </p>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {wishlist?.items.map((item) => (
            <WishlistItemCard key={item.id} item={item} isOwner={isOwnWishlist} />
          ))}
        </div>
      )}

      {isOwnWishlist && showAddModal && <AddItemModal onClose={() => setShowAddModal(false)} />}
    </Layout>
  )
}

function WishlistItemCard({ item, isOwner }: { item: WishlistItem; isOwner: boolean }) {
  const deleteItem = useDeleteItem()
  const refreshItem = useRefreshItem()
  const isProcessing = item.isProcessing
  const hasError = item.processingError

  const handleDelete = async (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (confirm('Удалить этот товар?')) {
      await deleteItem.mutateAsync(item.id)
    }
  }

  const handleRefresh = async (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    await refreshItem.mutateAsync(item.id)
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
      {/* Изображение или placeholder */}
      {isProcessing ? (
        <div className="w-full h-48 bg-gray-100 flex items-center justify-center">
          <div className="w-8 h-8 border-2 border-gray-300 border-t-gray-600 rounded-full animate-spin" />
        </div>
      ) : item.product.imageProxyUrl ? (
        <img
          src={item.product.imageProxyUrl}
          alt={item.product.name}
          className="w-full h-48 object-cover"
        />
      ) : (
        <div className="w-full h-48 bg-gray-100 flex items-center justify-center">
          <span className="text-gray-400 text-sm">Нет изображения</span>
        </div>
      )}

      <div className="p-4">
        {/* Название */}
        <h3 className="font-semibold">
          {isProcessing ? (
            <span className="text-gray-500 flex items-center gap-2">
              <span className="w-4 h-4 border-2 border-gray-300 border-t-gray-600 rounded-full animate-spin inline-block" />
              {item.customName || item.product.name}
            </span>
          ) : (
            item.customName || item.product.name
          )}
        </h3>

        {/* Цена */}
        <p className="text-gray-600">
          {isProcessing ? (
            <span className="text-gray-400">Загрузка цены...</span>
          ) : item.product.price ? (
            `${item.product.price} ${item.product.currency}`
          ) : (
            <span className="text-gray-400">Цена не указана</span>
          )}
        </p>

        <p className="text-sm text-gray-500">{sourceName}</p>

        {/* Ошибка парсинга */}
        {hasError && (
          <div className="mt-2 p-2 bg-yellow-50 border border-yellow-200 rounded">
            <p className="text-sm text-yellow-800">
              Не удалось загрузить данные товара
            </p>
          </div>
        )}

        {/* Кнопки для владельца */}
        {isOwner && !isProcessing && (
          <div className="mt-3 flex items-center gap-3 text-sm">
            <button
              onClick={handleRefresh}
              disabled={refreshItem.isPending}
              className="text-blue-600 hover:text-blue-700 disabled:opacity-50"
              title="Обновить цену и картинку"
            >
              {refreshItem.isPending ? 'Обновление...' : '↻ Обновить'}
            </button>
            <button
              onClick={handleDelete}
              disabled={deleteItem.isPending}
              className="text-red-600 hover:text-red-700"
            >
              Удалить
            </button>
          </div>
        )}
      </div>
    </a>
  )
}

function AddItemModal({ onClose }: { onClose: () => void }) {
  const [url, setUrl] = useState('')
  const [customName, setCustomName] = useState('')
  const [error, setError] = useState('')
  const addItem = useAddItem()

  const handleAdd = async () => {
    setError('')
    try {
      // Добавляем товар — парсинг происходит в фоне
      await addItem.mutateAsync({ url, customName: customName || undefined })
      // Сразу закрываем модалку и возвращаемся к вишлисту
      // Товар появится в списке со статусом "Загрузка..."
      onClose()
    } catch (err: any) {
      const message = err.response?.data?.error || err.response?.data?.message || 'Не удалось добавить товар'
      setError(message)
    }
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 max-w-md w-full">
        <h2 className="text-xl font-bold mb-4">Добавить товар</h2>

        {error && <div className="mb-3 text-sm text-red-600">{error}</div>}

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Ссылка на товар</label>
            <input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://..."
              className="mt-1 block w-full px-3 py-2 border rounded-md"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Название (опционально)</label>
            <input
              type="text"
              value={customName}
              onChange={(e) => setCustomName(e.target.value)}
              placeholder="Моё название"
              className="mt-1 block w-full px-3 py-2 border rounded-md"
            />
          </div>

          <div className="flex justify-end space-x-3">
            <button
              onClick={onClose}
              className="px-4 py-2 text-gray-600 hover:text-gray-800"
            >
              Отмена
            </button>
            <button
              onClick={handleAdd}
              disabled={!url || addItem.isPending}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {addItem.isPending ? 'Добавление...' : 'Добавить'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
