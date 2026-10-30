using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WishHub.Api.Controllers;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class WishlistPostgresSecurityTests
{
    [PostgresFact]
    public async Task CancelReservation_RevokedAccess_ReturnsNoCardAndPreservesPrivateValues()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "wishlist_privacy_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scoped, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        var images = new WishlistImageStorage(Path.Combine(Path.GetTempPath(), "wishhub-unused-images-" + schema));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var ct = timeout.Token;
            await using var setup = Context();
            await setup.Database.MigrateAsync(ct);
            var owner = NewUser("owner");
            var reserver = NewUser("reserver");
            var stranger = NewUser("stranger");
            setup.Users.AddRange(owner, reserver, stranger);
            await setup.SaveChangesAsync(ct);
            var service = new WishlistService(setup, images);
            var card = await service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/visible", Name = "Visible" }, ct);
            Assert.NotNull(await service.ReserveItemAsync(card.Id, reserver.Id, ct));
            owner.WishlistPrivacy = WishlistPrivacy.Private;
            await setup.SaveChangesAsync(ct);
            await service.UpdateItemAsync(owner.Id, card.Id,
                new() { Url = "https://shop.example/private", Name = "Private replacement", Price = 987m }, ct);

            await using var request = Context();
            var current = new WishlistService(request, images);
            Assert.False(await current.CanViewWishlistAsync(owner.Id, reserver.Id, ct));
            Assert.Empty(await current.GetItemsAsync(owner.Id, reserver.Id, ct));
            var controller = new WishlistController(current)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, reserver.Id.ToString())], "Test"))
                    }
                }
            };
            Assert.IsType<NoContentResult>(await controller.CancelReservation(card.Id, ct));
            await using var verification = Context();
            var saved = await verification.WishlistItems.AsNoTracking().SingleAsync(ct);
            Assert.False(saved.IsReserved);
            Assert.Null(saved.ReservedById);
            Assert.Equal("Private replacement", saved.Name);
            Assert.Equal("https://shop.example/private", saved.Url);
            Assert.Equal(987m, saved.Price);
            Assert.Empty(await verification.Notifications.ToListAsync(ct));

            // A failed or repeated cancellation must not affect a later reservation.
            await verification.Users.Where(u => u.Id == owner.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.WishlistPrivacy, WishlistPrivacy.Public), ct);
            var next = new WishlistService(verification, images);
            Assert.NotNull(await next.ReserveItemAsync(card.Id, stranger.Id, ct));
            Assert.False(await current.CancelReservationAsync(card.Id, reserver.Id, ct));
            Assert.False(await current.CancelReservationAsync(card.Id, owner.Id, ct));
            Assert.False(await current.CancelReservationAsync(Guid.NewGuid(), reserver.Id, ct));
            Assert.Equal(stranger.Id, (await verification.WishlistItems.AsNoTracking().SingleAsync(ct)).ReservedById);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(), UserName = name, Email = name + "@example.test",
        DisplayName = name, CreatedAt = DateTime.UtcNow
    };

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database to run this test.";
        }
    }
}