using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;

namespace WishHub.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(User user)
    {
        var secret = GetRequiredConfigurationValue("Jwt:Secret");
        var issuer = GetRequiredConfigurationValue("Jwt:Issuer");
        var audience = GetRequiredConfigurationValue("Jwt:Audience");
        var expiryMinutes = GetRequiredPositiveIntConfigurationValue("Jwt:AccessTokenExpiryMinutes");

        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.Email, user.Email!)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(Guid userId)
    {
        var expiryDays = GetRequiredPositiveIntConfigurationValue("Jwt:RefreshTokenExpiryDays");

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secret = GetRequiredConfigurationValue("Jwt:Secret");
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    private string GetRequiredConfigurationValue(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required configuration value '{key}' is missing.");
        }

        return value;
    }

    private int GetRequiredPositiveIntConfigurationValue(string key)
    {
        var value = GetRequiredConfigurationValue(key);
        if (!int.TryParse(value, out var parsedValue) || parsedValue <= 0)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be a positive integer.");
        }

        return parsedValue;
    }
}
