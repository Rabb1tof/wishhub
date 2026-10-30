using System.Net;
using System.Net.Sockets;
using WishHub.Parsing;

namespace WishHub.Tests.Parsing;

public sealed class MetadataHttpTransportTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::7f00:1")]
    public void IsPublicAddress_NonPublicOrTransitionAddress_IsRejected(string address) =>
        Assert.False(MetadataHttpTransport.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("192.2.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_PublicAddress_IsAccepted(string address) =>
        Assert.True(MetadataHttpTransport.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("evilwbbasket.ru")]
    [InlineData("cdn.wbbasket.ru.evil.test")]
    [InlineData("evil@cdn.wbbasket.ru")]
    [InlineData("cdn.wbbasket.ru:443")]
    [InlineData("127.0.0.1")]
    [InlineData("https://cdn.wbbasket.ru")]
    [InlineData("cdn.wbbasket.ru/path")]
    public void IsBasketHost_SpoofedAuthority_IsRejected(string host) =>
        Assert.False(MetadataHttpTransport.IsBasketHost(host));

    [Fact]
    public async Task ConnectToPublicAddressesAsync_FirstAddressUnavailable_UsesNextCheckedAddressAndSameToken()
    {
        var addresses = new[] { IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("8.8.8.8") };
        var attempted = new List<IPAddress>();
        using var cancellation = new CancellationTokenSource();
        using var expected = new MemoryStream();
        var result = await MetadataHttpTransport.ConnectToPublicAddressesAsync(addresses, (address, ct) =>
        {
            Assert.Equal(cancellation.Token, ct);
            attempted.Add(address);
            return address.Equals(addresses[0])
                ? ValueTask.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable))
                : ValueTask.FromResult<Stream>(expected);
        }, cancellation.Token);
        Assert.Same(expected, result);
        Assert.Equal(addresses, attempted);
    }

    [Fact]
    public async Task ConnectToPublicAddressesAsync_CancelledConnect_DoesNotTryAnotherAddress()
    {
        var addresses = new[] { IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("8.8.8.8") };
        var attempted = new List<IPAddress>();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await MetadataHttpTransport.ConnectToPublicAddressesAsync(addresses, (address, ct) =>
            {
                attempted.Add(address);
                cancellation.Cancel();
                return ValueTask.FromCanceled<Stream>(ct);
            }, cancellation.Token));
        Assert.Equal(addresses[0], Assert.Single(attempted));
    }

    [Fact]
    public async Task ConnectToPublicAddressesAsync_MixedPublicAndPrivateDns_RejectsBeforeAnyConnection()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await MetadataHttpTransport.ConnectToPublicAddressesAsync(
                new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1") }, (_, _) =>
                {
                    attempts++;
                    return ValueTask.FromResult<Stream>(new MemoryStream());
                }, CancellationToken.None));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task ConnectToPublicAddressesAsync_AllAddressesFail_ReportsFailureWithoutRepeatingAddresses()
    {
        var addresses = new[] { IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("8.8.8.8") };
        var attempted = new List<IPAddress>();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await MetadataHttpTransport.ConnectToPublicAddressesAsync(addresses, (address, _) =>
            {
                attempted.Add(address);
                return ValueTask.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable));
            }, CancellationToken.None));
        Assert.Equal(addresses, attempted);
        Assert.IsType<SocketException>(exception.InnerException);
    }

    [Fact]
    public async Task CreateHandler_InternalRedirectAndProxy_AreDisabledAndForeignHostCannotConnect()
    {
        using var handler = MetadataHttpTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetAsync("https://127.0.0.1/"));
    }
}
