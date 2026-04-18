namespace WishHub.Parsing.Ozon;

public sealed class OzonSessionMetadata
{
    public required string SessionId { get; init; }

    public required string StatePath { get; init; }

    public DateTime? WarmedUpAtUtc { get; set; }

    public DateTime? LastStateSavedAtUtc { get; set; }

    public DateTime? LastStateRestoredAtUtc { get; set; }

    public bool IsBlocked { get; set; }
}
