import { useEffect, useRef } from 'react'

export interface TelegramAuthData {
  id: number
  first_name: string
  last_name?: string
  username?: string
  photo_url?: string
  auth_date: number
  hash: string
}

interface Props {
  botUsername: string
  /** Callback после успешной подписи данных. Передаётся payload для API. */
  onAuth: (data: TelegramAuthData) => void
  /** 'large' | 'medium' | 'small' — внешний вид виджета */
  size?: 'large' | 'medium' | 'small'
  requestAccess?: 'write' | 'read'
}

/**
 * Вставляет Telegram Login Widget в DOM.
 * Виджет монтируется через <script> тег, который рендерит iframe.
 * Callback принимает подписанные данные для отправки на бэк.
 *
 * https://core.telegram.org/widgets/login
 */
export function TelegramLoginButton({
  botUsername,
  onAuth,
  size = 'large',
  requestAccess = 'write',
}: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const onAuthRef = useRef(onAuth)
  onAuthRef.current = onAuth

  useEffect(() => {
    if (!containerRef.current) return
    if (!botUsername) return

    // Глобальный callback, на который указывает data-onauth
    const callbackName = `__tgLoginCb_${Math.random().toString(36).slice(2)}`
    ;(window as unknown as Record<string, (d: TelegramAuthData) => void>)[callbackName] =
      (data: TelegramAuthData) => {
        onAuthRef.current(data)
      }

    const script = document.createElement('script')
    script.src = 'https://telegram.org/js/telegram-widget.js?22'
    script.async = true
    script.setAttribute('data-telegram-login', botUsername)
    script.setAttribute('data-size', size)
    script.setAttribute('data-onauth', `${callbackName}(user)`)
    script.setAttribute('data-request-access', requestAccess)
    script.setAttribute('data-userpic', 'false')

    containerRef.current.innerHTML = ''
    containerRef.current.appendChild(script)

    return () => {
      delete (window as unknown as Record<string, unknown>)[callbackName]
      if (containerRef.current) {
        containerRef.current.innerHTML = ''
      }
    }
  }, [botUsername, size, requestAccess])

  if (!botUsername) {
    return null
  }

  return <div ref={containerRef} />
}
