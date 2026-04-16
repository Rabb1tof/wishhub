using WishHub.Core.Entities;

namespace WishHub.Core.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId);
    System.Security.Claims.ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
