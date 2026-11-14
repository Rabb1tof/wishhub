using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WishHub.Api.Controllers;
using WishHub.Core.DTOs.Messages;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Api;

[Collection("Postgres qualification")]
public sealed class MessageHistoryTests
{
    private static readonly DateTime Date = new(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReadBatch_OnlyRequestedIncomingMessages_UpdatesExactlyThoseRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var server = CreateServer(options => options.UseSqlite(connection));
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var owner = NewUser("owner");
        var peer = NewUser("peer");
        var third = NewUser("third");
        var visible = NewMessage(1, peer, owner);
        var hidden = NewMessage(2, peer, owner);
        var sent = NewMessage(3, owner, peer);
        var unrelated = NewMessage(4, third, owner);
        var foreign = NewMessage(5, peer, third);
        db.AddRange(owner, peer, third, visible, hidden, sent, unrelated, foreign);
        await db.SaveChangesAsync();

        using var client = ClientAs(server, owner);
        var uri = $"/api/messages/{peer.Id}/read-batch";
        foreach (var request in new object[] { new { }, new { messageIds = Array.Empty<Guid>() }, new { messageIds = new Guid[101] } })
        {
            using var rejected = await client.PostAsJsonAsync(uri, request);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        var ids = new[] { visible.Id, visible.Id, sent.Id, unrelated.Id, foreign.Id, Guid.NewGuid() };
        for (var i = 0; i < 2; i++)
        {
            using var accepted = await client.PostAsJsonAsync(uri, new { messageIds = ids });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(visible.Id, Assert.Single((await accepted.Content.ReadFromJsonAsync<Guid[]>())!));
        }

        Assert.Equal(visible.Id, await db.Messages.AsNoTracking().Where(m => m.IsRead).Select(m => m.Id).SingleAsync());
        Assert.Equal(2, await client.GetFromJsonAsync<int>("/api/messages/unread/count"));
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task History_PostgresEqualTimestampsAndNewMessage_CursorTraversesWithoutGaps()
    {
        await WithPostgres(async (server, db, _) =>
        {
            var owner = NewUser("owner");
            var peer = NewUser("peer");
            var third = NewUser("third");
            var messages = Enumerable.Range(1, 21).Select(i => NewMessage(i, peer, owner)).ToArray();
            var foreign = NewMessage(22, third, owner);
            db.AddRange(owner, peer, third, foreign);
            db.AddRange(messages);
            await db.SaveChangesAsync();
            using var client = ClientAs(server, owner);
            var route = $"/api/messages/{peer.Id}";

            using var latest = await client.GetAsync(route);
            latest.EnsureSuccessStatusCode();
            Assert.Equal(messages.Skip(1).Select(m => m.Id), (await latest.Content.ReadFromJsonAsync<MessageDto[]>())!.Select(m => m.Id));
            var cursor = Assert.Single(latest.Headers.GetValues("X-Next-Before"));
            Assert.Equal(messages[1].Id.ToString(), cursor);

            db.Messages.Add(NewMessage(23, peer, owner));
            await db.SaveChangesAsync();
            using var previous = await client.GetAsync($"{route}?before={cursor}");
            previous.EnsureSuccessStatusCode();
            Assert.Equal(messages[0].Id, Assert.Single((await previous.Content.ReadFromJsonAsync<MessageDto[]>())!).Id);
            Assert.False(previous.Headers.Contains("X-Next-Before"));

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{route}?before={foreign.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{route}?page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{route}?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{route}?before={cursor}&page=2")).StatusCode);
            var legacy = await client.GetFromJsonAsync<MessageDto[]>($"{route}?page=2&pageSize=1");
            Assert.Equal(messages[20].Id, Assert.Single(legacy!).Id);
        });
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task Dialogs_PostgresManyPartners_UsesTwoQueriesAndPreservesProfilePrivacy()
    {
        await WithPostgres(async (server, db, reads) =>
        {
            var owner = NewUser("owner");
            var peers = Enumerable.Range(1, 8).Select(i => NewUser("peer-" + i)).ToArray();
            db.Users.Add(owner);
            db.Users.AddRange(peers);
            peers[0].WishlistPrivacy = WishlistPrivacy.Private;
            peers[1].WishlistPrivacy = WishlistPrivacy.FriendsOnly;
            db.Friendships.Add(new Friendship
            {
                Id = Guid.NewGuid(), RequesterId = peers[1].Id, AddresseeId = owner.Id,
                Status = FriendshipStatus.Accepted, CreatedAt = Date
            });
            db.WishlistItems.AddRange(peers.Take(3).Select(peer => new WishlistItem
            {
                Id = Guid.NewGuid(), OwnerId = peer.Id, Name = "Gift", Url = "https://shop.example/gift", CreatedAt = Date
            }));
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            {
                var incoming = NewMessage(index * 3 + 1, peer, owner);
                var alreadyRead = NewMessage(index * 3 + 2, peer, owner);
                alreadyRead.IsRead = true;
                db.Messages.AddRange(incoming, alreadyRead, NewMessage(index * 3 + 3, owner, peer));
            }
            db.Messages.Add(NewMessage(99, peers[0], peers[1]));
            await db.SaveChangesAsync();

            using var client = ClientAs(server, owner);
            reads.Count = 0;
            var dialogs = (await client.GetFromJsonAsync<DialogDto[]>("/api/messages/dialogs"))!;
            Assert.Equal(2, reads.Count);
            Assert.Equal(peers.Reverse().Select(peer => peer.Id), dialogs.Select(dialog => dialog.User.Id));
            Assert.All(dialogs, dialog =>
            {
                Assert.Equal(1, dialog.UnreadCount);
                Assert.Equal(owner.Id, dialog.LastMessage!.SenderId);
                Assert.Equal(dialog.User.Id, dialog.LastMessage.ReceiverId);
            });
            var byId = dialogs.ToDictionary(dialog => dialog.User.Id);
            Assert.Equal(0, byId[peers[0].Id].User.WishlistItemCount);
            Assert.Equal(FriendshipStatus.Accepted, byId[peers[1].Id].User.FriendshipStatus);
            Assert.Equal(1, byId[peers[1].Id].User.WishlistItemCount);
            Assert.Equal(1, byId[peers[2].Id].User.WishlistItemCount);
        });
    }

    private static TestServer CreateServer(Action<DbContextOptionsBuilder> configure) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(configure);
            services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
            services.AddSingleton(new AvatarStorage(Path.GetTempPath()));
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IMessageService, MessageService>();
            services.AddSignalR();
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(MessagesController).Assembly);
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, context.Request.Headers["X-Test-User"].ToString())], "Test"));
                return next(context);
            });
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));

    private static HttpClient ClientAs(TestServer server, User user)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.Id.ToString());
        return client;
    }

    private static async Task WithPostgres(Func<TestServer, AppDbContext, Reads, Task> run)
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "messages_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        try
        {
            var reads = new Reads();
            using var server = CreateServer(options => options
                .UseNpgsql(scoped, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", schema))
                .AddInterceptors(reads));
            using var scope = server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await run(server, db, reads);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static User NewUser(string username) => new()
    {
        Id = Guid.NewGuid(), UserName = username, NormalizedUserName = username.ToUpperInvariant(),
        DisplayName = username, CreatedAt = Date
    };

    private static Message NewMessage(int ordinal, User sender, User receiver) => new()
    {
        Id = new Guid(ordinal, 0, 0, new byte[8]), SenderId = sender.Id, ReceiverId = receiver.Id,
        Content = $"Message {ordinal}", CreatedAt = Date
    };

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database to run this test.";
        }
    }

    private sealed class Reads : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
