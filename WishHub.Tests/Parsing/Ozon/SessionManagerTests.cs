using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Moq;
using System.Text.Json;
using WishHub.Parsing.Ozon;

namespace WishHub.Tests.Parsing.Ozon;

public class SessionManagerTests
{
    [Fact]
    public async Task WarmUpSessionAsync_UsesExpectedWarmUpFlow()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var random = new Mock<IRandomValueProvider>();
            random.SetupSequence(x => x.Next(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(2500)
                .Returns(3)
                .Returns(220)
                .Returns(300)
                .Returns(260)
                .Returns(350)
                .Returns(310)
                .Returns(400)
                .Returns(1500);

            var context = new Mock<IBrowserContext>();
            var mouse = new Mock<IMouse>();
            var page = new Mock<IPage>();

            page.SetupGet(x => x.Mouse).Returns(mouse.Object);
            page.Setup(x => x.GotoAsync("https://www.ozon.ru/", It.IsAny<PageGotoOptions>())).ReturnsAsync((IResponse?)null);
            page.Setup(x => x.WaitForTimeoutAsync(It.IsAny<float>())).Returns(Task.CompletedTask);
            mouse.Setup(x => x.WheelAsync(0, It.IsAny<float>())).Returns(Task.CompletedTask);

            var manager = CreateManager(tempDir, random.Object);

            await manager.WarmUpSessionAsync(context.Object, page.Object);

            page.Verify(x => x.GotoAsync("https://www.ozon.ru/", It.Is<PageGotoOptions>(o => o.WaitUntil == WaitUntilState.DOMContentLoaded)), Times.Once);
            page.Verify(x => x.WaitForTimeoutAsync(It.Is<float>(value => value >= 2000 && value <= 4000)), Times.Once);
            page.Verify(x => x.WaitForTimeoutAsync(It.Is<float>(value => value >= 1000 && value <= 2000)), Times.Once);
            mouse.Verify(x => x.WheelAsync(0, It.Is<float>(value => value >= 180 && value <= 480)), Times.Exactly(3));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SaveStateAsync_PersistsStateInMemoryAndOnDisk()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            const string sessionId = "session-save";
            const string stateJson = "{\"cookies\":[],\"origins\":[]}";

            var context = new Mock<IBrowserContext>();
            context
                .Setup(x => x.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>()))
                .ReturnsAsync(stateJson);

            var manager = CreateManager(tempDir);

            await manager.SaveStateAsync(context.Object, sessionId);

            manager.TryGetMetadata(sessionId, out var metadata).Should().BeTrue();
            metadata.Should().NotBeNull();
            metadata!.LastStateSavedAtUtc.Should().NotBeNull();
            File.Exists(metadata.StatePath).Should().BeTrue();
            (await File.ReadAllTextAsync(metadata.StatePath)).Should().Be(stateJson);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SaveStateAsync_AfterWarmUp_PreservesWarmUpMetadata()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            const string sessionId = "session-warmed";

            var random = new Mock<IRandomValueProvider>();
            random.SetupSequence(x => x.Next(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(2000)
                .Returns(2)
                .Returns(200)
                .Returns(300)
                .Returns(220)
                .Returns(350)
                .Returns(1000);

            var context = new Mock<IBrowserContext>();
            var mouse = new Mock<IMouse>();
            var page = new Mock<IPage>();

            page.SetupGet(x => x.Mouse).Returns(mouse.Object);
            page.Setup(x => x.GotoAsync("https://www.ozon.ru/", It.IsAny<PageGotoOptions>())).ReturnsAsync((IResponse?)null);
            page.Setup(x => x.WaitForTimeoutAsync(It.IsAny<float>())).Returns(Task.CompletedTask);
            mouse.Setup(x => x.WheelAsync(0, It.IsAny<float>())).Returns(Task.CompletedTask);
            context.Setup(x => x.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>())).ReturnsAsync("{\"cookies\":[],\"origins\":[]}");

            var manager = CreateManager(tempDir, random.Object);

            await manager.WarmUpSessionAsync(context.Object, page.Object);
            await manager.SaveStateAsync(context.Object, sessionId);

            manager.TryGetMetadata(sessionId, out var metadata).Should().BeTrue();
            metadata!.WarmedUpAtUtc.Should().NotBeNull();
            metadata.LastStateSavedAtUtc.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RestoreStateAsync_AppliesCookiesAndLocalStorage()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            const string sessionId = "session-restore";
            const string stateJson = """
{
  "cookies": [
    {
      "name": "sessionid",
      "value": "abc123",
      "domain": ".ozon.ru",
      "path": "/",
      "expires": 1893456000,
      "httpOnly": true,
      "secure": true,
      "sameSite": "Lax"
    }
  ],
  "origins": [
    {
      "origin": "https://www.ozon.ru",
      "localStorage": [
        {
          "name": "region",
          "value": "moskva"
        }
      ]
    }
  ]
}
""";

            var context = new Mock<IBrowserContext>();
            var page = new Mock<IPage>();
            IEnumerable<Cookie>? restoredCookies = null;
            object? storagePayload = null;

            var manager = CreateManager(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, $"{sessionId}.json"), stateJson);

            context
                .Setup(x => x.AddCookiesAsync(It.IsAny<IEnumerable<Cookie>>()))
                .Callback<IEnumerable<Cookie>>(cookies => restoredCookies = cookies)
                .Returns(Task.CompletedTask);
            context
                .Setup(x => x.NewPageAsync())
                .ReturnsAsync(page.Object);

            page
                .Setup(x => x.GotoAsync("https://www.ozon.ru", It.IsAny<PageGotoOptions>()))
                .ReturnsAsync((IResponse?)null);
            page
                .Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<object?>()))
                .Callback<string, object?>((_, payload) => storagePayload = payload)
                .ReturnsAsync((JsonElement?)null);
            page
                .Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>()))
                .Returns(Task.CompletedTask);

            var restored = await manager.RestoreStateAsync(context.Object, sessionId);

            restored.Should().BeTrue();
            restoredCookies.Should().NotBeNull();
            var restoredCookie = restoredCookies!.Single();
            restoredCookie.Name.Should().Be("sessionid");
            restoredCookie.Domain.Should().Be(".ozon.ru");
            storagePayload.Should().NotBeNull();
            manager.TryGetMetadata(sessionId, out var metadata).Should().BeTrue();
            metadata!.LastStateRestoredAtUtc.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RestoreStateAsync_ReturnsFalseForBlockedSession()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var context = new Mock<IBrowserContext>();
            var manager = CreateManager(tempDir);

            manager.MarkBlocked("blocked-session");

            var restored = await manager.RestoreStateAsync(context.Object, "blocked-session");

            restored.Should().BeFalse();
            context.Verify(x => x.AddCookiesAsync(It.IsAny<IEnumerable<Cookie>>()), Times.Never);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MarkBlocked_SetsMetadataFlag()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var manager = CreateManager(tempDir);

            manager.MarkBlocked("blocked-session");

            manager.TryGetMetadata("blocked-session", out var metadata).Should().BeTrue();
            metadata!.IsBlocked.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static SessionManager CreateManager(string tempDir, IRandomValueProvider? random = null)
    {
        return new SessionManager(
            Options.Create(new SessionManagerOptions { StateDirectory = tempDir }),
            random ?? new StubRandomValueProvider(),
            NullLogger<SessionManager>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wishhub-session-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubRandomValueProvider : IRandomValueProvider
    {
        public int Next(int minValue, int maxValueExclusive) => minValue;
    }
}
