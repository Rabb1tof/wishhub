namespace WishHub.Api.Authentication;

internal static class OAuthCompletion
{
    public const string CookieName = "wishhub-oauth-completion";
    // Purpose-separated, one-use handoff reuses the refresh-token table without a new schema.
    public const string VkStateCookieName = "vk_oauth_state";
    public const string VkVerifierCookieName = "vk_oauth_verifier";
    public const string VkLinkCookieName = "vk_oauth_link_user";
    public const string TokenPrefix = "oauth.";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    public static CookieOptions CookieOptions(HttpRequest request, IConfiguration configuration) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = UseSecureCookies(request, configuration),
        Path = "/api/auth/complete"
    };

    public static CookieOptions StateCookieOptions(HttpRequest request, IConfiguration configuration, TimeSpan lifetime) => new()
    {
        HttpOnly = true, Secure = UseSecureCookies(request, configuration),
        SameSite = SameSiteMode.Lax, Path = "/", Expires = DateTimeOffset.UtcNow.Add(lifetime)
    };

    public static void ClearPending(HttpRequest request, HttpResponse response, IConfiguration configuration)
    {
        response.Cookies.Delete(CookieName, CookieOptions(request, configuration));
        var stateOptions = StateCookieOptions(request, configuration, TimeSpan.Zero);
        foreach (var name in new[] { VkStateCookieName, VkVerifierCookieName, VkLinkCookieName })
            response.Cookies.Delete(name, stateOptions);
    }

    private static bool UseSecureCookies(HttpRequest request, IConfiguration configuration) =>
        request.IsHttps || (Uri.TryCreate(configuration["Frontend:Url"], UriKind.Absolute, out var frontend) && frontend.Scheme == "https");
}
