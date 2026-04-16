import { Layout } from '@/components/Layout'
import { UserAvatar } from '@/components/UserAvatar'
import { useFriends, useFriendRequests, useAcceptRequest, useRejectOrCancelRequest } from '@/api/useFriends'
import type { Friendship, FriendRequest } from '@/types/friends'

export function FriendsPage() {
  const { data: friends, isLoading: friendsLoading } = useFriends()
  const { data: incomingRequests, isLoading: incomingLoading } = useFriendRequests('incoming')
  const { data: outgoingRequests, isLoading: outgoingLoading } = useFriendRequests('outgoing')

  return (
    <Layout>
      <div className="max-w-4xl mx-auto">
        <h1 className="text-2xl font-bold mb-6">Друзья</h1>

        {/* Incoming Requests */}
        <section className="mb-8">
          <h2 className="text-xl font-semibold mb-4">Входящие заявки</h2>
          {incomingLoading ? (
            <p className="text-gray-600">Загрузка...</p>
          ) : incomingRequests?.length === 0 ? (
            <p className="text-gray-500">Нет входящих заявок</p>
          ) : (
            <div className="space-y-3">
              {incomingRequests?.map((request) => (
                <FriendRequestCard key={request.id} request={request} type="incoming" />
              ))}
            </div>
          )}
        </section>

        {/* Outgoing Requests */}
        <section className="mb-8">
          <h2 className="text-xl font-semibold mb-4">Исходящие заявки</h2>
          {outgoingLoading ? (
            <p className="text-gray-600">Загрузка...</p>
          ) : outgoingRequests?.length === 0 ? (
            <p className="text-gray-500">Нет исходящих заявок</p>
          ) : (
            <div className="space-y-3">
              {outgoingRequests?.map((request) => (
                <FriendRequestCard key={request.id} request={request} type="outgoing" />
              ))}
            </div>
          )}
        </section>

        {/* Friends List */}
        <section>
          <h2 className="text-xl font-semibold mb-4">Мои друзья</h2>
          {friendsLoading ? (
            <p className="text-gray-600">Загрузка...</p>
          ) : friends?.length === 0 ? (
            <p className="text-gray-500">У вас пока нет друзей</p>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {friends?.map((friend) => (
                <FriendCard key={friend.id} friend={friend} />
              ))}
            </div>
          )}
        </section>
      </div>
    </Layout>
  )
}

function FriendRequestCard({ request, type }: { request: FriendRequest; type: 'incoming' | 'outgoing' }) {
  const acceptRequest = useAcceptRequest()
  const rejectRequest = useRejectOrCancelRequest()

  return (
    <div className="bg-white rounded-lg shadow p-4 flex items-center justify-between">
      <div className="flex items-center">
        <UserAvatar displayName={request.from.displayName} avatarUrl={request.from.avatarUrl} size="md" />
        <div className="ml-3">
          <p className="font-semibold">{request.from.displayName}</p>
          <p className="text-sm text-gray-500">@{request.from.username}</p>
        </div>
      </div>

      {type === 'incoming' ? (
        <div className="space-x-2">
          <button
            onClick={() => acceptRequest.mutateAsync(request.id)}
            disabled={acceptRequest.isPending}
            className="px-3 py-1 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50"
          >
            Принять
          </button>
          <button
            onClick={() => rejectRequest.mutateAsync(request.id)}
            disabled={rejectRequest.isPending}
            className="px-3 py-1 bg-red-600 text-white rounded-md hover:bg-red-700 disabled:opacity-50"
          >
            Отклонить
          </button>
        </div>
      ) : (
        <button
          onClick={() => rejectRequest.mutateAsync(request.id)}
          disabled={rejectRequest.isPending}
          className="px-3 py-1 bg-gray-300 text-gray-700 rounded-md hover:bg-gray-400 disabled:opacity-50"
        >
          Отменить
        </button>
      )}
    </div>
  )
}

function FriendCard({ friend }: { friend: Friendship }) {
  const removeFriend = useRejectOrCancelRequest()

  return (
    <div className="bg-white rounded-lg shadow p-4 flex items-center justify-between">
      <a href={`/profile/${friend.user.username}`} className="flex items-center hover:opacity-80">
        <UserAvatar displayName={friend.user.displayName} avatarUrl={friend.user.avatarUrl} size="md" />
        <div className="ml-3">
          <p className="font-semibold">{friend.user.displayName}</p>
          <p className="text-sm text-gray-500">@{friend.user.username}</p>
        </div>
      </a>

      <button
        onClick={() => removeFriend.mutateAsync(friend.id)}
        disabled={removeFriend.isPending}
        className="px-3 py-1 text-red-600 hover:text-red-700 text-sm"
      >
        Удалить
      </button>
    </div>
  )
}
