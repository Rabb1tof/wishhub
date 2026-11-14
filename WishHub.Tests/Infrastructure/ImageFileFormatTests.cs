using WishHub.Core.Validation;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

public sealed class ImageFileFormatTests
{
    [Fact]
    public async Task DetectExtension_PngFromNonzeroPosition_RewindsAndKeepsStreamOpen()
    {
        using var content = new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII="));
        content.Position = content.Length;

        Assert.Equal("png", await ImageFileFormat.DetectExtensionAsync(content, content.Length));

        Assert.Equal(0, content.Position);
        Assert.Equal(0x89, content.ReadByte());
    }

    [Theory]
    [InlineData("fill-only")]
    [InlineData("escaped-marker")]
    [InlineData("standalone-marker")]
    [InlineData("truncated-length")]
    [InlineData("oversized-segment")]
    public async Task DetectExtension_InvalidJpegAfterFill_RejectsAndRewinds(string scenario)
    {
        var bytes = Enumerable.Repeat((byte)0xFF, 80).ToArray();
        bytes[1] = 0xD8;
        var markerIndex = scenario == "truncated-length" ? bytes.Length - 2 : 50;
        if (scenario != "fill-only")
            bytes[markerIndex] = scenario switch
            {
                "escaped-marker" => 0,
                "standalone-marker" => 0xD9,
                _ => 0xE0
            };
        if (scenario != "truncated-length" && scenario != "fill-only")
        {
            bytes[markerIndex + 1] = 0;
            bytes[markerIndex + 2] = scenario == "oversized-segment" ? (byte)30 : (byte)2;
        }
        using var content = new MemoryStream(bytes);

        Assert.Null(await ImageFileFormat.DetectExtensionAsync(content, content.Length));

        Assert.Equal(0, content.Position);
        Assert.Equal(0xFF, content.ReadByte());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(79)]
    [InlineData(WishlistImageLimits.MaxFileBytes + 1)]
    public async Task DetectExtension_InvalidDeclaredLength_RejectsBeforeReading(long declaredLength)
    {
        using var content = new CountingStream(new byte[80]);
        content.Position = 2;

        Assert.Null(await ImageFileFormat.DetectExtensionAsync(content, declaredLength));

        Assert.Equal(0, content.BytesRead);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public async Task DetectExtension_CancelDuringJpegFill_StopsReadingAndRewinds()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, 16384).ToArray();
        bytes[1] = 0xD8;
        using var cancellation = new CancellationTokenSource();
        using var content = new CountingStream(bytes, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageFileFormat.DetectExtensionAsync(content, content.Length, cancellation.Token));

        Assert.Equal(33 + 4096, content.BytesRead);
        Assert.Equal(0, content.Position);
        Assert.Equal(0xFF, content.ReadByte());
    }

    [Theory]
    [InlineData("jpg", "image/jpeg", true)]
    [InlineData("png", " IMAGE/PNG; charset=binary ", true)]
    [InlineData("webp", "image/webp", true)]
    [InlineData("jpg", "image/png", false)]
    [InlineData("png", "image/svg+xml", false)]
    [InlineData("png", "text/html", false)]
    [InlineData("png", null, false)]
    [InlineData("png", "", false)]
    [InlineData("svg", "image/svg+xml", false)]
    public void MatchesContentType_UsesDetectedFormat(string extension, string? contentType, bool expected) =>
        Assert.Equal(expected, ImageFileFormat.MatchesContentType(extension, contentType));

    private sealed class CountingStream(byte[] bytes, CancellationTokenSource? cancelAfterSecondRead = null) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        private int _reads;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            if (++_reads == 2)
                cancelAfterSecondRead?.Cancel();
            return read;
        }
    }
}
