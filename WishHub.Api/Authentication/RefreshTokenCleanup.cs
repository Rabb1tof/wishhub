using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WishHub.Infrastructure.Data;

namespace WishHub.Api.Authentication;

public sealed class RefreshTokenCleanup(IServiceScopeFactory scopes, ILogger<RefreshTokenCleanup> logger) : BackgroundService
{
    public async Task<int> PruneExpiredAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var batch = db.RefreshTokens.Where(token => token.ExpiresAt <= now)
            .OrderBy(token => token.ExpiresAt).ThenBy(token => token.Id).Take(500);
        return await db.RefreshTokens.Where(token => token.ExpiresAt <= now && batch.Select(expired => expired.Id).Contains(token.Id))
            .ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await PruneExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not remove expired session records");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
