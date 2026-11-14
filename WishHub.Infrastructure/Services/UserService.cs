using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Storage;

namespace WishHub.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<UserService> _logger;
    private readonly AvatarStorage _avatars;

    public UserService(
        UserManager<User> userManager,
        AppDbContext dbContext,
        ILogger<UserService> logger,
        AvatarStorage avatars)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _logger = logger;
        _avatars = avatars;
    }

    public Task<UserProfileDto?> GetProfileAsync(string username, Guid? viewerId, CancellationToken ct = default) =>
        ProjectProfiles(_userManager.Users.Where(u => u.UserName == username), viewerId).FirstOrDefaultAsync(ct);

    public Task<UserProfileDto?> GetProfileByIdAsync(Guid userId, Guid? viewerId, CancellationToken ct = default) =>
        ProjectProfiles(_userManager.Users.Where(u => u.Id == userId), viewerId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserProfileDto>> GetProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds, Guid? viewerId, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, UserProfileDto>();

        var ids = userIds.ToArray();
        return await ProjectProfiles(_userManager.Users.Where(u => ids.Contains(u.Id)), viewerId)
            .ToDictionaryAsync(profile => profile.Id, ct);
    }

    public async Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return null;

        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;

        if (request.Bio != null)
            user.Bio = request.Bio;

        if (request.WishlistPrivacy != null)
        {
            user.WishlistPrivacy = Enum.Parse<WishlistPrivacy>(request.WishlistPrivacy, ignoreCase: true);
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to update user profile: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return null;
        }

        return await GetProfileByIdAsync(userId, userId, ct);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        if (_dbContext.Database.IsNpgsql())
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE""", ct);
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return false;

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return false;
        await _dbContext.RefreshTokens.Where(token => token.UserId == userId && !token.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.IsRevoked, true), ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<string?> UploadAvatarAsync(Guid userId, Stream fileStream, string contentType, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        // Keep replacements for one owner ordered across API instances, like wishlist writes.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        if (_dbContext.Database.IsNpgsql())
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return null;
        await _dbContext.Entry(user).ReloadAsync(ct);
        var oldUrl = user.AvatarUrl;
        var avatarUrl = await _avatars.SaveAsync(userId, fileStream, contentType, ct);
        if (avatarUrl is null)
            return null;
        var commitStarted = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            user.AvatarUrl = avatarUrl;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                user.AvatarUrl = oldUrl;
                _logger.LogWarning("Avatar profile update failed for {UserId}: {Errors}", userId,
                    string.Join(", ", result.Errors.Select(error => error.Code)));
                return null;
            }
            // Do not delete a possibly committed file if the database loses the commit response.
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None);
            DeleteAvatarIfOwned(userId, oldUrl);
            return avatarUrl;
        }
        finally
        {
            if (!commitStarted)
                DeleteAvatarIfOwned(userId, avatarUrl);
        }
    }

    private void DeleteAvatarIfOwned(Guid ownerId, string? url)
    {
        try { _avatars.DeleteOwned(ownerId, url); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Unable to remove an obsolete avatar for {UserId}", ownerId);
        }
    }

    public async Task<IReadOnlyList<UserProfileDto>> SearchUsersAsync(string query, Guid? currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Array.Empty<UserProfileDto>();

        query = query.ToLower();
        var users = _userManager.Users
            .Where(u => u.UserName!.ToLower().Contains(query) ||
                        u.DisplayName.ToLower().Contains(query) ||
                        (u.Email != null && u.Email.ToLower().Contains(query)))
            .Take(20);

        return await ProjectProfiles(users, currentUserId).ToListAsync(ct);
    }

    private IQueryable<UserProfileDto> ProjectProfiles(IQueryable<User> users, Guid? viewerId) =>
        from user in users.AsNoTracking()
        let friendshipStatus = viewerId.HasValue && viewerId != user.Id
            ? _dbContext.Friendships
                .Where(f => (f.RequesterId == viewerId && f.AddresseeId == user.Id) ||
                            (f.AddresseeId == viewerId && f.RequesterId == user.Id))
                // Wishlist access recognizes any Accepted row, including legacy reverse pairs.
                .OrderByDescending(f => f.Status == FriendshipStatus.Accepted)
                .ThenByDescending(f => f.Status == FriendshipStatus.Blocked)
                .ThenByDescending(f => f.Status == FriendshipStatus.Pending)
                .Select(f => (FriendshipStatus?)f.Status)
                .FirstOrDefault()
            : null
        select new UserProfileDto
        {
            Id = user.Id,
            Username = user.UserName!,
            DisplayName = user.DisplayName,
            Bio = user.Bio,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt,
            WishlistPrivacy = user.WishlistPrivacy.ToString(),
            FriendshipStatus = friendshipStatus,
            WishlistItemCount = user.WishlistPrivacy == WishlistPrivacy.Public || viewerId == user.Id ||
                (user.WishlistPrivacy == WishlistPrivacy.FriendsOnly && friendshipStatus == FriendshipStatus.Accepted)
                ? user.WishlistItems.Count
                : 0
        };
}
