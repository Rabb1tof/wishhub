import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useLocation, useSearchParams } from 'react-router-dom'
import { useLogin } from '@/api/useAuth'
import { useOAuthProviders, useTelegramLogin } from '@/api/useOAuth'
import { TelegramLoginButton } from '@/components/TelegramLoginButton'

const ERROR_MESSAGES: Record<string, string> = {
  vk_not_configured: 'Вход через VK не настроен',
  vk_token_failed: 'VK отклонил запрос токена',
  vk_no_token: 'VK не вернул токен',
  invalid_state: 'Сессия OAuth истекла, попробуйте снова',
  user_not_found: 'Пользователь не найден',
  user_create_failed: 'Не удалось создать пользователя',
}

export function LoginPage() {
  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [password, setPassword] = useState('')
  const navigate = useNavigate()
  const location = useLocation()
  const [params] = useSearchParams()
  const login = useLogin()
  const providers = useOAuthProviders()
  const telegramLogin = useTelegramLogin()

  const from = (location.state as { from?: { pathname: string } })?.from?.pathname || '/'
  const errorCode = params.get('error')

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    try {
      await login.mutateAsync({ usernameOrEmail, password })
      navigate(from, { replace: true })
    } catch {
      // Error handled by mutation
    }
  }

  const handleVkLogin = () => {
    window.location.href = '/api/auth/vk'
  }

  const vkEnabled = providers.data?.vk ?? false
  const telegramBot = providers.data?.telegramBotUsername ?? null
  const hasOAuth = vkEnabled || !!telegramBot

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="max-w-md w-full space-y-6 p-8 bg-white rounded-lg shadow">
        <h2 className="text-3xl font-bold text-center">Вход</h2>

        {errorCode && (
          <p className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-md px-3 py-2">
            {ERROR_MESSAGES[errorCode] ?? errorCode}
          </p>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="username" className="block text-sm font-medium text-gray-700">
              Имя пользователя или email
            </label>
            <input
              id="username"
              type="text"
              required
              value={usernameOrEmail}
              onChange={(e) => setUsernameOrEmail(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          <div>
            <label htmlFor="password" className="block text-sm font-medium text-gray-700">
              Пароль
            </label>
            <input
              id="password"
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          {login.isError && (
            <p className="text-red-500 text-sm">Неверные учетные данные</p>
          )}

          <button
            type="submit"
            disabled={login.isPending}
            className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
          >
            {login.isPending ? 'Вход...' : 'Войти'}
          </button>
        </form>

        {hasOAuth && (
          <div className="space-y-3">
            <div className="flex items-center gap-3 text-xs text-gray-500">
              <div className="flex-1 h-px bg-gray-200" />
              <span>или</span>
              <div className="flex-1 h-px bg-gray-200" />
            </div>

            {vkEnabled && (
              <button
                type="button"
                onClick={handleVkLogin}
                className="w-full py-2 px-4 rounded-md text-white font-medium flex items-center justify-center gap-2"
                style={{ background: '#0077FF' }}
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                  <path d="M13.162 18.994c.609 0 .858-.406.85-.915-.031-1.917.714-2.949 2.059-1.604 1.488 1.488 1.796 2.519 3.603 2.519h3.2c.808 0 1.126-.26 1.126-.668 0-.863-1.421-2.386-2.625-3.504-1.686-1.565-1.765-1.602-.313-3.486 1.801-2.339 4.157-5.336 2.073-5.336h-3.981c-.772 0-.828.435-1.103 1.083-.995 2.347-2.886 5.387-3.604 4.922-.751-.485-.407-2.406-.35-5.261.015-.754.011-1.271-1.141-1.539-.629-.145-1.241-.205-1.809-.205-2.273 0-3.841.953-2.95 1.119 1.571.293 1.42 3.692 1.054 5.16-.638 2.556-3.036-2.024-4.035-4.305-.241-.548-.315-.974-1.175-.974h-3.255c-.492 0-.787.16-.787.516 0 .602 2.96 6.72 5.786 9.77 2.756 2.975 5.48 2.708 7.376 2.708z"/>
                </svg>
                Войти через VK
              </button>
            )}

            {telegramBot && (
              <div className="flex justify-center">
                <TelegramLoginButton
                  botUsername={telegramBot}
                  onAuth={(data) => telegramLogin.mutate(data, {
                    onSuccess: () => navigate(from, { replace: true }),
                  })}
                />
              </div>
            )}

            {telegramLogin.isError && (
              <p className="text-red-500 text-sm text-center">
                Ошибка входа через Telegram
              </p>
            )}
          </div>
        )}

        <div className="text-center">
          <p>
            Нет аккаунта?{' '}
            <Link to="/register" className="text-blue-600 hover:underline">
              Зарегистрироваться
            </Link>
          </p>
        </div>
      </div>
    </div>
  )
}
