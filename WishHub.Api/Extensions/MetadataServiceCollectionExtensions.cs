using System.Security.Claims;
using System.Threading.RateLimiting;
using WishHub.Core.Interfaces;
using WishHub.Parsing;

namespace WishHub.Api.Extensions;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddProductMetadata(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("ProductMetadata", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WishHub/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).ConfigurePrimaryHttpMessageHandler(MetadataHttpTransport.CreateHandler);
        services.AddSingleton<IProductMetadataService>(sp => new ProductMetadataService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ProductMetadata"),
            configuration.GetValue<bool>("Metadata:EnableOzonWbray")));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("metadata", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 6,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        return services;
    }
}
