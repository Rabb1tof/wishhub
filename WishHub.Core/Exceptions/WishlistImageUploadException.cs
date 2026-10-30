namespace WishHub.Core.Exceptions;

public enum WishlistImageUploadError { InvalidImage, TooLarge, QuotaExceeded }

public sealed class WishlistImageUploadException(WishlistImageUploadError error, string message) : Exception(message)
{
    public WishlistImageUploadError Error { get; } = error;
}
