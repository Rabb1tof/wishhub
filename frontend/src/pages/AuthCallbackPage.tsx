import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import axios from 'axios'
import { useAuthStore } from '@/store/auth'
import type { User } from '@/types/auth'

/**
 * Обрабатывает OAuth-редирект с бэкенда.
 * Ожидает query: accessToken, refreshToken, expiresAt, error.
 * Сохраняет токены в Zustand, подтягивает профиль через /users/me и редиректит на главную.
 */
export function AuthCallbackPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const processedRef = useRef(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (processedRef.current) return
    processedRef.current = true

    const err = params.get('error')
    if (err) {
      setError(err)
      return
    }

    const accessToken = params.get('accessToken')
    const refreshToken = params.get('refreshToken')
    const expiresAt = params.get('expiresAt')

    if (!accessToken || !refreshToken || !expiresAt) {
      setError('missing_tokens')
      return
    }

    const run = async () => {
      try {
        // Получаем профиль напрямую с токеном из query
        const res = await axios.get<{
          id: string
          username: string
          displayName: string
          avatarUrl?: string
        }>('/api/users/me', {
          headers: { Authorization: `Bearer ${accessToken}` },
        })

        const user: User = {
          id: res.data.id,
          username: res.data.username,
          displayName: res.data.displayName,
          avatarUrl: res.data.avatarUrl,
        }

        setAuth({ accessToken, refreshToken, expiresAt, user })
        navigate('/', { replace: true })
      } catch (e) {
        console.error('AuthCallback failed', e)
        setError('profile_load_failed')
      }
    }

    run()
  }, [params, setAuth, navigate])

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="max-w-md w-full space-y-4 p-8 bg-white rounded-lg shadow text-center">
          <h2 className="text-xl font-semibold text-red-600">Ошибка авторизации</h2>
          <p className="text-sm text-gray-600">{describeError(error)}</p>
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
      <div className="text-gray-600">Входим…</div>
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
    missing_tokens: 'В ответе отсутствуют токены',
    profile_load_failed: 'Не удалось загрузить профиль',
  }
  return map[code] ?? code
}
