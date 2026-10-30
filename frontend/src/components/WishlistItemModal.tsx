import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { useAddItem, useUpdateItem } from '@/api/useWishlist'
import { resolveMetadata } from '@/api/metadata'
import { ItemImage } from '@/components/ItemImage'
import { isHttpUrl } from '@/utils/url'
import type { WishlistItem } from '@/types/wishlist'

type SuggestedField = 'name' | 'imageUrl'
type Found = { name: string | null; imageUrl: string | null }

export function WishlistItemModal({ item, onClose }: { item?: WishlistItem; onClose: () => void }) {
  const [draft, setDraft] = useState(() => ({
    url: item?.url ?? '',
    name: item?.name ?? '',
    imageUrl: item?.imageUrl ?? '',
    price: item?.price == null ? '' : String(item.price),
    currency: item?.currency || 'RUB',
    manual: { name: false, imageUrl: false },
    automatic: { name: false, imageUrl: false },
  }))
  const [error, setError] = useState('')
  const [fetching, setFetching] = useState(false)
  const [message, setMessage] = useState('')
  const [found, setFound] = useState<Found | null>(null)
  const request = useRef<AbortController | null>(null)
  const mounted = useRef(true)
  const addItem = useAddItem()
  const updateItem = useUpdateItem()
  const saving = addItem.isPending || updateItem.isPending

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      request.current?.abort()
    }
  }, [])

  const validUrl = draft.url.trim().length <= 2000 && isHttpUrl(draft.url)
  const validName = !!draft.name.trim() && draft.name.trim().length <= 500
  const validImage = !draft.imageUrl.trim() || isHttpUrl(draft.imageUrl)
  const validPrice = !draft.price.trim() || (Number.isFinite(Number(draft.price)) && Number(draft.price) >= 0)
  const valid = validUrl && validName && validImage && validPrice

  const cancelRequest = () => {
    request.current?.abort()
    request.current = null
    setFetching(false)
  }

  const changeUrl = (url: string) => {
    cancelRequest()
    setFound(null)
    setMessage('')
    setDraft((current) => ({
      ...current, url,
      name: current.automatic.name ? '' : current.name,
      imageUrl: current.automatic.imageUrl ? '' : current.imageUrl,
      automatic: { name: false, imageUrl: false },
    }))
  }

  const changeSuggestedField = (field: SuggestedField, value: string) => {
    setDraft((current) => ({
      ...current, [field]: value,
      manual: { ...current.manual, [field]: true },
      automatic: { ...current.automatic, [field]: false },
    }))
  }

  const getMetadata = async () => {
    if (!validUrl || request.current) return
    const controller = new AbortController()
    request.current = controller
    setFetching(true)
    setFound(null)
    setMessage('')
    try {
      const result = await resolveMetadata(draft.url.trim(), controller.signal)
      if (request.current !== controller || controller.signal.aborted) return
      if (result.status === 'unavailable' || result.status === 'unsupported') {
        setMessage('Не удалось получить данные. Заполните поля вручную и сохраните товар.')
        return
      }
      const suggestion: Found = {
        name: result.name?.trim() || null,
        imageUrl: result.imageUrl && isHttpUrl(result.imageUrl) ? result.imageUrl.trim() : null,
      }
      setFound(suggestion)
      setDraft((current) => {
        if (current.url !== draft.url) return current
        const next = { ...current, automatic: { ...current.automatic } }
        for (const field of ['name', 'imageUrl'] as const) {
          if (suggestion[field] && !current[field].trim() && !current.manual[field]) {
            next[field] = suggestion[field]
            next.automatic[field] = true
          }
        }
        return next
      })
      setMessage(suggestion.name || suggestion.imageUrl
        ? 'Найденные данные заполнили только пустые поля. Проверьте карточку перед сохранением.'
        : 'Данные не найдены. Заполните поля вручную и сохраните товар.')
    } catch {
      if (request.current === controller && !controller.signal.aborted) {
        setMessage('Не удалось получить данные. Заполните поля вручную и сохраните товар.')
      }
    } finally {
      if (request.current === controller) {
        request.current = null
        setFetching(false)
      }
    }
  }

  const applyFound = () => {
    if (!found) return
    setDraft((current) => ({
      ...current,
      name: found.name ?? current.name,
      imageUrl: found.imageUrl ?? current.imageUrl,
      manual: { name: !!found.name || current.manual.name, imageUrl: !!found.imageUrl || current.manual.imageUrl },
      automatic: { name: !found.name && current.automatic.name, imageUrl: !found.imageUrl && current.automatic.imageUrl },
    }))
  }

  const save = async (event: FormEvent) => {
    event.preventDefault()
    if (!valid || saving) return
    setError('')
    cancelRequest()
    const data = {
      url: draft.url.trim(),
      name: draft.name.trim(),
      imageUrl: draft.imageUrl.trim() || null,
      price: draft.price.trim() ? Number(draft.price) : null,
      currency: draft.currency.trim() || 'RUB',
    }
    try {
      if (item) await updateItem.mutateAsync({ id: item.id, data })
      else await addItem.mutateAsync(data)
      if (mounted.current) onClose()
    } catch (err) {
      const response = isAxiosError<{ error?: string; message?: string; detail?: string }>(err) ? err.response : undefined
      setError(response?.status === 409 ? 'Товар с такой ссылкой уже есть в вашем вишлисте.'
        : response?.data?.detail || response?.data?.error || response?.data?.message || 'Не удалось сохранить товар. Попробуйте ещё раз.')
    }
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
      <section role="dialog" aria-modal="true" aria-labelledby="item-modal-title" className="bg-white rounded-lg p-6 max-w-md w-full max-h-[90dvh] overflow-y-auto">
        <h2 id="item-modal-title" className="text-xl font-bold mb-4">{item ? 'Редактировать товар' : 'Добавить товар'}</h2>
        {error && <p role="alert" className="mb-3 text-sm text-red-600">{error}</p>}
        <form onSubmit={save} noValidate>
          <fieldset disabled={saving} className="space-y-4">
            <div>
              <label htmlFor="item-url" className="block text-sm font-medium text-gray-700">Ссылка на товар</label>
              <input id="item-url" type="url" required autoFocus maxLength={2000} value={draft.url} onChange={(e) => changeUrl(e.target.value)} placeholder="https://..." className="mt-1 block w-full px-3 py-2 border rounded-md" />
              {draft.url && !validUrl && <p className="text-sm text-red-600">Введите HTTP(S) ссылку длиной до 2000 символов.</p>}
            </div>
            <button type="button" onClick={getMetadata} disabled={!validUrl || fetching} className="px-4 py-2 border rounded-md text-blue-600 disabled:opacity-50">{fetching ? 'Получение данных...' : 'Получить данные'}</button>
            {message && <p role="status" className="text-sm text-gray-600">{message}</p>}
            {found && (found.name || found.imageUrl) && (
              <div className="p-3 bg-blue-50 rounded-md text-sm space-y-2">
                {found.name && <p className="break-words">Найдено: {found.name}</p>}
                {found.imageUrl && <p>Найдено фото товара</p>}
                <button type="button" onClick={applyFound} className="text-blue-600 underline">Применить найденное</button>
                <p className="text-gray-600">Заменить имя и фото найденными значениями.</p>
              </div>
            )}
            <div>
              <label htmlFor="item-name" className="block text-sm font-medium text-gray-700">Название</label>
              <input id="item-name" required maxLength={500} value={draft.name} onChange={(e) => changeSuggestedField('name', e.target.value)} className="mt-1 block w-full px-3 py-2 border rounded-md" />
              {draft.name && !validName && <p className="text-sm text-red-600">Введите непустое название длиной до 500 символов.</p>}
            </div>
            <div>
              <label htmlFor="item-image" className="block text-sm font-medium text-gray-700">Фото: URL (необязательно)</label>
              <input id="item-image" type="url" value={draft.imageUrl} onChange={(e) => changeSuggestedField('imageUrl', e.target.value)} className="mt-1 block w-full px-3 py-2 border rounded-md" />
              {!validImage && <p className="text-sm text-red-600">Введите HTTP(S) ссылку на фото или очистите поле.</p>}
              {draft.imageUrl && <button type="button" onClick={() => changeSuggestedField('imageUrl', '')} className="text-sm text-red-600 mt-1">Удалить фото</button>}
              {draft.imageUrl && <ItemImage imageUrl={draft.imageUrl} name="Превью товара" className="w-full h-32 object-contain mt-2" />}
            </div>
            <div className="flex gap-3">
              <div className="flex-1 min-w-0">
                <label htmlFor="item-price" className="block text-sm font-medium text-gray-700">Цена (необязательно)</label>
                <input id="item-price" type="number" min="0" step="any" value={draft.price} onChange={(e) => setDraft((current) => ({ ...current, price: e.target.value }))} className="mt-1 block w-full px-3 py-2 border rounded-md" />
                {!validPrice && <p className="text-sm text-red-600">Цена должна быть числом не меньше нуля.</p>}
              </div>
              <div className="w-24">
                <label htmlFor="item-currency" className="block text-sm font-medium text-gray-700">Валюта</label>
                <input id="item-currency" value={draft.currency} onChange={(e) => setDraft((current) => ({ ...current, currency: e.target.value }))} className="mt-1 block w-full px-3 py-2 border rounded-md" />
              </div>
            </div>
            <div className="flex justify-end gap-3">
              <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600">Отмена</button>
              <button type="submit" disabled={!valid || saving} className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50">{saving ? 'Сохранение...' : 'Сохранить'}</button>
            </div>
          </fieldset>
        </form>
      </section>
    </div>
  )
}
