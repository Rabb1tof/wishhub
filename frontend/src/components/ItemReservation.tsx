import type { MouseEvent } from 'react'
import { useReserveItem, useCancelReservation } from '@/api/useWishlist'
import type { WishlistItem } from '@/types/wishlist'
import './wishlist.css'

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
    <div className="wh-reservation">
      {item.isReserved && (
        <p className="wh-reservation-status">
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
          className="wh-button wh-button--quiet"
        >
          {pending ? 'Сохранение...' : item.isReservedByMe ? 'Отменить резервирование' : 'Зарезервировать'}
        </button>
      )}
      {(reserveItem.isError || cancelReservation.isError) && (
        <p role="alert" className="wh-wishlist-error">Не удалось изменить резервирование. Попробуйте ещё раз.</p>
      )}
    </div>
  )
}
