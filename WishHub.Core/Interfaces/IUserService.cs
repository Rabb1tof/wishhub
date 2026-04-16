using WishHub.Core.DTOs.Users;

namespace WishHub.Core.Interfaces;

public interface IUserService
{
    Task<UserProfileDto?> GetProfileAsync(string username, Guid? viewerId, CancellationToken ct = default);
    Task<UserProfileDto?> GetProfileByIdAsync(Guid userId, Guid? viewerId, CancellationToken ct = default);
    Task<UserProfileDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<string?> UploadAvatarAsync(Guid userId, Stream fileStream, string contentType, string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<UserProfileDto>> SearchUsersAsync(string query, Guid? currentUserId, CancellationToken ct = default);
}
