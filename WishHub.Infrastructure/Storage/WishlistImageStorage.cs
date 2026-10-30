using System.Buffers.Binary;
using System.Security;
using WishHub.Core.Exceptions;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;

namespace WishHub.Infrastructure.Storage;

/// <summary>The caller must hold the owner's database lock across upload, cleanup and card saves.</summary>
public sealed class WishlistImageStorage : IWishlistImageStorage
{
    private readonly string _rootPath;

    public WishlistImageStorage(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        EnsureSafePath(_rootPath);
        Directory.CreateDirectory(_rootPath);
        EnsureSafePath(_rootPath);
    }

    public async Task<string> UploadAsync(Guid ownerId, Stream content, string? contentType,
        IReadOnlySet<string> referencedImageUrls, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(referencedImageUrls);
        ct.ThrowIfCancellationRequested();
        var mime = contentType?.Split(';', 2)[0].Trim();
        if (ownerId == Guid.Empty || mime is null ||
            !(mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
              mime.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
              mime.Equals("image/webp", StringComparison.OrdinalIgnoreCase)))
            throw InvalidImage();

        var ownerPath = Path.Combine(_rootPath, ownerId.ToString("D"));
        EnsureSafePath(ownerPath);
        Directory.CreateDirectory(ownerPath);
        EnsureSafePath(ownerPath);
        var ownerBytes = CleanupAndMeasure(ownerId, ownerPath, referencedImageUrls, ct);
        var imageId = Guid.NewGuid();
        var temporaryPath = Path.Combine(ownerPath, $".{imageId:N}.tmp");
        try
        {
            var buffer = new byte[64 * 1024];
            var signature = new byte[33];
            var signatureLength = 0;
            long length = 0;
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var requested = (int)Math.Min(buffer.Length, WishlistImageLimits.MaxFileBytes + 1 - length);
                    var read = await content.ReadAsync(buffer.AsMemory(0, requested), ct);
                    if (read == 0)
                        break;
                    length += read;
                    if (length > WishlistImageLimits.MaxFileBytes)
                        throw new WishlistImageUploadException(WishlistImageUploadError.TooLarge, "Image exceeds the 5 MiB limit.");
                    if (ownerBytes > WishlistImageLimits.MaxOwnerBytes - length)
                        throw new WishlistImageUploadException(WishlistImageUploadError.QuotaExceeded, "Wishlist image storage quota exceeded.");
                    var signatureBytes = Math.Min(read, signature.Length - signatureLength);
                    buffer.AsSpan(0, signatureBytes).CopyTo(signature.AsSpan(signatureLength));
                    signatureLength += signatureBytes;
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            var extension = DetectExtension(signature.AsSpan(0, signatureLength), length);
            if (extension is null)
                throw InvalidImage();
            var image = new ManagedWishlistImage(ownerId, imageId, extension);
            if (!mime.Equals(image.ContentType, StringComparison.OrdinalIgnoreCase))
                throw InvalidImage();
            ct.ThrowIfCancellationRequested();
            EnsureSafePath(temporaryPath);
            File.Move(temporaryPath, Path.Combine(ownerPath, $"{imageId:D}.{extension}"));
            return image.Url;
        }
        finally
        {
            EnsureSafePath(temporaryPath);
            File.Delete(temporaryPath);
        }
    }

    public bool Exists(ManagedWishlistImage image)
    {
        try
        {
            var path = ImagePath(image);
            return path is not null && File.Exists(path);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return false;
        }
    }

    public Stream? OpenRead(ManagedWishlistImage image)
    {
        try
        {
            var path = ImagePath(image);
            return path is null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return null;
        }
    }

    private string? ImagePath(ManagedWishlistImage image)
    {
        if (!ManagedWishlistImage.TryParse(image.Url, out var canonical) || canonical != image)
            return null;
        var path = Path.Combine(_rootPath, image.OwnerId.ToString("D"), $"{image.ImageId:D}.{image.Extension}");
        EnsureSafePath(path);
        return path;
    }

    private static long CleanupAndMeasure(Guid ownerId, string ownerPath,
        IReadOnlySet<string> referencedImageUrls, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        long usedBytes = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(ownerPath))
        {
            ct.ThrowIfCancellationRequested();
            EnsureSafePath(path);
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.Directory) != 0)
                continue;
            var managed = ManagedWishlistImage.TryParse($"{ManagedWishlistImage.Prefix}{ownerId:D}/{file.Name}", out var image);
            var temporary = file.Name.Length == 37 && file.Name[0] == '.' &&
                file.Name.EndsWith(".tmp", StringComparison.Ordinal) &&
                Guid.TryParseExact(file.Name.AsSpan(1, 32), "N", out var temporaryId) && temporaryId != Guid.Empty &&
                file.Name == $".{temporaryId:N}.tmp";
            if (file.LastWriteTimeUtc < cutoff && (temporary || managed && !referencedImageUrls.Contains(image.Url)))
            {
                File.Delete(path);
                continue;
            }
            usedBytes = file.Length > WishlistImageLimits.MaxOwnerBytes - usedBytes
                ? WishlistImageLimits.MaxOwnerBytes + 1
                : usedBytes + file.Length;
        }
        return usedBytes;
    }

    private static string? DetectExtension(ReadOnlySpan<byte> header, long length)
    {
        // Check framing only; pixel decoding and re-encoding are intentionally outside this store.
        if (header.Length >= 12 && header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(4, 2));
            if (segmentLength >= 2 && segmentLength <= length - 4)
                return "jpg";
        }
        if (header.Length >= 33 && header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4)) == 13 && header.Slice(12, 4).SequenceEqual("IHDR"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4)) > 0 && BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4)) > 0)
            return "png";
        if (header.Length >= 20 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8) &&
            (header.Slice(12, 4).SequenceEqual("VP8 "u8) || header.Slice(12, 4).SequenceEqual("VP8L"u8) || header.Slice(12, 4).SequenceEqual("VP8X"u8)))
        {
            var riffLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4));
            if ((long)riffLength + 8 == length && chunkLength > 0 && (long)chunkLength + 20 + (chunkLength & 1) <= length)
                return "webp";
        }
        return null;
    }

    private static void EnsureSafePath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Wishlist image storage cannot use symbolic links or reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool IsPathFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or SecurityException or ArgumentException or NotSupportedException;

    private static WishlistImageUploadException InvalidImage() =>
        new(WishlistImageUploadError.InvalidImage, "Upload a JPEG, PNG or WebP image with a matching content type.");
}
