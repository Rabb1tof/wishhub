using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using WishHub.Api.Controllers;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

public sealed class UserPrivacySecurityTests
{
    private static readonly Guid OwnerId = Guid.Parse("441615dc-b6dd-4ae4-b222-d593f30f4fa8");

    [Theory]
    [InlineData("unknown-name")]
    [InlineData("defined-number")]
    [InlineData("undefined-number")]
    [InlineData("negative-number")]
    [InlineData("composite-name")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("padded-name")]
    public async Task UpdateProfile_InvalidPrivacy_ReturnsBadRequestBeforeServiceCall(string scenario)
    {
        var service = ApiService();
        using var server = Server(service);
        using var client = server.CreateClient();

        using var response = await client.PutAsJsonAsync("/api/users/me", new
        {
            displayName = "Should not change", wishlistPrivacy = InvalidPrivacy(scenario)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("WishlistPrivacy", out _));
        await service.DidNotReceive().UpdateProfileAsync(Arg.Any<Guid>(), Arg.Any<UpdateProfileRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unknown-name")]
    [InlineData("defined-number")]
    [InlineData("undefined-number")]
    [InlineData("negative-number")]
    [InlineData("composite-name")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("padded-name")]
    public async Task UpdateProfile_InvalidPrivacy_LeavesTrackedAndPersistedProfileUnchanged(string scenario)
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync(WishlistPrivacy.Private);
        await using var provider = Services(store);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracked = await db.Users.SingleAsync(user => user.Id == owner.Id);
        var service = scope.ServiceProvider.GetRequiredService<UserService>();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateProfileAsync(owner.Id, new UpdateProfileRequest
        {
            DisplayName = "Should not change", Bio = "Should not persist", WishlistPrivacy = InvalidPrivacy(scenario)
        }));

        Assert.Equal("Wishlist user", tracked.DisplayName);
        Assert.Null(tracked.Bio);
        Assert.Equal(WishlistPrivacy.Private, tracked.WishlistPrivacy);
        Assert.False(db.ChangeTracker.HasChanges());
        var saved = await db.Users.AsNoTracking().SingleAsync(user => user.Id == owner.Id);
        Assert.Equal("Wishlist user", saved.DisplayName);
        Assert.Null(saved.Bio);
        Assert.Equal(WishlistPrivacy.Private, saved.WishlistPrivacy);
    }

    [Theory]
    [InlineData("pUbLiC", WishlistPrivacy.Public)]
    [InlineData("fRiEnDsOnLy", WishlistPrivacy.FriendsOnly)]
    [InlineData("PRIVATE", WishlistPrivacy.Private)]
    public async Task UpdateProfile_NamedPrivacy_AcceptsAndPersistsCaseInsensitiveValue(string privacy, WishlistPrivacy expected)
    {
        var apiService = ApiService();
        using var server = Server(apiService);
        using var client = server.CreateClient();
        using var response = await client.PutAsJsonAsync("/api/users/me", new { wishlistPrivacy = privacy });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await apiService.Received(1).UpdateProfileAsync(OwnerId,
            Arg.Is<UpdateProfileRequest>(request => request.WishlistPrivacy == privacy), Arg.Any<CancellationToken>());

        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync(expected == WishlistPrivacy.Private ? WishlistPrivacy.Public : WishlistPrivacy.Private);
        await using var provider = Services(store);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserService>();
        var result = await service.UpdateProfileAsync(owner.Id, new UpdateProfileRequest { WishlistPrivacy = privacy });
        Assert.Equal(expected.ToString(), result!.WishlistPrivacy);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(expected, await db.Users.AsNoTracking().Where(user => user.Id == owner.Id).Select(user => user.WishlistPrivacy).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateProfile_NullOrOmittedPrivacy_PreservesPrivacyAndUpdatesOtherFields(bool explicitNull)
    {
        var apiService = ApiService();
        using var server = Server(apiService);
        using var client = server.CreateClient();
        var payload = new Dictionary<string, string?> { ["displayName"] = "Updated name", ["bio"] = "Updated bio" };
        if (explicitNull) payload["wishlistPrivacy"] = null;
        using var response = await client.PutAsJsonAsync("/api/users/me", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await apiService.Received(1).UpdateProfileAsync(OwnerId,
            Arg.Is<UpdateProfileRequest>(request => request.WishlistPrivacy == null && request.DisplayName == "Updated name" && request.Bio == "Updated bio"),
            Arg.Any<CancellationToken>());

        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync(WishlistPrivacy.Private);
        await using var provider = Services(store);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserService>();
        var result = await service.UpdateProfileAsync(owner.Id, new UpdateProfileRequest { DisplayName = "Updated name", Bio = "Updated bio" });
        Assert.Equal("Private", result!.WishlistPrivacy);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Users.AsNoTracking().SingleAsync(user => user.Id == owner.Id);
        Assert.Equal(WishlistPrivacy.Private, saved.WishlistPrivacy);
        Assert.Equal("Updated name", saved.DisplayName);
        Assert.Equal("Updated bio", saved.Bio);
    }

    private static string InvalidPrivacy(string scenario) => scenario switch
    {
        "unknown-name" => "Friends",
        "defined-number" => "0",
        "undefined-number" => "17",
        "negative-number" => "-1",
        "composite-name" => "Public, Private",
        "empty" => "",
        "whitespace" => " \t\r\n",
        "padded-name" => " Private ",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static ServiceProvider Services(WishlistTestStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(store.Connection));
        services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<UserService>();
        return services.BuildServiceProvider();
    }

    private static IUserService ApiService()
    {
        var service = Substitute.For<IUserService>();
        service.UpdateProfileAsync(OwnerId, Arg.Any<UpdateProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = OwnerId });
        return service;
    }

    private static TestServer Server(IUserService service) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddAuthorization();
            services.AddSingleton(service);
            services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString())], "validation-test"));
                return next();
            });
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
}
