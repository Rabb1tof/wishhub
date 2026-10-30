using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

public sealed class AvatarStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wishhub-avatar-storage-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("invalid-owner")]
    [InlineData("unsupported-type")]
    public async Task Save_InvalidOwnerOrType_DoesNotCreateDirectory(string scenario)
    {
        var storage = new AvatarStorage(_root);
        using var content = new MemoryStream(AvatarUploadTests.ImageBytes("png", 80));
        var owner = scenario == "invalid-owner" ? Guid.Empty : Guid.NewGuid();
        Assert.Null(await storage.SaveAsync(owner, content, scenario == "unsupported-type" ? "text/html" : "image/png", CancellationToken.None));
        Assert.Equal(0, content.Position);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task DeleteOwned_RemovesCanonicalLegacyActiveFileButPreservesForeignAndExternalTargets()
    {
        Directory.CreateDirectory(_root);
        var owner = Guid.NewGuid();
        var storage = new AvatarStorage(_root);
        var ownName = $"{owner:D}_{Guid.NewGuid():N}.html";
        var foreignName = $"{Guid.NewGuid():D}_{Guid.NewGuid():N}.png";
        await File.WriteAllTextAsync(Path.Combine(_root, ownName), "legacy");
        await File.WriteAllTextAsync(Path.Combine(_root, foreignName), "foreign");
        storage.DeleteOwned(owner, "/avatars/" + foreignName);
        storage.DeleteOwned(owner, "https://oauth.example.test/avatars/" + ownName);
        Assert.Equal("legacy", await File.ReadAllTextAsync(Path.Combine(_root, ownName)));
        Assert.Equal("foreign", await File.ReadAllTextAsync(Path.Combine(_root, foreignName)));
        storage.DeleteOwned(owner, "/avatars/" + ownName);
        Assert.Equal(foreignName, Path.GetFileName(Assert.Single(Directory.GetFiles(_root))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
