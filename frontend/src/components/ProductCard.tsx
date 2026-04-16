import type { WishlistItem } from '@/types/wishlist'

interface ProductCardProps {
  item: WishlistItem
  onDelete?: () => void
  onReserve?: () => void
  onCancelReservation?: () => void
  showActions?: boolean
}

export function ProductCard({ item, onDelete, onReserve, onCancelReservation, showActions = true }: ProductCardProps) {
  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
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
        <p className="text-sm text-gray-500">{item.product.source}</p>

        {item.isReserved && (
          <div className="mt-2 p-2 bg-green-100 rounded">
            <p className="text-sm text-green-800">
              Зарезервировано: {item.reservedByDisplayName}
            </p>
          </div>
        )}

        {showActions && (
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
