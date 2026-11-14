using System.Buffers.Binary;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WishHub.Api.Controllers;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

public sealed class AvatarUploadTests : IAsyncLifetime
{
    private readonly WishlistTestStore _store = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wishhub-avatar-tests-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();
    private User _owner = null!;

    public async Task InitializeAsync()
    {
        _owner = await _store.AddUserAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_store.Connection));
        services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();
    }

    private UserService Service(UserManager<User>? manager = null) => new(
        manager ?? _scope.ServiceProvider.GetRequiredService<UserManager<User>>(), Db,
        NullLogger<UserService>.Instance, new AvatarStorage(_root));

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Upload_ValidFormats_IgnoresFilenameAndPersistsExactBytes(string extension, string mime)
    {
        var bytes = ImageBytes(extension, 80);
        using var content = new MemoryStream(bytes);
        var url = await Service().UploadAvatarAsync(_owner.Id, content, mime, "picture.html");
        Assert.NotNull(url);
        Assert.StartsWith($"/avatars/{_owner.Id:D}_", url);
        Assert.EndsWith("." + extension, url);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, Path.GetFileName(url))));
        Assert.Equal(url, await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("html")]
    [InlineData("mismatched-mime")]
    [InlineData("unsupported-mime")]
    [InlineData("empty")]
    public async Task Upload_InvalidContentOrMime_LeavesProfileAndStorageUnchanged(string scenario)
    {
        var bytes = scenario switch { "html" => "<html>not an image</html>"u8.ToArray(), "empty" => [], _ => ImageBytes("png", 80) };
        var mime = scenario switch { "mismatched-mime" => "image/jpeg", "unsupported-mime" => "image/svg+xml", _ => "image/png" };
        using var content = new MemoryStream(bytes);
        Assert.Null(await Service().UploadAvatarAsync(_owner.Id, content, mime, "photo.png"));
        Assert.Null(await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Upload_IdentityFailure_ReturnsBadRequestAndRemovesNewFileWithoutDeletingOld()
    {
        var oldUrl = await SeedAvatarAsync(_owner.Id);
        var owner = await Db.Users.SingleAsync(user => user.Id == _owner.Id);
        var manager = new Mock<UserManager<User>>(Mock.Of<IUserStore<User>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(value => value.FindByIdAsync(_owner.Id.ToString())).ReturnsAsync(owner);
        manager.Setup(value => value.UpdateAsync(owner)).ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));
        var controller = new UsersController(Service(manager.Object), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _owner.Id.ToString())], "test"))
                }
            }
        };
        using var content = new MemoryStream(ImageBytes("png", 80));
        var file = new FormFile(content, 0, content.Length, "file", "new.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var response = await controller.UploadAvatar(file);
        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(oldUrl, await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
        Assert.Equal(Path.GetFileName(oldUrl), Path.GetFileName(Assert.Single(Files())));
        manager.Verify(value => value.UpdateAsync(owner), Times.Once);
    }

    [Fact]
    public async Task Upload_Replacement_DeletesOnlyPreviousOwnedFileAfterSuccessfulSave()
    {
        var oldUrl = await SeedAvatarAsync(_owner.Id);
        var foreignPath = Path.Combine(_root, $"{Guid.NewGuid():D}_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(foreignPath, ImageBytes("png", 80));
        using var content = new MemoryStream(ImageBytes("png", 90));
        var url = await Service().UploadAvatarAsync(_owner.Id, content, "image/png", "photo.png");
        Assert.NotNull(url);
        Assert.False(File.Exists(Path.Combine(_root, Path.GetFileName(oldUrl))));
        Assert.True(File.Exists(foreignPath));
        Assert.Equal(url, await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
        Assert.Equal(2, Files().Length);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("foreign")]
    [InlineData("traversal")]
    public async Task Upload_UnownedPreviousReference_DoesNotDeleteItsTarget(string scenario)
    {
        Directory.CreateDirectory(_root);
        var foreignName = $"{Guid.NewGuid():D}_{Guid.NewGuid():N}.png";
        var foreignPath = Path.Combine(_root, foreignName);
        var bytes = ImageBytes("png", 80);
        await File.WriteAllBytesAsync(foreignPath, bytes);
        var owner = await Db.Users.SingleAsync(user => user.Id == _owner.Id);
        owner.AvatarUrl = scenario switch
        {
            "external" => "https://oauth.example.test/avatars/" + foreignName,
            "foreign" => "/avatars/" + foreignName,
            _ => $"/avatars/{_owner.Id:D}_{Guid.NewGuid():N}.png/../{foreignName}"
        };
        await Db.SaveChangesAsync();
        using var content = new MemoryStream(bytes);
        Assert.NotNull(await Service().UploadAvatarAsync(_owner.Id, content, "image/png", "photo.png"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(foreignPath));
        Assert.Equal(2, Files().Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Upload_NonSeekableStream_EnforcesActualFiveMiBBoundary(int excess)
    {
        var bytes = ImageBytes("png", 5 * 1024 * 1024 + excess);
        using var content = new ObservedStream(bytes);
        var url = await Service().UploadAvatarAsync(_owner.Id, content, "image/png", "photo.png");
        Assert.Equal(excess == 0, url is not null);
        Assert.Equal(bytes.Length, content.BytesRead);
        Assert.Equal(excess == 0 ? 1 : 0, Files().Length);
    }

    [Fact]
    public async Task Upload_ReadFailure_DeletesStagedFileAndKeepsOldAvatar()
    {
        var oldUrl = await SeedAvatarAsync(_owner.Id);
        using var content = new ObservedStream(ImageBytes("png", 80), failAfterFirstRead: true);
        await Assert.ThrowsAsync<IOException>(() => Service().UploadAvatarAsync(_owner.Id, content, "image/png", "photo.png"));
        Assert.Equal(Path.GetFileName(oldUrl), Path.GetFileName(Assert.Single(Files())));
        Assert.Equal(oldUrl, await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
    }

    [Fact]
    public async Task Upload_CancellationAfterFirstRead_DeletesStagedFileAndKeepsOldAvatar()
    {
        var oldUrl = await SeedAvatarAsync(_owner.Id);
        using var cancellation = new CancellationTokenSource();
        using var content = new ObservedStream(ImageBytes("png", 80), cancelAfterRead: cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().UploadAvatarAsync(_owner.Id, content, "image/png", "photo.png", cancellation.Token));
        Assert.Equal(Path.GetFileName(oldUrl), Path.GetFileName(Assert.Single(Files())));
        Assert.Equal(oldUrl, await Db.Users.AsNoTracking().Where(user => user.Id == _owner.Id).Select(user => user.AvatarUrl).SingleAsync());
    }

    private async Task<string> SeedAvatarAsync(Guid ownerId)
    {
        Directory.CreateDirectory(_root);
        var name = $"{ownerId:D}_{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(_root, name), ImageBytes("png", 80));
        var owner = await Db.Users.SingleAsync(user => user.Id == ownerId);
        owner.AvatarUrl = "/avatars/" + name;
        await Db.SaveChangesAsync();
        return owner.AvatarUrl;
    }

    private string[] Files() => Directory.Exists(_root) ? Directory.GetFiles(_root) : [];

    internal static byte[] ImageBytes(string extension, int length)
    {
        var bytes = new byte[length];
        if (extension == "jpg")
        {
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 }.CopyTo(bytes, 0);
        }
        else if (extension == "png")
        {
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1 }.CopyTo(bytes, 0);
        }
        else
        {
            "RIFF"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)length - 8);
            "WEBPVP8 "u8.CopyTo(bytes.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)length - 20);
        }
        return bytes;
    }

    private sealed class ObservedStream(byte[] data, bool failAfterFirstRead = false, CancellationTokenSource? cancelAfterRead = null) : MemoryStream(data)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (BytesRead > 0 && failAfterFirstRead)
                throw new IOException("Synthetic read failure.");
            var read = await base.ReadAsync(buffer[..Math.Min(buffer.Length, 40 * 1024)], ct);
            BytesRead += read;
            cancelAfterRead?.Cancel();
            return read;
        }
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _store.DisposeAsync();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
