using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace WishHub.Api.Extensions;

public static class PasswordRequestLimits
{
    public const string Login = "password-login";
    public const string Registration = "password-registration";

    public static IServiceCollection AddPasswordRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var trustedProxies = (configuration["ReverseProxy:KnownProxies"] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(IPAddress.Parse).ToArray();
        var sharedQuota = configuration.GetValue<bool>("ReverseProxy:Enabled") && trustedProxies.Length == 0;
        if (trustedProxies.Length > 0)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                options.ForwardLimit = configuration.GetValue("ReverseProxy:ForwardLimit", 1);
                if (options.ForwardLimit is < 1 or > 4)
                    throw new InvalidOperationException("ReverseProxy:ForwardLimit must be between 1 and 4.");
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var proxy in trustedProxies)
                    options.KnownProxies.Add(proxy);
            });
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                return policy is Login or Registration
                    ? RateLimitPartition.GetConcurrencyLimiter("password-work", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 8,
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("other-endpoints");
            });
            options.AddPolicy(Login, context => FixedWindow(context, sharedQuota, sharedQuota ? 60 : 12));
            options.AddPolicy(Registration, context => FixedWindow(context, sharedQuota, sharedQuota ? 20 : 5));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                var retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)) : 1;
                context.HttpContext.Response.Headers.RetryAfter = retrySeconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, bool sharedQuota, int limit) =>
        RateLimitPartition.GetFixedWindowLimiter(sharedQuota ? "shared" : ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    private static string ClientKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip?.IsIPv4MappedToIPv6 == true)
            ip = ip.MapToIPv4();
        if (ip?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            ip = new IPAddress(bytes);
        }

        return ip?.ToString() ?? "unknown";
    }
}
