using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WishHub.Api.Extensions;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Api;

public sealed class AvatarFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wishhub-avatar-files-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("html")]
    [InlineData("HTML")]
    [InlineData("svg")]
    [InlineData("js")]
    [InlineData("xml")]
    [InlineData("gif")]
    public async Task Get_LegacyActiveFile_ReturnsNotFoundWithoutDeletingIt(string extension)
    {
        var avatars = Path.Combine(_root, "wwwroot", "avatars");
        Directory.CreateDirectory(avatars);
        var path = Path.Combine(avatars, "legacy." + extension);
        await File.WriteAllTextAsync(path, "synthetic legacy content");
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/avatars/legacy." + extension);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("synthetic legacy content", await File.ReadAllTextAsync(path));
        await host.StopAsync();
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("JPG", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Get_ExistingImage_ReturnsExactBytesWithImageTypeAndNosniff(string extension, string contentType)
    {
        using var host = await StartHostAsync();
        var storage = host.Services.GetRequiredService<AvatarStorage>();
        var expected = ImageBytes(contentType);
        await File.WriteAllBytesAsync(Path.Combine(storage.RootPath, "avatar." + extension), expected);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/avatars/avatar." + extension);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        await host.StopAsync();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task Registration_ConfiguredRoot_SharesStorageAndServingPathIndependentOfWorkingDirectory(string mode)
    {
        var configured = mode == "relative" ? "custom/avatars" : mode == "absolute" ? Path.Combine(_root, "absolute-avatars") : null;
        var expectedRoot = configured is null ? Path.Combine(_root, "wwwroot", "avatars")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(_root, configured);
        Assert.NotEqual(Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetFullPath(_root));
        using var host = await StartHostAsync(configured);
        var storage = host.Services.GetRequiredService<AvatarStorage>();
        Assert.Equal(Path.GetFullPath(expectedRoot), storage.RootPath);
        Assert.Same(storage, host.Services.GetRequiredService<AvatarStorage>());
        var expected = ImageBytes("image/png");
        await File.WriteAllBytesAsync(Path.Combine(storage.RootPath, "avatar.png"), expected);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/avatars/avatar.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        await host.StopAsync();
    }

    [Fact]
    public async Task Get_FileOutsideAvatarRoot_DoesNotExposeOtherWebRootFiles()
    {
        using var host = await StartHostAsync();
        var outside = Path.Combine(_root, "wwwroot", "outside.png");
        await File.WriteAllBytesAsync(outside, ImageBytes("image/png"));
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/outside.png");
        using var traversal = await client.GetAsync("/avatars/../outside.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        Assert.True(File.Exists(outside));
        await host.StopAsync();
    }

    private Task<IHost> StartHostAsync(string? configuredPath = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
        return new HostBuilder().ConfigureWebHost(web => web
            .UseContentRoot(_root).UseWebRoot(Path.Combine(_root, "wwwroot")).UseTestServer()
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["AvatarUploadPath"] = configuredPath }))
            .ConfigureServices((context, services) => services.AddAvatarFiles(context.Configuration, context.HostingEnvironment))
            .Configure(app => app.UseAvatarFiles())).StartAsync();
    }

    private static byte[] ImageBytes(string contentType) => Convert.FromBase64String(contentType switch
    {
        "image/jpeg" => "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=",
        "image/png" => "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII=",
        "image/webp" => "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType))
    });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
