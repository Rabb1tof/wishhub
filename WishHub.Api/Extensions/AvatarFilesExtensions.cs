using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using WishHub.Infrastructure.Storage;

namespace WishHub.Api.Extensions;

public static class AvatarFilesExtensions
{
    public static IServiceCollection AddAvatarFiles(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["AvatarUploadPath"] ?? Path.Combine("wwwroot", "avatars");
        var rootPath = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        services.AddSingleton(_ => new AvatarStorage(rootPath));
        return services;
    }

    public static IApplicationBuilder UseAvatarFiles(this IApplicationBuilder app)
    {
        var storage = app.ApplicationServices.GetRequiredService<AvatarStorage>();
        Directory.CreateDirectory(storage.RootPath);
        var files = new PhysicalFileProvider(storage.RootPath);
        app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(files.Dispose);
        return app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/avatars",
            FileProvider = files,
            ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [".webp"] = "image/webp"
            }),
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context => context.Context.Response.Headers.XContentTypeOptions = "nosniff"
        });
    }
}
