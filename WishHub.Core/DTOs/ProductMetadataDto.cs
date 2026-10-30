namespace WishHub.Core.DTOs;

/// <summary>Optional suggestions for an unsaved wishlist form; never contains price.</summary>
public sealed record ProductMetadataDto(
    string Status, string? Name, string? ImageUrl, string Source, string? Reason);
