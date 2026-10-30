using System.Net;
using System.Text;
using System.Text.Json;
using WishHub.Core.DTOs;
using WishHub.Parsing;

namespace WishHub.Tests.Parsing;

public sealed class ProductMetadataServiceTests
{
    private const string WbUrl = "https://www.wildberries.ru/catalog/310980140/detail.aspx";
    private const string OzonUrl = "https://www.ozon.ru/product/robot-3437056417/?sku=3437056417&variant=blue";
    private const string Photo = "https://ir.ozone.ru/s3/multimedia-1-o/12685835760.jpg";
    private const string ModMap = """{"recommend":{"mediabasket_route_map":[{"method":"mod","hosts":[{"host":"first.geobasket.ru"},{"host":"second.wbbasket.ru"},{"host":"third.geobasket.ru"}]}]}}""";
    private const string WbCard = """{"nm_id":310980140,"imt_name":"  Vacuum cleaner  ","media":{"photo_count":3}}""";

    [Fact]
    public async Task ResolveAsync_WildberriesModMap_UsesNmIdModuloAndVerifiedCard()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.Host == "cdn.wbbasket.ru" ? ModMap : WbCard)));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http);
        var result = await service.ResolveAsync("  " + WbUrl + "?size=12  ");
        Assert.Equal(new ProductMetadataDto("ok", "Vacuum cleaner", "https://third.geobasket.ru/vol3109/part310980/310980140/images/big/1.webp", "wildberries", null), result);
        Assert.Equal(new[] { "https://cdn.wbbasket.ru/api/v3/upstreams", "https://third.geobasket.ru/vol3109/part310980/310980140/info/ru/card.json" }, handler.Urls);
    }

    [Fact]
    public async Task ResolveAsync_WildberriesRangeMap_UsesInclusiveVolumeBoundaries()
    {
        const string map = """{"recommend":{"mediabasket_route_map":[{"method":"range","hosts":[{"host":"first.geobasket.ru","vol_range_from":0,"vol_range_to":3108},{"host":"range.wbbasket.ru","vol_range_from":3109,"vol_range_to":3109}]}]}}""";
        using var handler = new Handler((r, _) => Task.FromResult(Json(r.RequestUri!.Host == "cdn.wbbasket.ru" ? map : WbCard)));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http);
        var result = await service.ResolveAsync(WbUrl);
        Assert.Equal("ok", result.Status);
        Assert.Equal("https://range.wbbasket.ru/vol3109/part310980/310980140/images/big/1.webp", result.ImageUrl);
        Assert.Equal(2, handler.Urls.Count);
    }

    [Theory]
    [InlineData("unknown-method")]
    [InlineData("empty-hosts")]
    [InlineData("missing-map")]
    [InlineData("missing-range")]
    [InlineData("overlap")]
    [InlineData("malicious-host")]
    [InlineData("host-credentials")]
    [InlineData("host-port")]
    [InlineData("overlap-missing-host")]
    public async Task ResolveAsync_UnsupportedRouteMap_StopsWithoutGuessingHosts(string scenario)
    {
        var map = scenario switch
        {
            "unknown-method" => ModMap.Replace("\"mod\"", "\"uuid\""),
            "empty-hosts" => """{"recommend":{"mediabasket_route_map":[{"method":"mod","hosts":[]}]}}""",
            "missing-map" => "{}",
            "missing-range" => ModMap.Replace("\"mod\"", "\"range\""),
            "overlap" => """{"recommend":{"mediabasket_route_map":[{"method":"range","hosts":[{"host":"a.wbbasket.ru","vol_range_from":3100,"vol_range_to":3200},{"host":"b.wbbasket.ru","vol_range_from":3100,"vol_range_to":3200}]}]}}""",
            "overlap-missing-host" => """{"recommend":{"mediabasket_route_map":[{"method":"range","hosts":[{"vol_range_from":3109,"vol_range_to":3109},{"host":"b.wbbasket.ru","vol_range_from":3109,"vol_range_to":3109}]}]}}""",
            "malicious-host" => ModMap.Replace("third.geobasket.ru", "third.geobasket.ru.evil.test"),
            "host-credentials" => ModMap.Replace("third.geobasket.ru", "evil@third.geobasket.ru"),
            _ => ModMap.Replace("third.geobasket.ru", "third.geobasket.ru:443")
        };
        var (result, count) = await Wb(map, WbCard);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal("unsupported_route_map", result.Reason);
        Assert.Null(result.ImageUrl);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ResolveAsync_WildberriesWrongSku_RejectsNameAndImage()
    {
        var (result, count) = await Wb(ModMap, WbCard.Replace("310980140", "310980141"));
        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "wildberries", "product_mismatch"), result);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("no-photo", "partial", "Vacuum cleaner", false)]
    [InlineData("no-title", "partial", null, true)]
    [InlineData("empty", "unavailable", null, false)]
    public async Task ResolveAsync_WildberriesMissingFields_ReturnsOnlyAvailableSuggestions(string scenario, string status, string? title, bool image)
    {
        var card = scenario switch
        {
            "no-photo" => """{"nm_id":310980140,"imt_name":"Vacuum cleaner","media":{"photo_count":0}}""",
            "no-title" => """{"nm_id":310980140,"media":{"photo_count":1}}""",
            _ => """{"nm_id":310980140}"""
        };
        var (result, _) = await Wb(ModMap, card);
        Assert.Equal(status, result.Status);
        Assert.Equal(title, result.Name);
        Assert.Equal(image, result.ImageUrl is not null);
        Assert.Equal("missing_fields", result.Reason);
    }

    [Theory]
    [InlineData("spoof-host")]
    [InlineData("userinfo")]
    [InlineData("private-ip")]
    [InlineData("port")]
    [InlineData("non-http")]
    [InlineData("ozon-short-link")]
    [InlineData("sku-in-query")]
    [InlineData("path-prefix")]
    [InlineData("zero-sku")]
    [InlineData("overflow-sku")]
    [InlineData("too-long")]
    [InlineData("market")]
    public async Task ResolveAsync_UnsupportedOrMaliciousUrl_DoesNotFetch(string scenario)
    {
        var url = scenario switch
        {
            "spoof-host" => "https://www.ozon.ru.evil.test/product/3437056417/",
            "userinfo" => "https://evil@www.ozon.ru/product/3437056417/",
            "private-ip" => "http://127.0.0.1/product/3437056417/",
            "port" => "https://www.ozon.ru:444/product/3437056417/",
            "non-http" => "file:///product/3437056417/",
            "ozon-short-link" => "https://ozon.ru/t/opaque",
            "sku-in-query" => "https://ozon.ru/category/shoes/?sku=3437056417",
            "path-prefix" => "https://ozon.ru/anything/product/3437056417/",
            "zero-sku" => "https://ozon.ru/product/0/",
            "overflow-sku" => "https://ozon.ru/product/9999999999999999999/",
            "too-long" => OzonUrl + new string('a', 2000),
            _ => "https://market.yandex.ru/product--thing/123?sku=456"
        };
        using var handler = new Handler((_, _) => throw new Xunit.Sdk.XunitException("No outgoing request expected"));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        var result = await service.ResolveAsync(url);
        Assert.Equal("unsupported", result.Status);
        Assert.Null(result.Name);
        Assert.Null(result.ImageUrl);
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task ResolveAsync_OzonDisabled_DoesNotContactThirdParty()
    {
        using var handler = new Handler((_, _) => throw new Xunit.Sdk.XunitException("Disabled source contacted"));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http);
        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "ozon", "source_disabled"), await service.ResolveAsync(OzonUrl));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task ResolveAsync_OzonEnabled_PostsFullUrlAndAcceptsExactSku()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://wbray.ru/api/ozon/analyze/", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(OzonUrl, body.RootElement.GetProperty("card").GetString());
            Assert.Equal("", body.RootElement.GetProperty("client_id").GetString());
            Assert.False(body.RootElement.GetProperty("with_video").GetBoolean());
            Assert.Equal(3, body.RootElement.EnumerateObject().Count());
            return Json(OzonCard());
        });
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        Assert.Equal(new ProductMetadataDto("ok", "Vacuum cleaner", Photo, "ozon", null), await service.ResolveAsync(OzonUrl));
        Assert.Single(handler.Urls);
    }

    [Theory]
    [InlineData("wrong-sku", "product_mismatch")]
    [InlineData("missing-sku", "product_mismatch")]
    [InlineData("wrong-card-url", "product_mismatch")]
    [InlineData("captcha", "source_unavailable")]
    [InlineData("captcha-title", "source_unavailable")]
    [InlineData("generic-page", "source_unavailable")]
    [InlineData("html", "invalid_response")]
    [InlineData("bad-schema", "source_unavailable")]
    public async Task ResolveAsync_OzonInvalidIdentityOrBlockedResponse_RejectsSuggestions(string scenario, string reason)
    {
        var json = scenario switch
        {
            "wrong-sku" => OzonCard().Replace("3437056417", "3437056418"),
            "missing-sku" => OzonCard().Replace("\"sku\"", "\"other\""),
            "wrong-card-url" => OzonCard().Replace("https://www.ozon.ru/product/3437056417/", "https://evil.test/product/3437056417/"),
            "captcha" => """{"status":"captcha","card":{"sku":"3437056417"}}""",
            "captcha-title" => OzonCard(title: "Access denied CAPTCHA"),
            "generic-page" => OzonCard(title: "Ozon", image: "https://ir.ozone.ru/s3/cms/logo.jpg"),
            "html" => "<html>CAPTCHA</html>",
            _ => "[]"
        };
        var result = await Ozon(json);
        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "ozon", reason), result);
    }

    [Theory]
    [InlineData("private-host")]
    [InlineData("spoof-host")]
    [InlineData("credentials")]
    [InlineData("non-image")]
    [InlineData("logo")]
    [InlineData("http")]
    public async Task ResolveAsync_OzonUnsafeImage_ReturnsNameOnly(string scenario)
    {
        var image = scenario switch
        {
            "private-host" => "https://127.0.0.1/s3/photo.jpg",
            "spoof-host" => "https://ir.ozone.ru.evil.test/s3/photo.jpg",
            "credentials" => "https://evil@ir.ozone.ru/s3/photo.jpg",
            "non-image" => "https://ir.ozone.ru/s3/video.mp4",
            "logo" => "https://ir.ozone.ru/s3/cms/logo.jpg",
            _ => "http://ir.ozone.ru/s3/photo.jpg"
        };
        Assert.Equal(new ProductMetadataDto("partial", "Vacuum cleaner", null, "ozon", "missing_fields"), await Ozon(OzonCard(image: image)));
    }

    [Fact]
    public async Task ResolveAsync_OzonMissingMainPhoto_AcceptsOneOfFirstFourFallbacks()
    {
        var result = await Ozon(OzonCard(image: null, photos: new[] { new { source_url = "https://evil.test/photo.jpg" }, new { source_url = Photo } }));
        Assert.Equal("ok", result.Status);
        Assert.Equal(Photo, result.ImageUrl);
    }

    [Fact]
    public async Task ResolveAsync_OzonSixthCandidate_DoesNotExceedFiveCandidateBudget()
    {
        var photos = Enumerable.Range(0, 5).Select(i => new { source_url = i == 4 ? Photo : "https://evil.test/photo.jpg" }).ToArray();
        var result = await Ozon(OzonCard(image: "https://evil.test/main.jpg", photos: photos));
        Assert.Equal("partial", result.Status);
        Assert.Null(result.ImageUrl);
    }

    [Theory]
    [InlineData(403, "source_unavailable")]
    [InlineData(429, "rate_limited")]
    [InlineData(302, "redirect_rejected")]
    [InlineData(500, "source_unavailable")]
    public async Task ResolveAsync_HttpFailure_DoesNotRetryOrFollowRedirect(int status, string reason)
    {
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "ozon", reason), await service.ResolveAsync(OzonUrl));
        Assert.Single(handler.Urls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveAsync_OversizeResponse_StopsAtBoundWithOrWithoutContentLength(bool contentLength)
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes(new string(' ', ProductMetadataService.MaxResponseBytes + 500)));
        using var handler = new Handler((_, _) =>
        {
            HttpContent content = contentLength ? new ByteArrayContent(new byte[ProductMetadataService.MaxResponseBytes + 1]) : new StreamContent(stream);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        Assert.Equal("response_too_large", (await service.ResolveAsync(OzonUrl)).Reason);
        Assert.Equal(contentLength ? 0 : ProductMetadataService.MaxResponseBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task ResolveAsync_ResponseExactlyAtSizeLimit_IsAccepted()
    {
        var json = OzonCard();
        json += new string(' ', ProductMetadataService.MaxResponseBytes - Encoding.UTF8.GetByteCount(json));
        Assert.Equal("ok", (await Ozon(json)).Status);
    }

    [Fact]
    public async Task ResolveAsync_CallerCancellation_PropagatesAndReleasesConcurrencySlot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = true;
        using var handler = new Handler(async (_, ct) =>
        {
            entered.TrySetResult();
            if (cancel) await Task.Delay(Timeout.Infinite, ct);
            return Json(OzonCard());
        });
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        using var cts = new CancellationTokenSource();
        var task = service.ResolveAsync(OzonUrl, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        cancel = false;
        Assert.Equal("ok", (await service.ResolveAsync(OzonUrl)).Status);
    }

    [Fact]
    public async Task ResolveAsync_DeadlineIncludesBodyRead_ReturnsTimeoutWithinTwelveSeconds()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) }));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new ProductMetadataService(http, true);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.ResolveAsync(OzonUrl).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("timeout", result.Reason);
        Assert.Null(result.Name);
        Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ResolveAsync_FifthConcurrentCall_ReturnsBusyWithoutQueueOrHttpCall()
    {
        using var release = new CancellationTokenSource();
        using var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        var running = Enumerable.Range(0, 4).Select(_ => service.ResolveAsync(OzonUrl, release.Token)).ToArray();
        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "ozon", "busy"), await service.ResolveAsync(OzonUrl));
        Assert.Equal(4, handler.Urls.Count);
        release.Cancel();
        foreach (var task in running) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static async Task<(ProductMetadataDto Result, int Count)> Wb(string map, string card)
    {
        using var handler = new Handler((r, _) => Task.FromResult(Json(r.RequestUri!.Host == "cdn.wbbasket.ru" ? map : card)));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http);
        return (await service.ResolveAsync(WbUrl), handler.Urls.Count);
    }
    private static async Task<ProductMetadataDto> Ozon(string response)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(response)));
        using var http = new HttpClient(handler);
        using var service = new ProductMetadataService(http, true);
        return await service.ResolveAsync(OzonUrl);
    }
    private static string OzonCard(string? title = "Vacuum cleaner", string? image = Photo, object? photos = null) =>
        JsonSerializer.Serialize(new { status = "success", card = new { sku = "3437056417", card_url = "https://www.ozon.ru/product/3437056417/", title, main_photo = image, photos } });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return send(request, cancellationToken);
        }
    }
    private class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
    private sealed class HangingStream() : TrackingStream([])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
