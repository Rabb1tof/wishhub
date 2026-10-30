import { useState, useEffect, useRef, type FormEvent } from 'react'
import { Layout } from '@/components/Layout'
import { useMyProfile, useUpdateProfile, useChangePassword, useUploadAvatar } from '@/api/useProfile'
import { UserAvatar } from '@/components/UserAvatar'
import { ConnectedAccountsSection } from '@/components/ConnectedAccountsSection'

export function SettingsPage() {
  const { data: profile, isLoading, isError, refetch } = useMyProfile()
  const updateProfile = useUpdateProfile()
  const changePassword = useChangePassword()
  const uploadAvatar = useUploadAvatar()

  const [displayName, setDisplayName] = useState('')
  const [bio, setBio] = useState('')
  const [wishlistPrivacy, setWishlistPrivacy] = useState<'public' | 'friendsOnly' | 'private'>('public')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  const initializedFor = useRef<string | null>(null)

  // An avatar refetch must not replace this account's unsaved profile fields.
  useEffect(() => {
    if (profile && initializedFor.current !== profile.id) {
      initializedFor.current = profile.id
      setDisplayName(profile.displayName)
      setBio(profile.bio || '')
      // Accept the existing backend casing and fail closed for unknown values.
      const privacyMap: Record<string, 'public' | 'friendsOnly' | 'private'> = {
        'public': 'public',
        'friendsonly': 'friendsOnly',
        'private': 'private'
      }
      setWishlistPrivacy(privacyMap[profile.wishlistPrivacy.toLowerCase()] ?? 'private')
    }
  }, [profile])

  const handleProfileSubmit = (e: FormEvent) => {
    e.preventDefault()
    updateProfile.mutate({ displayName, bio, wishlistPrivacy })
  }

  const handlePasswordSubmit = (e: FormEvent) => {
    e.preventDefault()
    if (newPassword !== confirmPassword) return
    changePassword.mutate({ currentPassword, newPassword }, {
      onSuccess: () => { setCurrentPassword(''); setNewPassword(''); setConfirmPassword('') },
    })
  }

  const handleAvatarChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    uploadAvatar.mutate(file)
    e.target.value = ''
  }

  if (isLoading) {
    return (
      <Layout>
        <div className="text-center py-8">Загрузка...</div>
      </Layout>
    )
  }

  if (isError || !profile) return <Layout><p role="alert">Не удалось загрузить профиль.</p><button type="button" onClick={() => void refetch()}>Повторить</button></Layout>

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
              accept="image/jpeg,image/png,image/webp"
              aria-label="Фото профиля"
              onChange={handleAvatarChange}
              disabled={uploadAvatar.isPending}
              className="text-sm"
            />
          </div>
          {uploadAvatar.isError && <p role="alert" className="text-red-600 mt-2">Не удалось загрузить аватар. Попробуйте ещё раз.</p>}
          {uploadAvatar.isSuccess && (
            <p className="text-green-600 mt-2">Аватар обновлён</p>
          )}
        </section>

        {/* Profile Section */}
        <section className="bg-white rounded-lg shadow p-6 mb-6">
          <h2 className="text-xl font-semibold mb-4">Профиль</h2>
          <form onSubmit={handleProfileSubmit} className="space-y-4">
            <div>
              <label htmlFor="settings-name" className="block text-sm font-medium text-gray-700">Отображаемое имя</label>
              <input
                id="settings-name"
                maxLength={100}
                type="text"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label htmlFor="settings-bio" className="block text-sm font-medium text-gray-700">О себе</label>
              <textarea
                id="settings-bio"
                maxLength={500}
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                rows={3}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label htmlFor="settings-privacy" className="block text-sm font-medium text-gray-700">Приватность вишлиста</label>
              <select
                id="settings-privacy"
                value={wishlistPrivacy}
                onChange={(e) => setWishlistPrivacy(e.target.value as 'public' | 'friendsOnly' | 'private')}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              >
                <option value="public">Публичный</option>
                <option value="friendsOnly">Только друзья</option>
                <option value="private">Приватный</option>
              </select>
            </div>

            <button
              type="submit"
              disabled={updateProfile.isPending}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {updateProfile.isPending ? 'Сохранение...' : 'Сохранить'}
            </button>

            {updateProfile.isError && <p role="alert" className="text-red-600">Не удалось сохранить профиль. Попробуйте ещё раз.</p>}
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
              <label htmlFor="settings-current-password" className="block text-sm font-medium text-gray-700">Текущий пароль</label>
              <input
                id="settings-current-password"
                required
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label htmlFor="settings-new-password" className="block text-sm font-medium text-gray-700">Новый пароль</label>
              <input
                id="settings-new-password"
                required minLength={8} maxLength={100}
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="mt-1 block w-full px-3 py-2 border rounded-md"
              />
            </div>

            <div>
              <label htmlFor="settings-confirm-password" className="block text-sm font-medium text-gray-700">Подтвердите новый пароль</label>
              <input
                id="settings-confirm-password"
                required minLength={8} maxLength={100}
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

            {changePassword.isError && <p role="alert" className="text-red-600">Не удалось изменить пароль. Проверьте текущий пароль и повторите попытку.</p>}
            {changePassword.isSuccess && (
              <p className="text-green-600">Пароль изменён</p>
            )}
          </form>
        </section>
      </div>
    </Layout>
  )
}
