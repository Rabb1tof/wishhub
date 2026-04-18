namespace WishHub.Parsing.Ozon;

public sealed class SessionManagerOptions
{
    public string StateDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "playwright-state", "ozon", "sessions");
}
