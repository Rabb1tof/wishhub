using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Auth;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class OAuthController : ControllerBase
{
    private const string VkStateCookie = "vk_oauth_state";
    private const string VkVerifierCookie = "vk_oauth_verifier";
    private const string VkLinkUserCookie = "vk_oauth_link_user";
    private const string ProviderVk = "vk";
    private const string ProviderTelegram = "telegram";

    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthController> _logger;
    private readonly HttpClient _httpClient;

    public OAuthController(
        UserManager<User> userManager,
        ITokenService tokenService,
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<OAuthController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    #region VK OAuth

    /// <summary>
    /// Начать вход через VK ID. Редиректим на id.vk.com/authorize с PKCE.
    /// </summary>
    [HttpGet("vk")]
    public IActionResult VkAuthorize()
    {
        if (!IsVkConfigured())
        {
            return RedirectToFrontend("/login", ("error", "vk_not_configured"));
        }

        var (state, challenge) = PrepareVkAuthCookies();
        return Redirect(BuildVkAuthUrl(state, challenge));
    }

    /// <summary>
    /// Начало flow привязки VK-аккаунта к уже авторизованному пользователю.
    /// Возвращает URL авторизации + ставит куки, чтобы callback знал про link-флоу.
    /// Фронт делает window.location.href = response.url.
    /// </summary>
    [Authorize]
    [HttpPost("link/vk/start")]
    public ActionResult<OAuthStartResponse> StartVkLink()
    {
        if (!IsVkConfigured())
        {
            return BadRequest(new { error = "VK OAuth not configured" });
        }

        var (state, challenge) = PrepareVkAuthCookies();

        var userId = GetCurrentUserId();
        var signed = SignUserId(userId);
        Response.Cookies.Append(VkLinkUserCookie, signed, BuildCookieOptions(TimeSpan.FromMinutes(10)));

        return new OAuthStartResponse(BuildVkAuthUrl(state, challenge));
    }

    /// <summary>
    /// Универсальный VK callback. Если установлена кука с userId — это привязка
    /// к существующему пользователю. Иначе — логин/регистрация.
    /// </summary>
    [HttpGet("vk/callback")]
    public async Task<IActionResult> VkCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery(Name = "device_id")] string? deviceId,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        // Любой выход — чистим куки состояния
        var linkUserSigned = Request.Cookies[VkLinkUserCookie];
        var storedState = Request.Cookies[VkStateCookie];
        var codeVerifier = Request.Cookies[VkVerifierCookie];
        Response.Cookies.Delete(VkStateCookie);
        Response.Cookies.Delete(VkVerifierCookie);
        Response.Cookies.Delete(VkLinkUserCookie);

        if (!string.IsNullOrEmpty(error))
        {
            return linkUserSigned != null
                ? RedirectToFrontend("/settings", ("error", error))
                : RedirectToFrontend("/login", ("error", error));
        }

        if (string.IsNullOrEmpty(code) ||
            string.IsNullOrEmpty(state) ||
            storedState != state ||
            string.IsNullOrEmpty(codeVerifier) ||
            string.IsNullOrEmpty(deviceId))
        {
            return linkUserSigned != null
                ? RedirectToFrontend("/settings", ("error", "invalid_state"))
                : RedirectToFrontend("/login", ("error", "invalid_state"));
        }

        VkTokenResponse? tokenData;
        try
        {
            tokenData = await ExchangeVkCodeAsync(code, state, codeVerifier, deviceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VK token exchange failed");
            return RedirectToFrontend(linkUserSigned != null ? "/settings" : "/login", ("error", "vk_token_failed"));
        }

        if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
        {
            return RedirectToFrontend(linkUserSigned != null ? "/settings" : "/login", ("error", "vk_no_token"));
        }

        // Тянем профиль VK ID, чтобы узнать email / имя / аватар
        VkUserInfo? userInfo = null;
        try
        {
            userInfo = await FetchVkUserInfoAsync(tokenData.AccessToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VK user_info fetch failed, continuing without profile");
        }

        var externalId = tokenData.UserId.ToString();
        var email = userInfo?.Email;

        // === LINK FLOW ===
        if (linkUserSigned != null)
        {
            var userId = TryVerifySignedUserId(linkUserSigned);
            if (userId == null)
            {
                return RedirectToFrontend("/settings", ("error", "invalid_link_state"));
            }

            var conflict = await _dbContext.ExternalLogins
                .FirstOrDefaultAsync(e => e.Provider == ProviderVk && e.ExternalId == externalId, ct);

            if (conflict != null && conflict.UserId != userId)
            {
                return RedirectToFrontend("/settings", ("error", "vk_already_linked"));
            }

            if (conflict == null)
            {
                _dbContext.ExternalLogins.Add(new UserExternalLogin
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    Provider = ProviderVk,
                    ExternalId = externalId,
                    AccessToken = tokenData.AccessToken,
                    LinkedAt = DateTime.UtcNow,
                });
                await _dbContext.SaveChangesAsync(ct);
            }

            return RedirectToFrontend("/settings", ("linked", ProviderVk));
        }

        // === LOGIN / REGISTER FLOW ===
        var existingLogin = await _dbContext.ExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == ProviderVk && e.ExternalId == externalId, ct);

        User? user;
        if (existingLogin != null)
        {
            user = await _userManager.FindByIdAsync(existingLogin.UserId.ToString());
            if (user == null)
            {
                return RedirectToFrontend("/login", ("error", "user_not_found"));
            }
        }
        else
        {
            // Если email уже есть в системе — привязываем VK к существующему пользователю
            if (!string.IsNullOrEmpty(email))
            {
                user = await _userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    _dbContext.ExternalLogins.Add(new UserExternalLogin
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        Provider = ProviderVk,
                        ExternalId = externalId,
                        AccessToken = tokenData.AccessToken,
                        LinkedAt = DateTime.UtcNow,
                    });
                    await _dbContext.SaveChangesAsync(ct);
                    return await IssueTokensAndRedirectAsync(user, ct);
                }
            }

            // Иначе создаём нового пользователя
            var emailForDb = email ?? $"vk_{tokenData.UserId}@wishhub.local";
            var userName = $"vk_user_{tokenData.UserId}";
            var displayName = BuildDisplayName(userInfo?.FirstName, userInfo?.LastName) ?? userName;

            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = emailForDb,
                DisplayName = displayName,
                AvatarUrl = userInfo?.Avatar,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = !string.IsNullOrEmpty(email),
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                _logger.LogError("Failed to create VK user: {Errors}",
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
                return RedirectToFrontend("/login", ("error", "user_create_failed"));
            }

            _dbContext.ExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = ProviderVk,
                ExternalId = externalId,
                AccessToken = tokenData.AccessToken,
                LinkedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync(ct);
        }

        return await IssueTokensAndRedirectAsync(user, ct);
    }

    #endregion

    #region Telegram Login Widget

    public record TelegramAuthRequest(
        long Id,
        string FirstName,
        string? LastName,
        string? Username,
        string? PhotoUrl,
        string? AuthDate,
        string? Hash
    );

    /// <summary>
    /// Логин через Telegram Login Widget. Виджет на фронте подписывает данные,
    /// фронт POST'ит их сюда. Возвращает обычный AuthResponse.
    /// </summary>
    [HttpPost("telegram")]
    public async Task<ActionResult<AuthResponse>> TelegramLogin(
        [FromBody] TelegramAuthRequest request,
        CancellationToken ct)
    {
        if (!VerifyTelegramAuth(request))
        {
            return BadRequest(new { error = "Invalid Telegram auth data" });
        }

        var telegramId = request.Id.ToString();
        var existingLogin = await _dbContext.ExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == ProviderTelegram && e.ExternalId == telegramId, ct);

        User? user;
        if (existingLogin != null)
        {
            user = await _userManager.FindByIdAsync(existingLogin.UserId.ToString());
            if (user == null)
            {
                return BadRequest(new { error = "User not found" });
            }
        }
        else
        {
            var userName = !string.IsNullOrEmpty(request.Username) ? request.Username : $"tg_{request.Id}";
            var email = $"tg_{request.Id}@wishhub.local";
            var displayName = string.IsNullOrWhiteSpace(request.LastName)
                ? request.FirstName
                : $"{request.FirstName} {request.LastName}".Trim();

            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                DisplayName = displayName,
                AvatarUrl = request.PhotoUrl,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true,
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return BadRequest(new
                {
                    error = "Failed to create user",
                    errors = createResult.Errors.Select(e => e.Description),
                });
            }

            _dbContext.ExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = ProviderTelegram,
                ExternalId = telegramId,
                LinkedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync(ct);
        }

        return await GenerateAuthResponseAsync(user, ct);
    }

    /// <summary>
    /// Привязка Telegram-аккаунта к текущему пользователю. Виджет также подписывает данные.
    /// </summary>
    [Authorize]
    [HttpPost("link/telegram")]
    public async Task<IActionResult> LinkTelegram(
        [FromBody] TelegramAuthRequest request,
        CancellationToken ct)
    {
        if (!VerifyTelegramAuth(request))
        {
            return BadRequest(new { error = "Invalid Telegram auth data" });
        }

        var userId = GetCurrentUserId();
        var telegramId = request.Id.ToString();

        var existing = await _dbContext.ExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == ProviderTelegram && e.ExternalId == telegramId, ct);

        if (existing != null && existing.UserId != userId)
        {
            return Conflict(new { error = "This Telegram account is already linked to another user" });
        }

        if (existing == null)
        {
            _dbContext.ExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = ProviderTelegram,
                ExternalId = telegramId,
                LinkedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    #endregion

    #region External logins management

    /// <summary>
    /// Публичная информация о доступных OAuth-провайдерах. Фронт дергает на загрузке,
    /// чтобы знать, какие кнопки показывать и какой username у Telegram-бота.
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<OAuthProvidersDto> GetProviders()
    {
        return new OAuthProvidersDto(
            Vk: IsVkConfigured(),
            TelegramBotUsername: string.IsNullOrEmpty(_configuration["OAuth:Telegram:BotToken"])
                ? null
                : _configuration["OAuth:Telegram:BotUsername"]);
    }

    /// <summary>
    /// Список привязанных внешних провайдеров текущего пользователя.
    /// </summary>
    [Authorize]
    [HttpGet("external-logins")]
    public async Task<ActionResult<ExternalLoginsResponse>> GetExternalLogins(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var logins = await _dbContext.ExternalLogins
            .Where(e => e.UserId == userId)
            .Select(e => new ExternalLoginDto(e.Provider, e.ExternalId, e.LinkedAt))
            .ToListAsync(ct);

        var hasPassword = await _userManager.HasPasswordAsync(user);

        return new ExternalLoginsResponse(logins, hasPassword);
    }

    /// <summary>
    /// Отвязать провайдера. Нельзя отвязать единственный способ входа.
    /// </summary>
    [Authorize]
    [HttpDelete("unlink/{provider}")]
    public async Task<IActionResult> UnlinkProvider(string provider, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        var userId = GetCurrentUserId();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var hasPassword = await _userManager.HasPasswordAsync(user);
        var otherLogins = await _dbContext.ExternalLogins
            .CountAsync(e => e.UserId == userId && e.Provider != provider, ct);

        if (!hasPassword && otherLogins == 0)
        {
            return BadRequest(new { error = "Cannot unlink the only authentication method" });
        }

        var login = await _dbContext.ExternalLogins
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Provider == provider, ct);

        if (login == null)
        {
            return NotFound();
        }

        _dbContext.ExternalLogins.Remove(login);
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    #endregion

    #region Helpers

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    private bool IsVkConfigured()
    {
        return !string.IsNullOrEmpty(_configuration["OAuth:Vk:ClientId"]) &&
               !string.IsNullOrEmpty(_configuration["OAuth:Vk:ClientSecret"]) &&
               !string.IsNullOrEmpty(_configuration["OAuth:Vk:RedirectUri"]);
    }

    private string BuildVkAuthUrl(string state, string codeChallenge)
    {
        var clientId = _configuration["OAuth:Vk:ClientId"];
        var redirectUri = _configuration["OAuth:Vk:RedirectUri"];
        var qp = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "email vkid.personal_info",
        };
        return QueryHelpers.AddQueryString("https://id.vk.com/authorize", qp);
    }

    private (string state, string codeChallenge) PrepareVkAuthCookies()
    {
        var stateBytes = RandomNumberGenerator.GetBytes(16);
        var state = Convert.ToHexString(stateBytes).ToLowerInvariant();

        // PKCE: code_verifier — 48 случайных байт в base64url (~64 символа)
        var verifierBytes = RandomNumberGenerator.GetBytes(48);
        var codeVerifier = Base64UrlEncode(verifierBytes);
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var cookieOpts = BuildCookieOptions(TimeSpan.FromMinutes(10));
        Response.Cookies.Append(VkStateCookie, state, cookieOpts);
        Response.Cookies.Append(VkVerifierCookie, codeVerifier, cookieOpts);

        return (state, codeChallenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? BuildDisplayName(string? firstName, string? lastName)
    {
        var full = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    private CookieOptions BuildCookieOptions(TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps, // в dev (http) = false, иначе не отправится
        SameSite = SameSiteMode.Lax, // нужен Lax для редиректа с внешнего сайта
        Expires = DateTimeOffset.UtcNow.Add(lifetime),
        Path = "/",
    };

    private async Task<VkTokenResponse?> ExchangeVkCodeAsync(
        string code,
        string state,
        string codeVerifier,
        string deviceId,
        CancellationToken ct)
    {
        var clientId = _configuration["OAuth:Vk:ClientId"];
        var redirectUri = _configuration["OAuth:Vk:RedirectUri"];

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri!,
            ["client_id"] = clientId!,
            ["device_id"] = deviceId,
            ["state"] = state,
        };

        var response = await _httpClient.PostAsync(
            "https://id.vk.com/oauth2/auth",
            new FormUrlEncodedContent(form),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("VK ID token endpoint returned {Status}: {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<VkTokenResponse>(cancellationToken: ct);
    }

    private async Task<VkUserInfo?> FetchVkUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var clientId = _configuration["OAuth:Vk:ClientId"];

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId!,
            ["access_token"] = accessToken,
        };

        var response = await _httpClient.PostAsync(
            "https://id.vk.com/oauth2/user_info",
            new FormUrlEncodedContent(form),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("VK ID user_info returned {Status}: {Body}", response.StatusCode, body);
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<VkUserInfoEnvelope>(cancellationToken: ct);
        return envelope?.User;
    }

    private async Task<IActionResult> IssueTokensAndRedirectAsync(User user, CancellationToken ct)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(ct);

        return RedirectToFrontend("/auth/callback",
            ("accessToken", accessToken),
            ("refreshToken", refreshToken.Token),
            ("expiresAt", DateTime.UtcNow.AddMinutes(60).ToString("O")));
    }

    private async Task<ActionResult<AuthResponse>> GenerateAuthResponseAsync(User user, CancellationToken ct)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
            },
        };
    }

    private IActionResult RedirectToFrontend(string path, params (string key, string value)[] query)
    {
        var baseUrl = _configuration["Frontend:Url"] ?? "http://localhost:5173";
        var url = $"{baseUrl.TrimEnd('/')}{path}";
        if (query.Length > 0)
        {
            var dict = query.ToDictionary(x => x.key, x => (string?)x.value);
            url = QueryHelpers.AddQueryString(url, dict);
        }
        return Redirect(url);
    }

    private bool VerifyTelegramAuth(TelegramAuthRequest request)
    {
        var botToken = _configuration["OAuth:Telegram:BotToken"];
        if (string.IsNullOrEmpty(botToken))
        {
            _logger.LogError("Telegram BotToken not configured");
            return false;
        }

        // Проверка TTL подписи (Telegram рекомендует максимум ~24 часа)
        if (long.TryParse(request.AuthDate, out var authDateSec))
        {
            var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateSec);
            if (DateTimeOffset.UtcNow - authDate > TimeSpan.FromHours(24))
            {
                return false;
            }
        }

        // Поля в алфавитном порядке, только непустые (кроме hash)
        var fields = new List<string>
        {
            $"auth_date={request.AuthDate}",
            $"first_name={request.FirstName}",
            $"id={request.Id}",
        };
        if (!string.IsNullOrEmpty(request.LastName)) fields.Add($"last_name={request.LastName}");
        if (!string.IsNullOrEmpty(request.PhotoUrl)) fields.Add($"photo_url={request.PhotoUrl}");
        if (!string.IsNullOrEmpty(request.Username)) fields.Add($"username={request.Username}");
        fields.Sort(StringComparer.Ordinal);
        var dataCheckString = string.Join("\n", fields);

        using var sha256 = SHA256.Create();
        var secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        var computedHashString = Convert.ToHexString(computedHash).ToLowerInvariant();

        return !string.IsNullOrEmpty(request.Hash) &&
               computedHashString == request.Hash.ToLowerInvariant();
    }

    private string SignUserId(Guid userId)
    {
        // userId.ts{unix}.hmac(secret, "userId.ts{unix}")
        var secret = _configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured");
        var payload = $"{userId}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"{payload}.{sig}";
    }

    private Guid? TryVerifySignedUserId(string signed)
    {
        var parts = signed.Split('.');
        if (parts.Length != 3) return null;

        if (!Guid.TryParse(parts[0], out var userId)) return null;
        if (!long.TryParse(parts[1], out var ts)) return null;

        var issued = DateTimeOffset.FromUnixTimeSeconds(ts);
        if (DateTimeOffset.UtcNow - issued > TimeSpan.FromMinutes(15)) return null;

        var secret = _configuration["Jwt:Secret"];
        if (string.IsNullOrEmpty(secret)) return null;

        var payload = $"{parts[0]}.{parts[1]}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(parts[2]))
            ? userId : null;
    }

    private class VkTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    private class VkUserInfoEnvelope
    {
        [JsonPropertyName("user")]
        public VkUserInfo? User { get; set; }
    }

    private class VkUserInfo
    {
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }

    public record OAuthStartResponse(string Url);

    public record OAuthProvidersDto(bool Vk, string? TelegramBotUsername);

    public record ExternalLoginDto(string Provider, string ExternalId, DateTime LinkedAt);

    public record ExternalLoginsResponse(IReadOnlyList<ExternalLoginDto> Logins, bool HasPassword);

    #endregion
}
