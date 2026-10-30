using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Api.Extensions;

public static class WishlistImageServiceCollectionExtensions
{
    public static IServiceCollection AddWishlistImages(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["WishlistImages:StoragePath"] ?? Path.Combine("App_Data", "wishlist-images");
        var storagePath = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var relativeToWebRoot = Path.GetRelativePath(webRoot, storagePath);
        if (!Path.IsPathRooted(relativeToWebRoot) && relativeToWebRoot != ".." &&
            !relativeToWebRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("WishlistImages:StoragePath must be outside wwwroot.");

        services.AddSingleton<IWishlistImageStorage>(new WishlistImageStorage(storagePath));
        services.AddScoped<IWishlistImageService, WishlistImageService>();
        return services;
    }
}
