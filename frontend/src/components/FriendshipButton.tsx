import { useSendRequest, useAcceptRequest, useRejectOrCancelRequest } from '@/api/useFriends'

interface FriendshipButtonProps {
  friendshipStatus: 'pending' | 'accepted' | 'blocked' | null
  userId: string
  friendshipId?: string
}

export function FriendshipButton({ friendshipStatus, userId, friendshipId }: FriendshipButtonProps) {
  const sendRequest = useSendRequest()
  const acceptRequest = useAcceptRequest()
  const rejectRequest = useRejectOrCancelRequest()
  const removeFriend = useRejectOrCancelRequest()

  if (friendshipStatus === null) {
    return (
      <button
        onClick={() => sendRequest.mutateAsync(userId)}
        disabled={sendRequest.isPending}
        className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
      >
        Добавить в друзья
      </button>
    )
  }

  if (friendshipStatus === 'pending' && friendshipId) {
    return (
      <div className="space-x-2">
        <button
          onClick={() => acceptRequest.mutateAsync(friendshipId)}
          disabled={acceptRequest.isPending}
          className="px-3 py-1 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50"
        >
          Принять
        </button>
        <button
          onClick={() => rejectRequest.mutateAsync(friendshipId)}
          disabled={rejectRequest.isPending}
          className="px-3 py-1 bg-red-600 text-white rounded-md hover:bg-red-700 disabled:opacity-50"
        >
          Отклонить
        </button>
      </div>
    )
  }

  if (friendshipStatus === 'accepted' && friendshipId) {
    return (
      <button
        onClick={() => removeFriend.mutateAsync(friendshipId)}
        disabled={removeFriend.isPending}
        className="px-3 py-1 text-red-600 hover:text-red-700 text-sm"
      >
        Удалить из друзей
      </button>
    )
  }

  return null
}
