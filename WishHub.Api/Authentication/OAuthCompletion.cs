namespace WishHub.Api.Authentication;

internal static class OAuthCompletion
{
    public const string CookieName = "wishhub-oauth-completion";
    // Purpose-separated, one-use handoff reuses the refresh-token table without a new schema.
    public const string TokenPrefix = "oauth.";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    public static CookieOptions CookieOptions(HttpRequest request, IConfiguration configuration) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = request.IsHttps ||
            (Uri.TryCreate(configuration["Frontend:Url"], UriKind.Absolute, out var frontend) && frontend.Scheme == "https"),
        Path = "/api/auth/complete"
    };
}
