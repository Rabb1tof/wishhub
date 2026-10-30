import { useEffect, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { isAxiosError } from 'axios'
import { useAddItem, useUpdateItem } from '@/api/useWishlist'
import { resolveMetadata } from '@/api/metadata'
import { uploadWishlistImage } from '@/api/wishlistImages'
import { isManagedImageUrl, isWishlistImageUrl } from '@/utils/wishlistImages'
import './wishlist-modal.css'
import { ItemImage } from '@/components/ItemImage'
import { isHttpUrl } from '@/utils/url'
import type { WishlistItem } from '@/types/wishlist'

type SuggestedField = 'name' | 'imageUrl'
type Found = { name: string | null; imageUrl: string | null }
type Upload = { file: File | null; preview: string | null; phase: 'uploading' | 'failed' | 'cancelled' | 'done'; error: string }

function fileError(file: File): string {
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) return 'Поддерживаются только JPEG, PNG и WebP.'
  if (file.size === 0) return 'Файл пуст. Выберите другое фото.'
  if (file.size > 5 * 1024 * 1024) return 'Фото больше 5 МБ. Выберите файл поменьше.'
  return ''
}

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
  const dialog = useRef<HTMLElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const uploadRequest = useRef<AbortController | null>(null)
  const previewUrl = useRef<string | null>(null)
  const [upload, setUpload] = useState<Upload | null>(null)
  const uploadBlocked = !!upload && upload.phase !== 'done'
  const addItem = useAddItem()
  const updateItem = useUpdateItem()
  const saving = addItem.isPending || updateItem.isPending

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      request.current?.abort()
      uploadRequest.current?.abort()
      if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    }
  }, [])

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    dialog.current?.querySelector<HTMLInputElement>('#item-url')?.focus()
    return () => {
      document.body.style.overflow = overflow
      previous?.focus()
    }
  }, [])

  useEffect(() => { if (saving) dialog.current?.focus() }, [saving])

  const trapFocus = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Escape' && !saving) { event.preventDefault(); onClose(); return }
    if (event.key !== 'Tab') return
    const controls = Array.from(dialog.current?.querySelectorAll<HTMLElement>('button, input:not([hidden]), select, a[href], [tabindex="0"]') ?? [])
      .filter((element) => !element.matches(':disabled'))
    if (!controls.length) { event.preventDefault(); dialog.current?.focus(); return }
    const first = controls[0], last = controls.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  const validUrl = draft.url.trim().length <= 2000 && isHttpUrl(draft.url)
  const validName = !!draft.name.trim() && draft.name.trim().length <= 500
  const hasUploadedImage = isManagedImageUrl(draft.imageUrl)
  const validImage = !draft.imageUrl.trim() || isWishlistImageUrl(draft.imageUrl.trim())
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

  const resetUpload = () => {
    uploadRequest.current?.abort()
    uploadRequest.current = null
    if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    previewUrl.current = null
    setUpload(null)
  }

  const uploadFile = async (file: File, preview: string) => {
    uploadRequest.current?.abort()
    const controller = new AbortController()
    uploadRequest.current = controller
    setUpload({ file, preview, phase: 'uploading', error: '' })
    try {
      const imageUrl = await uploadWishlistImage(file, controller.signal)
      if (controller.signal.aborted || uploadRequest.current !== controller) return
      changeSuggestedField('imageUrl', imageUrl)
      setUpload({ file, preview, phase: 'done', error: '' })
    } catch {
      if (!controller.signal.aborted && uploadRequest.current === controller) {
        setUpload({ file, preview, phase: 'failed', error: 'Не удалось загрузить фото. Повторите попытку или выберите другое.' })
      }
    } finally {
      if (uploadRequest.current === controller) uploadRequest.current = null
    }
  }

  const chooseFiles = (files: ArrayLike<File> | null) => {
    if (!files?.length || saving) return
    resetUpload()
    changeSuggestedField('imageUrl', '')
    const file = files[0]
    const error = files.length !== 1 ? 'Выберите одно фото.' : fileError(file)
    if (error) { setUpload({ file: null, preview: null, phase: 'failed', error }); return }
    const preview = URL.createObjectURL(file)
    previewUrl.current = preview
    void uploadFile(file, preview)
  }

  const changeImageUrl = (value: string) => {
    resetUpload()
    changeSuggestedField('imageUrl', value)
  }

  const cancelUpload = () => {
    uploadRequest.current?.abort()
    uploadRequest.current = null
    setUpload((current) => current ? { ...current, phase: 'cancelled', error: '' } : null)
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
        imageUrl: result.imageUrl && isHttpUrl(result.imageUrl) && isWishlistImageUrl(result.imageUrl) ? result.imageUrl.trim() : null,
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
    if (found.imageUrl) resetUpload()
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
    if (!valid || saving || uploadBlocked) return
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
    <div className="wh-modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !saving) onClose() }}>
      <section ref={dialog} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="item-modal-title" className="wh-modal" onKeyDown={trapFocus}>
        <header className="wh-modal-header">
          <div>
            <h2 id="item-modal-title">{item ? 'Изменить желание' : 'Добавить желание'}</h2>
            <p className="wh-muted">Достаточно ссылки и названия. Остальное — по желанию.</p>
          </div>
          <button type="button" onClick={onClose} disabled={saving} className="wh-modal-close" aria-label="Закрыть форму">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg>
          </button>
        </header>
        <form onSubmit={save} noValidate className="wh-wish-form">
          <fieldset disabled={saving}>
            {error && <p role="alert" className="wh-form-error">{error}</p>}
            <div className="wh-field wh-link-field">
              <label htmlFor="item-url">Ссылка на товар</label>
              <div className="wh-url-row">
                <input id="item-url" type="url" required maxLength={2000} value={draft.url} onChange={(e) => changeUrl(e.target.value)} placeholder="https://..." className="wh-input" aria-invalid={!!draft.url && !validUrl} aria-describedby="item-url-hint" />
                <button type="button" onClick={getMetadata} disabled={!validUrl || fetching} className="wh-button wh-button--quiet" aria-busy={fetching}>{fetching ? 'Получение данных...' : 'Получить данные'}</button>
              </div>
              <p id="item-url-hint" className={draft.url && !validUrl ? 'wh-field-error' : 'wh-field-hint'}>{draft.url && !validUrl ? 'Введите HTTP(S) ссылку длиной до 2000 символов.' : 'По кнопке попробуем найти название и фото.'}</p>
            </div>
            {message && <p role="status" className="wh-metadata-message">{message}</p>}
            {found && (found.name || found.imageUrl) && (
              <div className="wh-found">
                <div>{found.name && <p>Найдено: {found.name}</p>}{found.imageUrl && <p className="wh-muted">Найдено фото товара</p>}</div>
                <button type="button" onClick={applyFound} className="wh-button wh-button--quiet">Применить найденное</button>
              </div>
            )}
            <div className="wh-field">
              <label htmlFor="item-name">Название</label>
              <input id="item-name" required maxLength={500} value={draft.name} onChange={(e) => changeSuggestedField('name', e.target.value)} placeholder="Что вас порадует?" className="wh-input" aria-invalid={!!draft.name && !validName} />
              {draft.name && !validName && <p className="wh-field-error">Введите непустое название длиной до 500 символов.</p>}
            </div>
            <div className="wh-image-section">
              <div className="wh-photo-heading"><h3>Фото</h3><span className="wh-muted">Необязательно</span></div>
              <div className="wh-image-drop" onDragOver={(event) => event.preventDefault()} onDrop={(event) => { event.preventDefault(); chooseFiles(event.dataTransfer.files) }}>
                <div className="wh-photo-preview">
                  {upload?.preview ? <img src={upload.preview} alt="Превью выбранного фото" />
                    : draft.imageUrl ? <ItemImage imageUrl={draft.imageUrl} name="Превью товара" className="wh-preview-image" />
                    : <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.25" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="3" /><circle cx="8" cy="8" r="1.5" /><path d="m3 17 5-5 4 4 3-3 6 6" /></svg>}
                </div>
                <div className="wh-photo-controls">
                  <input ref={fileInput} id="item-file" type="file" accept="image/jpeg,image/png,image/webp" hidden aria-label="Фото из файла" aria-describedby="item-file-hint" onChange={(event) => { chooseFiles(event.currentTarget.files); event.currentTarget.value = '' }} />
                  <button type="button" className="wh-button wh-upload-button" onClick={() => fileInput.current?.click()}>
                    <svg viewBox="0 0 20 20" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true"><path d="M10 13V3m-4 4 4-4 4 4M3 12v5h14v-5" strokeLinecap="round" strokeLinejoin="round" /></svg>
                    {upload || draft.imageUrl ? 'Заменить фото' : 'Выбрать фото'}
                  </button>
                  <p id="item-file-hint" className="wh-field-hint">JPEG, PNG или WebP до 5 МБ. Можно перетащить сюда.</p>
                  {upload?.file && <p className="wh-file-name">{upload.file.name}</p>}
                  {upload?.phase === 'uploading' && <div className="wh-upload-state"><p role="status">Загрузка фото...</p><button type="button" className="wh-button wh-button--quiet" onClick={cancelUpload}>Отменить загрузку</button></div>}
                  {upload?.phase === 'done' && <p role="status" className="wh-upload-done">Фото загружено</p>}
                  {upload?.phase === 'cancelled' && <p role="status" className="wh-field-hint">Загрузка отменена. Повторите её или удалите фото.</p>}
                  {upload?.error && <p role="alert" className="wh-field-error">{upload.error}</p>}
                  {(upload?.phase === 'failed' || upload?.phase === 'cancelled') && upload.file && upload.preview && <button type="button" className="wh-button wh-button--quiet" onClick={() => { if (upload.file && upload.preview) void uploadFile(upload.file, upload.preview) }}>Повторить загрузку</button>}
                  {(draft.imageUrl || upload) && <button type="button" onClick={() => changeImageUrl('')} className="wh-button wh-button--danger">Удалить фото</button>}
                </div>
              </div>
              <div className="wh-field wh-photo-url">
                <label htmlFor="item-image">Фото: URL (необязательно)</label>
                <input id="item-image" type="text" inputMode="url" value={hasUploadedImage ? '' : draft.imageUrl} onChange={(e) => changeImageUrl(e.target.value)} placeholder="https://..." className="wh-input" aria-invalid={!validImage} aria-describedby={hasUploadedImage ? 'item-image-hint' : undefined} />
                {hasUploadedImage && <p id="item-image-hint" className="wh-field-hint">Используется загруженное фото; ссылка заменит его.</p>}
                {!validImage && <p className="wh-field-error">Введите HTTP(S) ссылку на фото или очистите поле.</p>}
              </div>
            </div>
            <div className="wh-price-row">
              <div className="wh-field">
                <label htmlFor="item-price">Цена (необязательно)</label>
                <input id="item-price" type="number" min="0" step="any" value={draft.price} onChange={(e) => setDraft((current) => ({ ...current, price: e.target.value }))} placeholder="Не указана" className="wh-input" aria-invalid={!validPrice} />
                {!validPrice && <p className="wh-field-error">Цена должна быть числом не меньше нуля.</p>}
              </div>
              <div className="wh-field">
                <label htmlFor="item-currency">Валюта</label>
                <input id="item-currency" value={draft.currency} onChange={(e) => setDraft((current) => ({ ...current, currency: e.target.value }))} className="wh-input" />
              </div>
            </div>
          </fieldset>
          <footer className="wh-modal-footer">
            <p className="wh-field-hint">{uploadBlocked ? 'Завершите загрузку или удалите выбранное фото.' : 'Желание можно изменить в любой момент.'}</p>
            <div><button type="button" onClick={onClose} disabled={saving} className="wh-button wh-button--quiet">Отмена</button><button type="submit" disabled={!valid || saving || uploadBlocked} className="wh-button wh-button--primary">{saving ? 'Сохранение...' : 'Сохранить'}</button></div>
          </footer>
        </form>
      </section>
    </div>
  )
}
