using WishHub.Core.Validation;

namespace WishHub.Infrastructure.Storage;

public sealed class AvatarStorage(string rootPath)
{
    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public async Task<string?> SaveAsync(Guid ownerId, Stream content, string? contentType, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || !new[] { "jpg", "png", "webp" }.Any(extension =>
                ImageFileFormat.MatchesContentType(extension, contentType)))
            return null;
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(RootPath);
        var id = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(RootPath, $".{id}.tmp");
        try
        {
            long length = 0;
            var buffer = new byte[64 * 1024];
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
                        return null;
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            string? extension;
            await using (var staged = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
                extension = await ImageFileFormat.DetectExtensionAsync(staged, length, ct);
            if (extension is null || !ImageFileFormat.MatchesContentType(extension, contentType))
                return null;
            ct.ThrowIfCancellationRequested();
            var name = $"{ownerId:D}_{id}.{extension}";
            File.Move(temporaryPath, Path.Combine(RootPath, name));
            return "/avatars/" + name;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public void DeleteOwned(Guid ownerId, string? url)
    {
        var prefix = $"/avatars/{ownerId:D}_";
        if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal))
            return;
        var suffix = url[prefix.Length..];
        if (suffix.Length is < 34 or > 49 || suffix[32] != '.' ||
            !Guid.TryParseExact(suffix[..32], "N", out var id) || id == Guid.Empty ||
            suffix != id.ToString("N") + suffix[32..] ||
            !suffix[33..].All(char.IsAsciiLetterOrDigit))
            return;
        File.Delete(Path.Combine(RootPath, url["/avatars/".Length..]));
    }
}
