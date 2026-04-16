using WishHub.Core.DTOs.Friends;

namespace WishHub.Core.Interfaces;

public interface IFriendshipService
{
    Task<FriendshipDto?> SendRequestAsync(Guid requesterId, Guid addresseeId, CancellationToken ct = default);
    Task<FriendshipDto?> AcceptRequestAsync(Guid friendshipId, Guid addresseeId, CancellationToken ct = default);
    Task<bool> RejectOrCancelRequestAsync(Guid friendshipId, Guid currentUserId, CancellationToken ct = default);
    Task<bool> RemoveFriendAsync(Guid userId, Guid friendId, CancellationToken ct = default);
    Task<IReadOnlyList<FriendshipDto>> GetFriendsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<FriendRequestDto>> GetIncomingRequestsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<FriendRequestDto>> GetOutgoingRequestsAsync(Guid userId, CancellationToken ct = default);
}
