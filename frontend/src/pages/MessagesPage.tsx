import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { useDialogs } from '@/api/useMessages'
import { ChatWindow } from '@/components/ChatWindow'

export function MessagesPage() {
  const [searchParams] = useSearchParams()
  const initialUserId = searchParams.get('user')
  const [selectedUserId, setSelectedUserId] = useState<string | null>(initialUserId)
  const { data: dialogs, isLoading: dialogsLoading } = useDialogs()

  const selectedDialog = dialogs?.find((d) => d.user.id === selectedUserId)

  return (
    <Layout>
      <div className="max-w-6xl mx-auto h-[calc(100vh-200px)]">
        <div className="flex h-full bg-white rounded-lg shadow overflow-hidden">
          {/* Dialogs List */}
          <div className="w-1/3 border-r overflow-y-auto">
            <h2 className="p-4 font-bold border-b">Диалоги</h2>
            {dialogsLoading ? (
              <p className="p-4 text-gray-600">Загрузка...</p>
            ) : dialogs?.length === 0 ? (
              <p className="p-4 text-gray-500">Нет диалогов</p>
            ) : (
              <div>
                {dialogs?.map((dialog) => (
                  <button
                    key={dialog.user.id}
                    onClick={() => setSelectedUserId(dialog.user.id)}
                    className={`w-full p-4 text-left hover:bg-gray-50 border-b ${
                      selectedUserId === dialog.user.id ? 'bg-blue-50' : ''
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center">
                        <UserAvatar displayName={dialog.user.displayName} avatarUrl={dialog.user.avatarUrl} size="md" />
                        <div className="ml-3">
                          <p className="font-semibold">{dialog.user.displayName}</p>
                          {dialog.lastMessage && (
                            <p className="text-sm text-gray-500 truncate max-w-[150px]">
                              {dialog.lastMessage.content}
                            </p>
                          )}
                        </div>
                      </div>
                      {dialog.unreadCount > 0 && (
                        <span className="px-2 py-1 bg-blue-600 text-white text-xs rounded-full">
                          {dialog.unreadCount}
                        </span>
                      )}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Chat Window */}
          <div className="w-2/3 flex flex-col">
            {selectedUserId && selectedDialog ? (
              <ChatWindow userId={selectedUserId} userName={selectedDialog.user.displayName} />
            ) : (
              <div className="flex-1 flex items-center justify-center text-gray-500">
                Выберите диалог
              </div>
            )}
          </div>
        </div>
      </div>
    </Layout>
  )
}
