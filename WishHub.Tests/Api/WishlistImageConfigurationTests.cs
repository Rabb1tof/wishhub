using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using WishHub.Api.Extensions;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;

namespace WishHub.Tests.Api;

public sealed class WishlistImageConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wishhub-image-config-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("default")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task Registration_StoragePath_SavesOutsideWebRootAtConfiguredLocation(string mode)
    {
        var configured = mode == "absolute" ? Path.Combine(_root, "absolute-images") : mode == "relative" ? "private-images" : null;
        var expected = configured is null ? Path.Combine(_root, "App_Data", "wishlist-images")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(_root, configured);
        var services = new ServiceCollection();
        services.AddWishlistImages(Configuration(configured), Environment());
        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IWishlistImageStorage>();
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII=");
        using var content = new MemoryStream(bytes);
        var url = await storage.UploadAsync(Guid.NewGuid(), content, "image/png", new HashSet<string>());
        Assert.True(ManagedWishlistImage.TryParse(url, out var image));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(expected, image.OwnerId.ToString("D"), $"{image.ImageId:D}.png")));
        Assert.False(Directory.Exists(Path.Combine(_root, "wwwroot")));
    }

    [Theory]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/images")]
    [InlineData("private/../wwwroot/images")]
    public void Registration_PublicStoragePath_RejectsBeforeCreatingDirectory(string path)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddWishlistImages(Configuration(path), Environment()));
        Assert.False(Directory.Exists(_root));
    }

    private IConfiguration Configuration(string? path) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WishlistImages:StoragePath"] = path }).Build();

    private IWebHostEnvironment Environment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(_root);
        environment.WebRootPath.Returns(Path.Combine(_root, "wwwroot"));
        return environment;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
