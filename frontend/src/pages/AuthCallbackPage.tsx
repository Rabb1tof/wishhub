import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { completeAuth } from '@/api/completeAuth'
import { useAuthStore } from '@/store/auth'
import type { AuthResponse } from '@/types/auth'

export function AuthCallbackPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const providerError = params.get('error')
  const completion = useRef<Promise<AuthResponse> | null>(null)
  const startingSession = useRef(useAuthStore.getState().sessionVersion)
  const [completionError, setCompletionError] = useState<string | null>(null)

  useEffect(() => {
    if (providerError) return
    let active = true
    // StrictMode replays the effect. A one-use handoff must keep the same request.
    completion.current ??= completeAuth()
    completion.current.then((session) => {
      if (!active) return
      if (useAuthStore.getState().sessionVersion !== startingSession.current) {
        setCompletionError('session_changed')
        return
      }
      useAuthStore.getState().setAuth(session)
      navigate('/', { replace: true })
    }, (error: unknown) => {
      if (!active) return
      setCompletionError(isAxiosError(error) && error.response?.status === 401
        ? 'completion_expired'
        : 'completion_failed')
    })
    return () => { active = false }
  }, [providerError, navigate])

  const error = providerError || completionError
  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="max-w-md w-full space-y-4 p-8 bg-white rounded-lg shadow text-center">
          <h2 className="text-xl font-semibold text-red-600">Ошибка авторизации</h2>
          <p role="alert" className="text-sm text-gray-600">{describeError(error)}</p>
          <button
            onClick={() => navigate('/login', { replace: true })}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
          >
            Вернуться ко входу
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center">
      <div role="status" className="text-gray-600">Входим…</div>
    </div>
  )
}

function describeError(code: string): string {
  const map: Record<string, string> = {
    vk_not_configured: 'Вход через VK не настроен на сервере',
    vk_token_failed: 'VK отклонил запрос токена',
    vk_no_token: 'VK не вернул access token',
    invalid_state: 'Проверка состояния OAuth не пройдена — попробуйте ещё раз',
    user_not_found: 'Пользователь не найден',
    user_create_failed: 'Не удалось создать пользователя',
    account_link_required: 'Войдите в существующий аккаунт и привяжите VK в настройках',
    completion_expired: 'Сессия входа отсутствует или истекла. Попробуйте войти снова.',
    completion_failed: 'Не удалось завершить вход. Попробуйте войти снова.',
    session_changed: 'Аккаунт уже изменился. Попробуйте войти снова.',
  }
  return map[code] ?? code
}
