using System.IdentityModel.Tokens.Jwt;
using WishHub.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WishHub.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Auth;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _dbContext;

    public AuthController(UserManager<User> userManager, ITokenService tokenService, AppDbContext dbContext)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _dbContext = dbContext;
    }

    [HttpPost("register")]
    [EnableRateLimiting(PasswordRequestLimits.Registration)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl
            }
        });
    }

    [HttpPost("login")]
    [EnableRateLimiting(PasswordRequestLimits.Login)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.UserName == request.UsernameOrEmail || u.Email == request.UsernameOrEmail);

        if (user == null)
        {
            return Unauthorized(new { Error = "Invalid credentials" });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockUserAsync(user.Id, ct);
        await _dbContext.Entry(user).ReloadAsync(ct);
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized(new { Error = "Invalid credentials" });
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl
            }
        });
    }

    [HttpPost("refresh")]
    public Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct = default) =>
        RotateAsync(request.RefreshToken, completion: false, ct);

    [HttpPost("complete")]
    [AllowAnonymous]
    public Task<ActionResult<AuthResponse>> Complete(
        [FromHeader(Name = "X-WishHub-OAuth")] string? marker,
        [FromServices] IConfiguration configuration, CancellationToken ct)
    {
        // The custom header prevents a cross-origin form from consuming this cookie.
        if (marker != "1")
            return Task.FromResult<ActionResult<AuthResponse>>(BadRequest(new { Error = "Invalid login completion request" }));
        var token = Request.Cookies[OAuthCompletion.CookieName];
        Response.Cookies.Delete(OAuthCompletion.CookieName, OAuthCompletion.CookieOptions(Request, configuration));
        return string.IsNullOrEmpty(token)
            ? Task.FromResult<ActionResult<AuthResponse>>(Unauthorized(new { Error = "Login completion expired" }))
            : RotateAsync(token, completion: true, ct);
    }

    private async Task<ActionResult<AuthResponse>> RotateAsync(string token, bool completion, CancellationToken ct)
    {
        if (token.StartsWith(OAuthCompletion.TokenPrefix, StringComparison.Ordinal) != completion)
            return Unauthorized(new { Error = "Invalid refresh token" });
        var refreshToken = await _dbContext.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow, ct);

        if (refreshToken == null)
        {
            return Unauthorized(new { Error = "Invalid refresh token" });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockUserAsync(refreshToken.UserId, ct);
        var user = await _userManager.FindByIdAsync(refreshToken.UserId.ToString());
        if (user == null)
        {
            return Unauthorized(new { Error = "User not found" });
        }

        // Conditional consumption remains atomic after any wait for the owner lock.
        var now = DateTime.UtcNow;
        var consumed = await _dbContext.RefreshTokens.Where(rt => rt.Id == refreshToken.Id &&
                !rt.IsRevoked && rt.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true), ct);
        if (consumed != 1)
            return Unauthorized(new { Error = "Invalid refresh token" });

        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = new JwtSecurityTokenHandler().ReadJwtToken(newAccessToken).ValidTo,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl
            }
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromServices] IConfiguration configuration, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Unauthorized();
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        await LockUserAsync(Guid.Parse(userId), ct);
        await _dbContext.RefreshTokens.Where(rt => rt.UserId == Guid.Parse(userId) && !rt.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true), ct);
        await transaction.CommitAsync(ct);
        OAuthCompletion.ClearPending(Request, Response, configuration);
        return NoContent();
    }

    private Task LockUserAsync(Guid userId, CancellationToken ct) => _dbContext.Database.IsNpgsql()
        ? _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE""", ct)
        : Task.CompletedTask;
}
