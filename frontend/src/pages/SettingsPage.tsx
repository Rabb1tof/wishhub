import { useState, useEffect, type FormEvent } from 'react'
import { Layout } from '@/components/Layout'
import { useMyProfile, useUpdateProfile, useChangePassword, useUploadAvatar } from '@/api/useProfile'
import { UserAvatar } from '@/components/UserAvatar'
import { ConnectedAccountsSection } from '@/components/ConnectedAccountsSection'

export function SettingsPage() {
  const { data: profile, isLoading } = useMyProfile()
  const updateProfile = useUpdateProfile()
  const changePassword = useChangePassword()
  const uploadAvatar = useUploadAvatar()

  const [displayName, setDisplayName] = useState('')
  const [bio, setBio] = useState('')
  const [wishlistPrivacy, setWishlistPrivacy] = useState<'public' | 'friendsOnly' | 'private'>('public')
  const [priceRefreshIntervalHours, setPriceRefreshIntervalHours] = useState<number>(24)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  // Initialize form when profile loads (only once)
  useEffect(() => {
    if (profile) {
      setDisplayName(profile.displayName)
      setBio(profile.bio || '')
      // Map backend CamelCase to frontend camelCase
      const privacyMap: Record<string, 'public' | 'friendsOnly' | 'private'> = {
        'Public': 'public',
        'public': 'public',
        'FriendsOnly': 'friendsOnly',
        'friendsonly': 'friendsOnly',
        'Private': 'private',
        'private': 'private'
      }
      setWishlistPrivacy(privacyMap[profile.wishlistPrivacy] ?? 'public')
      setPriceRefreshIntervalHours(profile.priceRefreshIntervalHours ?? 24)
    }
  }, [profile])

  const handleProfileSubmit = async (e: FormEvent) => {
    e.preventDefault()
    await updateProfile.mutateAsync({ displayName, bio, wishlistPrivacy, priceRefreshIntervalHours })
  }

  const handlePasswordSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (newPassword !== confirmPassword) return
    await changePassword.mutateAsync({ currentPassword, newPassword })
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
  }

  const handleAvatarChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    await uploadAvatar.mutateAsync(file)
  }

  if (isLoading) {
    return (
      <Layout>
        <div className="text-center py-8">Загрузка...</div>
      </Layout>
    )
  }

  return (
    <Layout>
      <div className="max-w-2xl mx-auto">
        <h1 className="text-2xl font-bold mb-6">Настройки</h1>

        {/* Avatar Section */}
        <section className="bg-white rounded-lg shadow p-6 mb-6">
          <h2 className="text-xl font-semibold mb-4">Аватар</h2>
          <div className="flex items-center space-x-4">
            <UserAvatar
              displayName={profile?.displayName || '?'}
              avatarUrl={profile?.avatarUrl}
              size="lg"
            />
            <input
              type="file"
              accept="image/*"
              onChange={handleAvatarChange}
              disabled={uploadAvatar.isPending}
              className="text-sm"
            />
          </div>
          {uploadAvatar.isSuccess && (
            <p className="text-green-600 mt-2">Аватар обновлён</p>
          )}
        </section>

        {/* Profile Section */}
        <section className="bg-white rounded-lg shadow p-6 mb-6">
          <h2 className="text-xl font-semibold mb-4">Профиль</h2>
          <form onSubmit={handleProfileSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Отображаемое имя</label>
              <input
                type="text"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">О себе</label>
              <textarea
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                rows={3}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Приватность вишлиста</label>
              <select
                value={wishlistPrivacy}
                onChange={(e) => setWishlistPrivacy(e.target.value as 'public' | 'friendsOnly' | 'private')}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              >
                <option value="public">Публичный</option>
                <option value="friendsOnly">Только друзья</option>
                <option value="private">Приватный</option>
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Автообновление цен и картинок</label>
              <select
                value={priceRefreshIntervalHours}
                onChange={(e) => setPriceRefreshIntervalHours(Number(e.target.value))}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              >
                <option value={1}>Каждый час</option>
                <option value={3}>Каждые 3 часа</option>
                <option value={6}>Каждые 6 часов</option>
                <option value={12}>Каждые 12 часов</option>
                <option value={24}>Раз в сутки</option>
                <option value={72}>Раз в 3 дня</option>
                <option value={168}>Раз в неделю</option>
              </select>
              <p className="text-xs text-gray-500 mt-1">Сервис автоматически сверяет цены и картинки с маркетплейсами. При изменении цены придёт уведомление.</p>
            </div>

            <button
              type="submit"
              disabled={updateProfile.isPending}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {updateProfile.isPending ? 'Сохранение...' : 'Сохранить'}
            </button>

            {updateProfile.isSuccess && (
              <p className="text-green-600">Профиль обновлён</p>
            )}
          </form>
        </section>

        {/* Connected accounts */}
        <ConnectedAccountsSection />

        {/* Password Section */}
        <section className="bg-white rounded-lg shadow p-6">
          <h2 className="text-xl font-semibold mb-4">Изменить пароль</h2>
          <form onSubmit={handlePasswordSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Текущий пароль</label>
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Новый пароль</label>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Подтвердите новый пароль</label>
              <input
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
              {newPassword !== confirmPassword && confirmPassword && (
                <p className="text-red-500 text-sm mt-1">Пароли не совпадают</p>
              )}
            </div>

            <button
              type="submit"
              disabled={changePassword.isPending || newPassword !== confirmPassword}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {changePassword.isPending ? 'Изменение...' : 'Изменить пароль'}
            </button>

            {changePassword.isSuccess && (
              <p className="text-green-600">Пароль изменён</p>
            )}
          </form>
        </section>
      </div>
    </Layout>
  )
}
