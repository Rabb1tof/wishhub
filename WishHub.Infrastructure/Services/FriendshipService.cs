using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WishHub.Core.DTOs.Friends;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class FriendshipService : IFriendshipService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserService _userService;
    private readonly ILogger<FriendshipService> _logger;

    public FriendshipService(AppDbContext dbContext, IUserService userService, ILogger<FriendshipService> logger)
    {
        _dbContext = dbContext;
        _userService = userService;
        _logger = logger;
    }

    public async Task<FriendshipDto?> SendRequestAsync(Guid requesterId, Guid addresseeId, CancellationToken ct = default)
    {
        if (requesterId == addresseeId)
        {
            _logger.LogWarning("User {RequesterId} tried to send friend request to themselves", requesterId);
            return null;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockPairAsync(requesterId, addresseeId, ct);

        // Check both directions after the pair's lock has been acquired.
        var existing = await _dbContext.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == requesterId && f.AddresseeId == addresseeId) ||
                (f.RequesterId == addresseeId && f.AddresseeId == requesterId), ct);

        if (existing != null)
        {
            _logger.LogWarning("Friendship already exists between {RequesterId} and {AddresseeId}", requesterId, addresseeId);
            return null;
        }

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Friendships.Add(friendship);

        // Create notification
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = addresseeId,
            Type = NotificationType.FriendRequest,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { FriendshipId = friendship.Id, RequesterId = requesterId }),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await MapToDtoAsync(friendship, requesterId, ct);
    }

    public async Task<FriendshipDto?> AcceptRequestAsync(Guid friendshipId, Guid addresseeId, CancellationToken ct = default)
    {
        var requesterId = await _dbContext.Friendships.AsNoTracking()
            .Where(f => f.Id == friendshipId && f.AddresseeId == addresseeId)
            .Select(f => (Guid?)f.RequesterId).FirstOrDefaultAsync(ct);
        if (!requesterId.HasValue)
            return null;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockPairAsync(requesterId.Value, addresseeId, ct);

        var friendship = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.AddresseeId == addresseeId && f.Status == FriendshipStatus.Pending, ct);

        if (friendship == null)
            return null;

        friendship.Status = FriendshipStatus.Accepted;
        friendship.UpdatedAt = DateTime.UtcNow;

        // Create notification for requester
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = friendship.RequesterId,
            Type = NotificationType.FriendRequestAccepted,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { FriendshipId = friendship.Id, AcceptorId = addresseeId }),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await MapToDtoAsync(friendship, addresseeId, ct);
    }

    public async Task<bool> RejectOrCancelRequestAsync(Guid friendshipId, Guid currentUserId, CancellationToken ct = default)
    {
        var pair = await _dbContext.Friendships.AsNoTracking()
            .Where(f => f.Id == friendshipId &&
                (f.RequesterId == currentUserId || f.AddresseeId == currentUserId))
            .Select(f => new { f.RequesterId, f.AddresseeId }).FirstOrDefaultAsync(ct);
        if (pair == null)
            return false;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockPairAsync(pair.RequesterId, pair.AddresseeId, ct);

        var friendship = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId &&
                (f.RequesterId == currentUserId || f.AddresseeId == currentUserId), ct);

        if (friendship == null)
            return false;

        if (friendship.Status == FriendshipStatus.Accepted)
        {
            var pairRows = await _dbContext.Friendships.Where(f =>
                (f.RequesterId == pair.RequesterId && f.AddresseeId == pair.AddresseeId) ||
                (f.RequesterId == pair.AddresseeId && f.AddresseeId == pair.RequesterId)).ToListAsync(ct);
            _dbContext.Friendships.RemoveRange(pairRows);
        }
        else
        {
            _dbContext.Friendships.Remove(friendship);
        }
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return true;
    }

    public async Task<bool> RemoveFriendAsync(Guid userId, Guid friendId, CancellationToken ct = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockPairAsync(userId, friendId, ct);

        var friendships = await _dbContext.Friendships
            .Where(f => (f.RequesterId == userId && f.AddresseeId == friendId) ||
                        (f.RequesterId == friendId && f.AddresseeId == userId))
            .ToListAsync(ct);

        if (!friendships.Any(f => f.Status == FriendshipStatus.Accepted))
            return false;

        // Legacy reverse rows, including pending requests, must not restore revoked access.
        _dbContext.Friendships.RemoveRange(friendships);
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return true;
    }

    private async Task LockPairAsync(Guid firstUserId, Guid secondUserId, CancellationToken ct)
    {
        // Database order is identical for A/B and B/A, including across API instances.
        if (_dbContext.Database.IsNpgsql())
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {firstUserId} OR \"Id\" = {secondUserId} ORDER BY \"Id\" FOR UPDATE", ct);
    }

    public async Task<IReadOnlyList<FriendshipDto>> GetFriendsAsync(Guid userId, CancellationToken ct = default)
    {
        var friendships = await _dbContext.Friendships
            .AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == userId || f.AddresseeId == userId))
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(ct);

        var result = new List<FriendshipDto>();
        foreach (var friendship in friendships)
        {
            result.Add(await MapToDtoAsync(friendship, userId, ct));
        }

        return result;
    }

    public async Task<IReadOnlyList<FriendRequestDto>> GetIncomingRequestsAsync(Guid userId, CancellationToken ct = default)
    {
        var friendships = await _dbContext.Friendships
            .AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending && f.AddresseeId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var result = new List<FriendRequestDto>();
        foreach (var friendship in friendships)
        {
            var fromUser = await _userService.GetProfileByIdAsync(friendship.RequesterId, userId, ct);
            if (fromUser != null)
            {
                result.Add(new FriendRequestDto
                {
                    Id = friendship.Id,
                    From = fromUser,
                    CreatedAt = friendship.CreatedAt
                });
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<FriendRequestDto>> GetOutgoingRequestsAsync(Guid userId, CancellationToken ct = default)
    {
        var friendships = await _dbContext.Friendships
            .AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending && f.RequesterId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var result = new List<FriendRequestDto>();
        foreach (var friendship in friendships)
        {
            var toUser = await _userService.GetProfileByIdAsync(friendship.AddresseeId, userId, ct);
            if (toUser != null)
            {
                result.Add(new FriendRequestDto
                {
                    Id = friendship.Id,
                    From = toUser,
                    CreatedAt = friendship.CreatedAt
                });
            }
        }

        return result;
    }

    private async Task<FriendshipDto> MapToDtoAsync(Friendship friendship, Guid currentUserId, CancellationToken ct = default)
    {
        var otherUserId = friendship.RequesterId == currentUserId ? friendship.AddresseeId : friendship.RequesterId;
        var otherUser = await _userService.GetProfileByIdAsync(otherUserId, currentUserId, ct);

        return new FriendshipDto
        {
            Id = friendship.Id,
            User = otherUser!,
            Status = friendship.Status.ToString(),
            CreatedAt = friendship.CreatedAt
        };
    }
}
