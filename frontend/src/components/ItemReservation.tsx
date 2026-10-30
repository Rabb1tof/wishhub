import type { MouseEvent } from 'react'
import { useReserveItem, useCancelReservation } from '@/api/useWishlist'
import type { WishlistItem } from '@/types/wishlist'

export function ItemReservation({ item, isOwner }: { item: WishlistItem; isOwner: boolean }) {
  const reserveItem = useReserveItem()
  const cancelReservation = useCancelReservation()
  if (isOwner) return null

  const pending = reserveItem.isPending || cancelReservation.isPending
  const handleReservation = (event: MouseEvent<HTMLButtonElement>) => {
    event.preventDefault()
    event.stopPropagation()
    reserveItem.reset()
    cancelReservation.reset()
    if (item.isReservedByMe) cancelReservation.mutate(item.id)
    else reserveItem.mutate(item.id)
  }

  return (
    <div className="mt-2">
      {item.isReserved && (
        <p className="text-sm text-green-600">
          {item.isReservedByMe
            ? 'Зарезервировано вами'
            : item.reservedBy
              ? `Зарезервировано: ${item.reservedBy.displayName}`
              : 'Зарезервировано'}
        </p>
      )}
      {(!item.isReserved || item.isReservedByMe) && (
        <button
          type="button"
          onClick={handleReservation}
          disabled={pending}
          className="mt-2 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50"
        >
          {pending ? 'Сохранение...' : item.isReservedByMe ? 'Отменить резервирование' : 'Зарезервировать'}
        </button>
      )}
      {(reserveItem.isError || cancelReservation.isError) && (
        <p role="alert" className="mt-2 text-sm text-red-600">Не удалось изменить резервирование. Попробуйте ещё раз.</p>
      )}
    </div>
  )
}
