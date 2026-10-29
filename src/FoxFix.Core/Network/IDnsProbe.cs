using System.Net;
using System.Net.Sockets;

namespace FoxFix.Core.Network;

public interface IDnsProbe
{
    /// <summary>Resolves through the operating system's configured DNS (what apps actually use).</summary>
    Task<bool> ResolveWithSystemAsync(string host, TimeSpan timeout, CancellationToken ct);

    Task<bool> QueryUdpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct);

    Task<bool> QueryTcpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct);

    /// <summary>The addresses the system resolver returns. Empty when the name does not resolve.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string host, TimeSpan timeout, CancellationToken ct);
}

public sealed class SystemDnsProbe : IDnsProbe
{
    private const int DnsPort = 53;

    public async Task<bool> ResolveWithSystemAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = LinkedTimeout(timeout, ct);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
            return addresses.Length > 0;
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = LinkedTimeout(timeout, ct);
        try
        {
            return await Dns.GetHostAddressesAsync(host, cts.Token);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return [];
        }
    }

    public async Task<bool> QueryUdpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct)
    {
        var id = NewId();
        var query = DnsPacket.BuildQuery(host, id);

        using var cts = LinkedTimeout(timeout, ct);
        using var udp = new UdpClient(server.AddressFamily);
        try
        {
            await udp.SendAsync(query, new IPEndPoint(server, DnsPort), cts.Token);

            while (true)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                if (DnsPacket.IsValidAnswer(result.Buffer, id))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return false;
        }
    }

    public async Task<bool> QueryTcpAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken ct)
    {
        var id = NewId();
        var query = DnsPacket.BuildQuery(host, id);

        using var cts = LinkedTimeout(timeout, ct);
        using var tcp = new TcpClient(server.AddressFamily);
        try
        {
            await tcp.ConnectAsync(server, DnsPort, cts.Token);
            var stream = tcp.GetStream();

            var framed = new byte[query.Length + 2];
            framed[0] = (byte)(query.Length >> 8);
            framed[1] = (byte)query.Length;
            query.CopyTo(framed, 2);
            await stream.WriteAsync(framed, cts.Token);

            var lengthBytes = new byte[2];
            await stream.ReadExactlyAsync(lengthBytes, cts.Token);
            var length = (lengthBytes[0] << 8) | lengthBytes[1];

            var response = new byte[length];
            await stream.ReadExactlyAsync(response, cts.Token);
            return DnsPacket.IsValidAnswer(response, id);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return false;
        }
    }

    private static ushort NewId() => (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);

    private static CancellationTokenSource LinkedTimeout(TimeSpan timeout, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    /// <summary>Timeouts and socket errors mean "no answer"; a cancel requested by the caller must propagate.</summary>
    private static bool IsNetworkFailure(Exception ex, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is OperationCanceledException or SocketException or IOException or EndOfStreamException;
    }
}
