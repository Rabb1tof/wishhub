using System.Net;
using System.Net.Sockets;

namespace WishHub.Parsing;

public static class MetadataHttpTransport
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        MaxResponseHeadersLength = 32,
        MaxConnectionsPerServer = 4,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            if (context.DnsEndPoint.Port != 443 || !(host == "wbray.ru" || IsBasketHost(host)))
                throw new HttpRequestException("Metadata host is not allowed.");

            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            return await ConnectToPublicAddressesAsync(addresses, ConnectToAddressAsync, ct);
        }
    };

    internal static async ValueTask<Stream> ConnectToPublicAddressesAsync(
        IPAddress[] addresses, Func<IPAddress, CancellationToken, ValueTask<Stream>> connect, CancellationToken ct)
    {
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("Metadata DNS must contain only public addresses.");

        SocketException? lastFailure = null;
        foreach (var address in addresses)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await connect(address, ct);
            }
            catch (SocketException ex)
            {
                // An unavailable IPv6 route must not hide a working IPv4 address.
                lastFailure = ex;
            }
        }
        throw new HttpRequestException("No metadata address could be connected.", lastFailure);
    }

    private static async ValueTask<Stream> ConnectToAddressAsync(IPAddress address, CancellationToken ct)
    {
        // Connect to the checked address, avoiding a second DNS lookup/rebinding.
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, 443), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsBasketHost(string host) =>
        host.Length <= 253 && Uri.CheckHostName(host) == UriHostNameType.Dns &&
        host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') &&
        (host.EndsWith(".wbbasket.ru", StringComparison.OrdinalIgnoreCase) ||
         host.EndsWith(".geobasket.ru", StringComparison.OrdinalIgnoreCase));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 ||
                (b[0] == 100 && b[1] is >= 64 and <= 127) ||
                (b[0] == 169 && b[1] == 254) ||
                (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                (b[0] == 192 && (b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2) ||
                    (b[1] == 88 && b[2] == 99))) ||
                (b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100))) ||
                (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }

        // Only global unicast; exclude transition, documentation and special-use ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 0x02) &&
            !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) &&
            !(b[0] == 0x20 && b[1] == 0x02) &&
            !(b[0] == 0x3f && b[1] == 0xff && b[2] < 0x10);
    }
}
