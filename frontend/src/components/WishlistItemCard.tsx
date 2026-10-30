import { Link } from 'react-router-dom'
import { useDeleteItem } from '@/api/useWishlist'
import { formatItemSource } from '@/utils/wishlist'
import type { WishlistItem } from '@/types/wishlist'
import { ItemImage } from './ItemImage'
import { ItemReservation } from './ItemReservation'
import './wishlist.css'

interface WishlistItemCardProps {
  item: WishlistItem
  isOwner: boolean
  onEdit?: () => void
  wishlistPath?: string
}

export function WishlistItemCard({ item, isOwner, onEdit, wishlistPath }: WishlistItemCardProps) {
  const deleteItem = useDeleteItem()
  const content = (
    <>
      <ItemImage imageUrl={item.imageUrl} name={item.name} className="wh-item-image" />
      <div className="wh-item-body">
        <p className="wh-item-source">{formatItemSource(item.source, item.url)}</p>
        <h3 className="wh-item-name">{item.name}</h3>
        <p className={`wh-item-price${item.price === null ? ' wh-item-price--missing' : ''}`}>
          {item.price === null ? 'Цена не указана' : `${item.price.toLocaleString('ru-RU', { maximumFractionDigits: 20 })} ${item.currency}`}
        </p>
      </div>
    </>
  )

  return (
    <article className={`wh-item-card${wishlistPath ? ' wh-item-card--compact' : ''}`}>
      {wishlistPath ? (
        <Link to={wishlistPath} className="wh-item-link">{content}</Link>
      ) : (
        <a href={item.url} target="_blank" rel="noopener noreferrer" className="wh-item-link">{content}</a>
      )}
      {!wishlistPath && isOwner && onEdit && (
        <div className="wh-item-footer">
          <div className="wh-item-actions">
            <button type="button" onClick={onEdit} className="wh-button wh-button--quiet">Редактировать</button>
            <button type="button" onClick={() => { if (confirm('Удалить этот товар?')) deleteItem.mutate(item.id) }} disabled={deleteItem.isPending} className="wh-button wh-button--danger">Удалить</button>
          </div>
          {deleteItem.isError && <p role="alert" className="wh-wishlist-error">Не удалось удалить товар.</p>}
        </div>
      )}
      {!wishlistPath && !isOwner && <div className="wh-item-footer"><ItemReservation item={item} isOwner={false} /></div>}
    </article>
  )
}
