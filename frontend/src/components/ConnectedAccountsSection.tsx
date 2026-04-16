import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  useExternalLogins,
  useLinkTelegram,
  useOAuthProviders,
  useStartVkLink,
  useUnlinkProvider,
} from '@/api/useOAuth'
import { TelegramLoginButton } from './TelegramLoginButton'

const LINK_ERROR_MESSAGES: Record<string, string> = {
  vk_already_linked: 'Этот аккаунт VK уже привязан к другому пользователю',
  vk_not_configured: 'Вход через VK не настроен',
  vk_token_failed: 'VK отклонил запрос токена',
  vk_no_token: 'VK не вернул токен',
  invalid_state: 'Сессия OAuth истекла, попробуйте снова',
  invalid_link_state: 'Сессия привязки истекла',
}

export function ConnectedAccountsSection() {
  const providers = useOAuthProviders()
  const { data: externalLogins, isLoading } = useExternalLogins()
  const startVk = useStartVkLink()
  const linkTelegram = useLinkTelegram()
  const unlinkProvider = useUnlinkProvider()
  const [params, setParams] = useSearchParams()
  const [flash, setFlash] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  // Читаем сообщения после OAuth-редиректа
  useEffect(() => {
    const linked = params.get('linked')
    const error = params.get('error')
    if (linked) {
      setFlash({ type: 'success', text: providerLabel(linked) + ' успешно привязан' })
      params.delete('linked')
      setParams(params, { replace: true })
    } else if (error) {
      setFlash({ type: 'error', text: LINK_ERROR_MESSAGES[error] ?? error })
      params.delete('error')
      setParams(params, { replace: true })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (isLoading || !externalLogins) {
    return (
      <section className="bg-white rounded-lg shadow p-6 mb-6">
        <h2 className="text-xl font-semibold mb-4">Связанные аккаунты</h2>
        <p className="text-sm text-gray-500">Загрузка…</p>
      </section>
    )
  }

  const linkedProviders = new Set(externalLogins.logins.map((l) => l.provider))
  const canUnlink = (provider: string) => {
    // Нельзя отвязать единственный способ входа
    const hasPassword = externalLogins.hasPassword
    const otherLogins = externalLogins.logins.filter((l) => l.provider !== provider).length
    return hasPassword || otherLogins > 0
  }

  const vkAvailable = providers.data?.vk ?? false
  const telegramBot = providers.data?.telegramBotUsername ?? null

  return (
    <section className="bg-white rounded-lg shadow p-6 mb-6">
      <h2 className="text-xl font-semibold mb-4">Связанные аккаунты</h2>

      {flash && (
        <p
          className={
            flash.type === 'success'
              ? 'text-sm text-green-700 bg-green-50 border border-green-200 rounded-md px-3 py-2 mb-4'
              : 'text-sm text-red-700 bg-red-50 border border-red-200 rounded-md px-3 py-2 mb-4'
          }
        >
          {flash.text}
        </p>
      )}

      <div className="space-y-3">
        {/* VK */}
        <ProviderRow
          name="VK"
          available={vkAvailable}
          linked={linkedProviders.has('vk')}
          linkedAt={externalLogins.logins.find((l) => l.provider === 'vk')?.linkedAt}
          canUnlink={canUnlink('vk')}
          onLink={() => startVk.mutate()}
          onUnlink={() => unlinkProvider.mutate('vk')}
          isLinking={startVk.isPending}
          isUnlinking={unlinkProvider.isPending}
          actionLink={
            vkAvailable && !linkedProviders.has('vk') ? (
              <button
                onClick={() => startVk.mutate()}
                disabled={startVk.isPending}
                className="px-3 py-1.5 rounded-md text-sm text-white disabled:opacity-50"
                style={{ background: '#0077FF' }}
              >
                Привязать
              </button>
            ) : null
          }
        />

        {/* Telegram */}
        <ProviderRow
          name="Telegram"
          available={!!telegramBot}
          linked={linkedProviders.has('telegram')}
          linkedAt={externalLogins.logins.find((l) => l.provider === 'telegram')?.linkedAt}
          canUnlink={canUnlink('telegram')}
          isLinking={linkTelegram.isPending}
          isUnlinking={unlinkProvider.isPending}
          onLink={() => {}}
          onUnlink={() => unlinkProvider.mutate('telegram')}
          actionLink={
            telegramBot && !linkedProviders.has('telegram') ? (
              <TelegramLoginButton
                botUsername={telegramBot}
                size="medium"
                onAuth={(data) =>
                  linkTelegram.mutate(data, {
                    onSuccess: () =>
                      setFlash({ type: 'success', text: 'Telegram успешно привязан' }),
                    onError: () =>
                      setFlash({ type: 'error', text: 'Не удалось привязать Telegram' }),
                  })
                }
              />
            ) : null
          }
        />
      </div>

      {!vkAvailable && !telegramBot && (
        <p className="text-sm text-gray-500 mt-4">
          Внешние провайдеры не настроены на сервере.
        </p>
      )}
    </section>
  )
}

interface RowProps {
  name: string
  available: boolean
  linked: boolean
  linkedAt?: string
  canUnlink: boolean
  onLink: () => void
  onUnlink: () => void
  isLinking: boolean
  isUnlinking: boolean
  actionLink: React.ReactNode
}

function ProviderRow({
  name,
  available,
  linked,
  linkedAt,
  canUnlink,
  onUnlink,
  isUnlinking,
  actionLink,
}: RowProps) {
  return (
    <div className="flex items-center justify-between py-2 border-b last:border-b-0">
      <div>
        <div className="font-medium">{name}</div>
        <div className="text-xs text-gray-500">
          {!available
            ? 'Недоступно'
            : linked
              ? `Привязан${linkedAt ? ' ' + new Date(linkedAt).toLocaleDateString('ru-RU') : ''}`
              : 'Не привязан'}
        </div>
      </div>
      <div>
        {linked ? (
          <button
            onClick={onUnlink}
            disabled={!canUnlink || isUnlinking}
            title={!canUnlink ? 'Нельзя отвязать единственный способ входа' : undefined}
            className="px-3 py-1.5 rounded-md text-sm border border-gray-300 hover:bg-gray-50 disabled:opacity-50"
          >
            {isUnlinking ? 'Отвязка…' : 'Отвязать'}
          </button>
        ) : (
          actionLink
        )}
      </div>
    </div>
  )
}

function providerLabel(provider: string): string {
  return provider === 'vk' ? 'VK' : provider === 'telegram' ? 'Telegram' : provider
}
