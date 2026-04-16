using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<UserService> _logger;
    private readonly string _avatarUploadPath;

    public UserService(
        UserManager<User> userManager,
        AppDbContext dbContext,
        ILogger<UserService> logger,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _logger = logger;
        _avatarUploadPath = configuration["AvatarUploadPath"] ?? "wwwroot/avatars";
    }

    public async Task<UserProfileDto?> GetProfileAsync(string username, Guid? viewerId, CancellationToken ct = default)
    {
        var user = await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == username, ct);

        if (user == null)
            return null;

        return await MapToDtoAsync(user, viewerId, ct);
    }

    public async Task<UserProfileDto?> GetProfileByIdAsync(Guid userId, Guid? viewerId, CancellationToken ct = default)
    {
        var user = await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
            return null;

        return await MapToDtoAsync(user, viewerId, ct);
    }

    public async Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return null;

        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;

        if (request.Bio != null)
            user.Bio = request.Bio;

        if (request.WishlistPrivacy != null &&
            Enum.TryParse<WishlistPrivacy>(request.WishlistPrivacy, ignoreCase: true, out var privacy))
        {
            user.WishlistPrivacy = privacy;
        }

        if (request.PriceRefreshIntervalHours.HasValue)
        {
            user.PriceRefreshIntervalHours = Math.Clamp(request.PriceRefreshIntervalHours.Value, 1, 168);
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to update user profile: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return null;
        }

        return await MapToDtoAsync(user, userId, ct);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return false;

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded;
    }

    public async Task<string?> UploadAvatarAsync(Guid userId, Stream fileStream, string contentType, string fileName, CancellationToken ct = default)
    {
        if (fileStream == null || fileStream.Length == 0)
            return null;

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(contentType))
        {
            _logger.LogWarning("Invalid avatar file type: {ContentType}", contentType);
            return null;
        }

        // Max 5MB
        if (fileStream.Length > 5 * 1024 * 1024)
        {
            _logger.LogWarning("Avatar file too large: {Size} bytes", fileStream.Length);
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return null;

        // Ensure upload directory exists
        var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), _avatarUploadPath);
        Directory.CreateDirectory(uploadPath);

        // Generate unique filename
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var uniqueFileName = $"{userId}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadPath, uniqueFileName);

        // Save file
        using (var stream = File.Create(filePath))
        {
            await fileStream.CopyToAsync(stream, ct);
        }

        // Update user avatar URL
        var avatarUrl = $"/avatars/{uniqueFileName}";
        user.AvatarUrl = avatarUrl;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Avatar uploaded for user {UserId}: {AvatarUrl}", userId, avatarUrl);
        return avatarUrl;
    }

    public async Task<IReadOnlyList<UserProfileDto>> SearchUsersAsync(string query, Guid? currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Array.Empty<UserProfileDto>();

        query = query.ToLower();

        var users = await _userManager.Users
            .AsNoTracking()
            .Where(u => u.UserName!.ToLower().Contains(query) ||
                        u.DisplayName.ToLower().Contains(query) ||
                        (u.Email != null && u.Email.ToLower().Contains(query)))
            .Take(20)
            .ToListAsync(ct);

        var results = new List<UserProfileDto>();
        foreach (var user in users)
        {
            results.Add(await MapToDtoAsync(user, currentUserId, ct));
        }

        return results;
    }

    private async Task<UserProfileDto> MapToDtoAsync(User user, Guid? viewerId, CancellationToken ct = default)
    {
        // Get friendship status
        FriendshipStatus? friendshipStatus = null;
        if (viewerId.HasValue && viewerId.Value != user.Id)
        {
            var friendship = await _dbContext.Friendships
                .AsNoTracking()
                .FirstOrDefaultAsync(f =>
                    (f.RequesterId == viewerId.Value && f.AddresseeId == user.Id) ||
                    (f.AddresseeId == viewerId.Value && f.RequesterId == user.Id), ct);

            friendshipStatus = friendship?.Status;
        }

        // Count wishlist items — скрываем для тех, у кого нет доступа
        var canViewWishlist = user.WishlistPrivacy == WishlistPrivacy.Public
            || (viewerId.HasValue && viewerId.Value == user.Id)
            || (user.WishlistPrivacy == WishlistPrivacy.FriendsOnly && friendshipStatus == FriendshipStatus.Accepted);

        var wishlistCount = canViewWishlist
            ? await _dbContext.WishlistItems
                .AsNoTracking()
                .CountAsync(w => w.OwnerId == user.Id, ct)
            : 0;

        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.UserName!,
            DisplayName = user.DisplayName,
            Bio = user.Bio,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt,
            WishlistPrivacy = user.WishlistPrivacy.ToString(),
            FriendshipStatus = friendshipStatus,
            WishlistItemCount = wishlistCount,
            PriceRefreshIntervalHours = user.PriceRefreshIntervalHours
        };
    }
}
