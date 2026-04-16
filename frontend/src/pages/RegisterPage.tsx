import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useRegister } from '@/api/useAuth'

export function RegisterPage() {
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const navigate = useNavigate()
  const register = useRegister()

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (password !== confirmPassword) {
      setError('Пароли не совпадают')
      return
    }
    setError('')

    const data = { username, email, displayName, password }

    try {
      await register.mutateAsync(data)
      navigate('/')
    } catch (err: any) {
      const responseData = err.response?.data
      const errors = responseData?.Errors || responseData?.errors

      if (errors) {
        if (Array.isArray(errors)) {
          setError(errors.join(', '))
        } else if (typeof errors === 'object') {
          const errorMessages = Object.values(errors).flat().join(', ')
          setError(errorMessages || 'Ошибка регистрации')
        } else {
          setError(String(errors))
        }
        return
      }

      if (responseData?.detail || responseData?.title) {
        setError(responseData.detail || responseData.title || 'Ошибка регистрации')
        return
      }

      if (typeof responseData === 'string') {
        setError(responseData)
        return
      }

      setError('Ошибка регистрации')
    }
  }

  const passwordsMatch = password === confirmPassword

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="max-w-md w-full space-y-8 p-8 bg-white rounded-lg shadow">
        <h2 className="text-3xl font-bold text-center">Регистрация</h2>

        {error && (
          <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="username" className="block text-sm font-medium text-gray-700">
              Имя пользователя *
            </label>
            <input
              id="username"
              type="text"
              required
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          <div>
            <label htmlFor="email" className="block text-sm font-medium text-gray-700">
              Email *
            </label>
            <input
              id="email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          <div>
            <label htmlFor="displayName" className="block text-sm font-medium text-gray-700">
              Отображаемое имя *
            </label>
            <input
              id="displayName"
              type="text"
              required
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          <div>
            <label htmlFor="password" className="block text-sm font-medium text-gray-700">
              Пароль *
            </label>
            <input
              id="password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
          </div>

          <div>
            <label htmlFor="confirmPassword" className="block text-sm font-medium text-gray-700">
              Подтвердите пароль *
            </label>
            <input
              id="confirmPassword"
              type="password"
              required
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md"
            />
            {!passwordsMatch && confirmPassword && (
              <p className="text-red-500 text-sm mt-1">Пароли не совпадают</p>
            )}
          </div>

          {register.isError && (
            <p className="text-red-500 text-sm">Ошибка регистрации</p>
          )}

          <button
            type="submit"
            disabled={register.isPending || !passwordsMatch}
            className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
          >
            {register.isPending ? 'Регистрация...' : 'Зарегистрироваться'}
          </button>
        </form>

        <p className="text-center">
          Уже есть аккаунт?{' '}
          <Link to="/login" className="text-blue-600 hover:underline">
            Войти
          </Link>
        </p>
      </div>
    </div>
  )
}
