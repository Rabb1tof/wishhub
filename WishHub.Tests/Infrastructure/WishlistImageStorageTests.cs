using System.Buffers.Binary;
using System.Text;
using WishHub.Core.Exceptions;
using WishHub.Core.Validation;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

public sealed class WishlistImageStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wishhub-image-storage-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _owner = Guid.NewGuid();
    private readonly WishlistImageStorage _storage;
    private static readonly IReadOnlySet<string> NoReferences = new HashSet<string>(StringComparer.Ordinal);
    private string Root => Path.Combine(_directory, "store");
    private string OwnerPath => Path.Combine(Root, _owner.ToString("D"));

    public WishlistImageStorageTests() => _storage = new WishlistImageStorage(Root);

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Upload_SupportedSignatures_PersistsExactBytesAndCanonicalOwnerUrl(string extension, string mime)
    {
        var bytes = ImageBytes(extension, 80);
        using var input = new MemoryStream(bytes);
        var url = await _storage.UploadAsync(_owner, input, mime, NoReferences);
        Assert.True(ManagedWishlistImage.TryParse(url, out var image));
        Assert.Equal(_owner, image.OwnerId);
        Assert.NotEqual(Guid.Empty, image.ImageId);
        Assert.Equal(extension, image.Extension);
        Assert.True(_storage.Exists(image));
        await using var read = _storage.OpenRead(image);
        Assert.NotNull(read);
        using var actual = new MemoryStream();
        await read.CopyToAsync(actual);
        Assert.Equal(bytes, actual.ToArray());
        Assert.Single(Directory.GetFiles(OwnerPath));
        Assert.True(input.CanRead);
        Assert.False(_storage.Exists(image with { OwnerId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Upload_FragmentedHeader_RecognizesSignatureAcrossReads()
    {
        using var input = new ObservedStream(ImageBytes("webp", 80), chunkSize: 1);
        var url = await _storage.UploadAsync(_owner, input, "IMAGE/WEBP; charset=binary", NoReferences);
        Assert.True(ManagedWishlistImage.TryParse(url, out var image));
        Assert.Equal("webp", image.Extension);
        Assert.Equal(80, input.BytesRead);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("truncated-png")]
    [InlineData("random")]
    [InlineData("svg")]
    [InlineData("wrong-riff")]
    [InlineData("mismatched-mime")]
    [InlineData("missing-mime")]
    [InlineData("unsupported-mime")]
    [InlineData("png-signature-only")]
    [InlineData("invalid-png-chunk")]
    [InlineData("empty-png-width")]
    [InlineData("empty-png-height")]
    [InlineData("jpeg-header-only")]
    [InlineData("invalid-jpeg-segment")]
    [InlineData("webp-header-only")]
    [InlineData("invalid-webp-chunk")]
    [InlineData("invalid-webp-size")]
    public async Task Upload_InvalidSignatureOrMime_RejectsWithoutLeavingFiles(string scenario)
    {
        var bytes = scenario switch
        {
            "empty" => [],
            "truncated-png" => new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "random" => Encoding.UTF8.GetBytes("not an image"),
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'></svg>"),
            "wrong-riff" => Encoding.UTF8.GetBytes("RIFF0000WAVEdata"),
            "png-signature-only" => ImageBytes("png", 80)[..8],
            "invalid-png-chunk" => ChangedByte(ImageBytes("png", 80), 12, (byte)'X'),
            "empty-png-width" => ChangedByte(ImageBytes("png", 80), 19, 0),
            "empty-png-height" => ChangedByte(ImageBytes("png", 80), 23, 0),
            "jpeg-header-only" => ImageBytes("jpg", 80)[..11],
            "invalid-jpeg-segment" => ChangedByte(ImageBytes("jpg", 80), 4, 0x7F),
            "webp-header-only" => ImageBytes("webp", 80)[..12],
            "invalid-webp-chunk" => ChangedByte(ImageBytes("webp", 80), 12, (byte)'X'),
            "invalid-webp-size" => ChangedByte(ImageBytes("webp", 80), 4, 0),
            _ => ImageBytes("png", 80)
        };
        var mime = scenario switch
        {
            "mismatched-mime" => "image/jpeg",
            "missing-mime" => null,
            "unsupported-mime" => "image/svg+xml",
            "jpeg-header-only" or "invalid-jpeg-segment" => "image/jpeg",
            "webp-header-only" or "invalid-webp-chunk" or "invalid-webp-size" => "image/webp",
            _ => "image/png"
        };
        using var input = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<WishlistImageUploadException>(() => _storage.UploadAsync(_owner, input, mime, NoReferences));
        Assert.Equal(WishlistImageUploadError.InvalidImage, error.Error);
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_ExactFiveMiB_AcceptsCompleteFile()
    {
        using var input = new ObservedStream(ImageBytes("jpg", (int)WishlistImageLimits.MaxFileBytes));
        var url = await _storage.UploadAsync(_owner, input, "image/jpeg", NoReferences);
        Assert.True(ManagedWishlistImage.TryParse(url, out var image));
        await using var read = _storage.OpenRead(image);
        Assert.NotNull(read);
        Assert.Equal(WishlistImageLimits.MaxFileBytes, read.Length);
        Assert.Equal(WishlistImageLimits.MaxFileBytes, input.BytesRead);
        Assert.True(input.LargestReadRequest <= 64 * 1024);
    }

    [Fact]
    public async Task Upload_OverFiveMiB_ReadsOnlyLimitPlusOneAndRemovesTemporaryFile()
    {
        using var input = new ObservedStream(ImageBytes("jpg", (int)WishlistImageLimits.MaxFileBytes + 4096));
        var error = await Assert.ThrowsAsync<WishlistImageUploadException>(() => _storage.UploadAsync(_owner, input, "image/jpeg", NoReferences));
        Assert.Equal(WishlistImageUploadError.TooLarge, error.Error);
        Assert.Equal(WishlistImageLimits.MaxFileBytes + 1, input.BytesRead);
        Assert.Empty(Directory.GetFiles(OwnerPath));
    }

    [Fact]
    public async Task Upload_ReadFailure_RemovesTemporaryFileAndKeepsCallerStreamOpen()
    {
        using var input = new ObservedStream(ImageBytes("png", 128 * 1024), failAfterFirstRead: true);
        await Assert.ThrowsAsync<IOException>(() => _storage.UploadAsync(_owner, input, "image/png", NoReferences));
        Assert.Equal(64 * 1024, input.BytesRead);
        Assert.Empty(Directory.GetFiles(OwnerPath));
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_Cancellation_RemovesTemporaryFile(bool beforeRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new ObservedStream(ImageBytes("png", 128 * 1024), cancelAfterRead: beforeRead ? null : cancellation);
        if (beforeRead)
            cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _storage.UploadAsync(_owner, input, "image/png", NoReferences, cancellation.Token));
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
        Assert.Equal(beforeRead ? 0 : 64 * 1024, input.BytesRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Upload_OwnerQuota_AllowsExactLimitAndRejectsOneByteOver(int extraByte)
    {
        Directory.CreateDirectory(OwnerPath);
        var existing = new ManagedWishlistImage(_owner, Guid.NewGuid(), "png");
        var existingPath = Path.Combine(OwnerPath, $"{existing.ImageId:D}.png");
        var existingLength = WishlistImageLimits.MaxOwnerBytes - WishlistImageLimits.MaxFileBytes + extraByte;
        using (var file = File.Create(existingPath))
            file.SetLength(existingLength);
        using var input = new MemoryStream(ImageBytes("jpg", (int)WishlistImageLimits.MaxFileBytes));
        if (extraByte == 0)
        {
            var url = await _storage.UploadAsync(_owner, input, "image/jpeg", NoReferences);
            Assert.True(ManagedWishlistImage.TryParse(url, out var image));
            Assert.True(_storage.Exists(image));
            Assert.Equal(WishlistImageLimits.MaxOwnerBytes, Directory.GetFiles(OwnerPath).Sum(path => new FileInfo(path).Length));
        }
        else
        {
            var error = await Assert.ThrowsAsync<WishlistImageUploadException>(() => _storage.UploadAsync(_owner, input, "image/jpeg", NoReferences));
            Assert.Equal(WishlistImageUploadError.QuotaExceeded, error.Error);
            Assert.Equal(existingPath, Assert.Single(Directory.GetFiles(OwnerPath)));
        }
        Assert.Equal(existingLength, new FileInfo(existingPath).Length);
    }

    [Fact]
    public async Task Upload_Cleanup_DeletesOnlyOldUnreferencedCanonicalFilesAndOldOwnTemps()
    {
        Directory.CreateDirectory(OwnerPath);
        var old = CreateManagedFile(hoursOld: 25);
        var referenced = CreateManagedFile(hoursOld: 25);
        var recent = CreateManagedFile(hoursOld: 23);
        var staleTemporary = CreateFile($".{Guid.NewGuid():N}.tmp", hoursOld: 25);
        var recentTemporary = CreateFile($".{Guid.NewGuid():N}.tmp", hoursOld: 23);
        var unrelated = CreateFile("notes.txt", hoursOld: 25);
        var unrelatedTemporary = CreateFile(".not-a-guid.tmp", hoursOld: 25);
        var nonCanonical = CreateFile("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA.png", hoursOld: 25);
        var nonCanonicalTemporary = CreateFile(".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.tmp", hoursOld: 25);
        var emptyIdTemporary = CreateFile(".00000000000000000000000000000000.tmp", hoursOld: 25);
        var otherOwner = Guid.NewGuid();
        var otherOwnerPath = Path.Combine(Root, otherOwner.ToString("D"));
        Directory.CreateDirectory(otherOwnerPath);
        var otherFile = Path.Combine(otherOwnerPath, $"{Guid.NewGuid():D}.png");
        File.WriteAllBytes(otherFile, ImageBytes("png", 80));
        File.SetLastWriteTimeUtc(otherFile, DateTime.UtcNow.AddHours(-25));
        using var input = new MemoryStream(ImageBytes("png", 80));
        await _storage.UploadAsync(_owner, input, "image/png", new HashSet<string> { referenced.Url });

        Assert.False(_storage.Exists(old));
        Assert.False(File.Exists(staleTemporary));
        Assert.True(_storage.Exists(referenced));
        Assert.True(_storage.Exists(recent));
        foreach (var path in new[] { recentTemporary, unrelated, unrelatedTemporary, nonCanonical, nonCanonicalTemporary, emptyIdTemporary, otherFile })
            Assert.True(File.Exists(path), $"Unrelated or recent file was removed: {Path.GetFileName(path)}");
    }

    [Fact]
    public async Task Upload_CleanupWhileOldDraftIsOpen_UnlinksFileAndKeepsActiveReader()
    {
        Directory.CreateDirectory(OwnerPath);
        var draft = CreateManagedFile(hoursOld: 25);
        var draftPath = Path.Combine(OwnerPath, $"{draft.ImageId:D}.png");
        var expected = ImageBytes("png", 80);
        await using var activeReader = _storage.OpenRead(draft);
        Assert.NotNull(activeReader);
        using var input = new MemoryStream(ImageBytes("png", 80));

        var url = await _storage.UploadAsync(_owner, input, "image/png", NoReferences);

        Assert.False(File.Exists(draftPath));
        Assert.False(_storage.Exists(draft));
        Assert.Null(_storage.OpenRead(draft));
        Assert.True(ManagedWishlistImage.TryParse(url, out var added));
        Assert.True(_storage.Exists(added));
        using var downloaded = new MemoryStream();
        await activeReader.CopyToAsync(downloaded);
        Assert.Equal(expected, downloaded.ToArray());
    }

    [Fact]
    public void Read_MissingOrInvalidManagedPaths_ReturnsNoFile()
    {
        foreach (var image in new[]
        {
            new ManagedWishlistImage(_owner, Guid.NewGuid(), "png"),
            new ManagedWishlistImage(_owner, Guid.NewGuid(), "../outside.png"),
            new ManagedWishlistImage(_owner, Guid.NewGuid(), "PNG"),
            new ManagedWishlistImage(Guid.Empty, Guid.NewGuid(), "jpg"),
            new ManagedWishlistImage(_owner, Guid.Empty, "jpg")
        })
        {
            Assert.False(_storage.Exists(image));
            Assert.Null(_storage.OpenRead(image));
        }
    }

    [Fact]
    public async Task Upload_AndRead_SymlinkOwnerDirectoryCannotReachOutsideRoot()
    {
        var foreign = Path.Combine(_directory, "foreign");
        Directory.CreateDirectory(foreign);
        var image = new ManagedWishlistImage(_owner, Guid.NewGuid(), "png");
        var target = Path.Combine(foreign, $"{image.ImageId:D}.png");
        File.WriteAllBytes(target, ImageBytes("png", 80));
        Directory.CreateSymbolicLink(OwnerPath, foreign);
        try
        {
            Assert.False(_storage.Exists(image));
            Assert.Null(_storage.OpenRead(image));
            using var input = new MemoryStream(ImageBytes("png", 80));
            await Assert.ThrowsAsync<IOException>(() => _storage.UploadAsync(_owner, input, "image/png", NoReferences));
            Assert.Equal(target, Assert.Single(Directory.GetFiles(foreign)));
        }
        finally { Directory.Delete(OwnerPath); }
    }

    [Fact]
    public async Task Upload_AndRead_SymlinkFileCannotExposeOrDeleteForeignData()
    {
        Directory.CreateDirectory(OwnerPath);
        var foreign = Path.Combine(_directory, "foreign.png");
        var bytes = ImageBytes("png", 80);
        File.WriteAllBytes(foreign, bytes);
        var image = new ManagedWishlistImage(_owner, Guid.NewGuid(), "png");
        var link = Path.Combine(OwnerPath, $"{image.ImageId:D}.png");
        File.CreateSymbolicLink(link, foreign);
        try
        {
            Assert.False(_storage.Exists(image));
            Assert.Null(_storage.OpenRead(image));
            using var input = new MemoryStream(ImageBytes("png", 80));
            await Assert.ThrowsAsync<IOException>(() => _storage.UploadAsync(_owner, input, "image/png", NoReferences));
            Assert.Equal(bytes, File.ReadAllBytes(foreign));
        }
        finally { File.Delete(link); }
    }

    private ManagedWishlistImage CreateManagedFile(int hoursOld)
    {
        var image = new ManagedWishlistImage(_owner, Guid.NewGuid(), "png");
        CreateFile($"{image.ImageId:D}.png", hoursOld);
        return image;
    }

    private string CreateFile(string name, int hoursOld)
    {
        var path = Path.Combine(OwnerPath, name);
        File.WriteAllBytes(path, ImageBytes("png", 80));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-hoursOld));
        return path;
    }

    private static byte[] ChangedByte(byte[] bytes, int offset, byte value)
    {
        bytes[offset] = value;
        return bytes;
    }

    private static byte[] ImageBytes(string extension, int length)
    {
        var original = Convert.FromBase64String(extension switch
        {
            "jpg" => "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=",
            "png" => "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII=",
            "webp" => "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA",
            _ => throw new ArgumentOutOfRangeException(nameof(extension))
        });
        var bytes = new byte[Math.Max(length, original.Length)];
        original.CopyTo(bytes, 0);
        if (extension == "webp")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length - 8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), (uint)bytes.Length - 20);
        }
        return bytes;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class ObservedStream(byte[] bytes, int? chunkSize = null, bool failAfterFirstRead = false,
        CancellationTokenSource? cancelAfterRead = null) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public int LargestReadRequest { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            if (failAfterFirstRead && BytesRead > 0)
                throw new IOException("Injected stream read failure.");
            var read = await base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize ?? buffer.Length)], cancellationToken);
            BytesRead += read;
            cancelAfterRead?.Cancel();
            return read;
        }
    }
}
