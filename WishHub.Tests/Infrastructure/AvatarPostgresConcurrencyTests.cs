using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class AvatarPostgresConcurrencyTests
{
    [PostgresFact]
    public async Task Upload_ConcurrentReplacements_ReloadsLockedOwnerAndRetainsOnlyFinalFile()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "avatar_race_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "wishhub-avatar-race-" + Guid.NewGuid().ToString("N"));
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        ServiceProvider Provider(OwnerLockGate? gate = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(scopedConnection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema));
                if (gate is not null) options.AddInterceptors(gate);
            });
            services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
            services.AddSingleton(new AvatarStorage(root));
            services.AddScoped<UserService>();
            return services.BuildServiceProvider();
        }

        try
        {
            await using var setupProvider = Provider();
            await using var setupScope = setupProvider.CreateAsyncScope();
            var setup = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await setup.Database.MigrateAsync();
            var owner = new User
            {
                Id = Guid.NewGuid(), UserName = "avatar-owner", Email = "avatar-owner@example.test",
                DisplayName = "Owner", SecurityStamp = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow
            };
            var oldName = $"{owner.Id:D}_{Guid.NewGuid():N}.png";
            owner.AvatarUrl = "/avatars/" + oldName;
            setup.Users.Add(owner);
            await setup.SaveChangesAsync();
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(Path.Combine(root, oldName), AvatarUploadTests.ImageBytes("png", 80));

            var firstGate = new OwnerLockGate(hold: true);
            var secondGate = new OwnerLockGate(hold: false);
            await using var firstProvider = Provider(firstGate);
            await using var secondProvider = Provider(secondGate);
            await using var firstScope = firstProvider.CreateAsyncScope();
            await using var secondScope = secondProvider.CreateAsyncScope();
            // A scoped UserManager may already track the owner before this method gets its lock.
            await secondScope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(user => user.Id == owner.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var firstContent = new MemoryStream(AvatarUploadTests.ImageBytes("png", 80));
            using var secondContent = new MemoryStream(AvatarUploadTests.ImageBytes("png", 90));
            var first = firstScope.ServiceProvider.GetRequiredService<UserService>()
                .UploadAvatarAsync(owner.Id, firstContent, "image/png", "first.png", timeout.Token);
            Task<string?>? second = null;
            try
            {
                await firstGate.Acquired.Task.WaitAsync(timeout.Token);
                second = secondScope.ServiceProvider.GetRequiredService<UserService>()
                    .UploadAvatarAsync(owner.Id, secondContent, "image/png", "second.png", timeout.Token);
                await secondGate.Attempted.Task.WaitAsync(timeout.Token);
                Assert.False(secondGate.Acquired.Task.IsCompleted);
                firstGate.Release.TrySetResult();
                var urls = await Task.WhenAll(first, second);
                Assert.NotNull(urls[0]);
                Assert.NotNull(urls[1]);
                Assert.NotEqual(urls[0], urls[1]);
                Assert.Equal(urls[1], await setup.Users.AsNoTracking().Where(user => user.Id == owner.Id).Select(user => user.AvatarUrl).SingleAsync());
                var finalFile = Assert.Single(Directory.GetFiles(root));
                Assert.Equal(Path.GetFileName(urls[1]), Path.GetFileName(finalFile));
                Assert.Equal(AvatarUploadTests.ImageBytes("png", 90), await File.ReadAllBytesAsync(finalFile));
            }
            finally
            {
                firstGate.Release.TrySetResult();
                await first;
                if (second is not null) await second;
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database to run this test.";
        }
    }

    private sealed class OwnerLockGate(bool hold) : DbCommandInterceptor
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Acquired.TrySetResult();
                if (hold) await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }
}
