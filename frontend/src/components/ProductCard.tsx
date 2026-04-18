import type { WishlistItem } from '@/types/wishlist'

interface ProductCardProps {
  item: WishlistItem
  onDelete?: () => void
  onReserve?: () => void
  onCancelReservation?: () => void
  showActions?: boolean
}

export function ProductCard({ item, onDelete, onReserve, onCancelReservation, showActions = true }: ProductCardProps) {
  const isProcessing = item.isProcessing
  const hasError = item.processingError

  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
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

        {/* Источник */}
        <p className="text-sm text-gray-500">{item.product.source}</p>

        {/* Ошибка парсинга */}
        {hasError && (
          <div className="mt-2 p-2 bg-yellow-50 border border-yellow-200 rounded">
            <p className="text-sm text-yellow-800">
              Не удалось загрузить данные. <a href={item.product.url} target="_blank" rel="noopener noreferrer" className="underline">Открыть товар</a>
            </p>
          </div>
        )}

        {/* Резервация */}
        {item.isReserved && (
          <div className="mt-2 p-2 bg-green-100 rounded">
            <p className="text-sm text-green-800">
              Зарезервировано: {item.reservedByDisplayName}
            </p>
          </div>
        )}

        {/* Действия */}
        {showActions && !isProcessing && (
          <div className="mt-3 flex gap-2">
            {onDelete && (
              <button
                onClick={onDelete}
                className="text-red-600 hover:text-red-700 text-sm"
              >
                Удалить
              </button>
            )}
            {onReserve && !item.isReserved && (
              <button
                onClick={onReserve}
                className="px-3 py-1 bg-green-600 text-white text-sm rounded-md hover:bg-green-700"
              >
                Зарезервировать
              </button>
            )}
            {onCancelReservation && item.isReserved && item.reservedById && (
              <button
                onClick={onCancelReservation}
                className="text-green-600 hover:underline text-sm"
              >
                Отменить
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
