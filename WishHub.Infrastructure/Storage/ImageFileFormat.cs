using System.Buffers.Binary;
using WishHub.Core.Validation;

namespace WishHub.Infrastructure.Storage;

/// <summary>Checks image framing without decoding pixels. Leaves the readable, seekable stream at position zero.</summary>
public static class ImageFileFormat
{
    public static async Task<string?> DetectExtensionAsync(Stream content, long length, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || !content.CanSeek)
            throw new ArgumentException("Image inspection requires a readable, seekable stream.", nameof(content));
        content.Position = 0;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (length is < 12 or > WishlistImageLimits.MaxFileBytes || content.Length != length)
                return null;
            var header = new byte[33];
            var count = await content.ReadAtLeastAsync(header.AsMemory(0, (int)Math.Min(header.Length, length)),
                (int)Math.Min(header.Length, length), throwOnEndOfStream: false, ct);
            if (count >= 12 && header.AsSpan(0, 3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }))
                return await DetectJpegAsync(content, length, ct);
            if (count >= 33 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
                BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4)) == 13 && header.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
                BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4)) > 0 && BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4)) > 0)
                return "png";
            if (count >= 20 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8) &&
                (header.AsSpan(12, 4).SequenceEqual("VP8 "u8) || header.AsSpan(12, 4).SequenceEqual("VP8L"u8) || header.AsSpan(12, 4).SequenceEqual("VP8X"u8)))
            {
                var riffLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
                if ((long)riffLength + 8 == length && chunkLength > 0 && (long)chunkLength + 20 + (chunkLength & 1) <= length)
                    return "webp";
            }
            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }

    public static bool MatchesContentType(string extension, string? contentType)
    {
        var expected = extension switch
        {
            "jpg" => "image/jpeg",
            "png" => "image/png",
            "webp" => "image/webp",
            _ => null
        };
        return expected is not null && expected.Equals(contentType?.Split(';', 2)[0].Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> DetectJpegAsync(Stream content, long length, CancellationToken ct)
    {
        // T.81 B.1.1.2 permits FF fill before any marker, including the first segment after SOI.
        content.Position = 3;
        var buffer = new byte[4096];
        while (content.Position < length)
        {
            var count = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - content.Position)), ct);
            if (count == 0)
                return null;
            var index = buffer.AsSpan(0, count).IndexOfAnyExcept((byte)0xFF);
            if (index < 0)
                continue;
            if (buffer[index] is 0 or 1 or >= 0xD0 and <= 0xD9)
                return null;
            content.Position = content.Position - count + index + 1;
            var remaining = length - content.Position;
            if (remaining < 2 || await content.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, false, ct) != 2)
                return null;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, 2));
            return segmentLength >= 2 && segmentLength <= remaining ? "jpg" : null;
        }
        return null;
    }
}
