using System.Net;
using System.Net.NetworkInformation;
using FoxFix.Core.Network;

namespace FoxFix.Core.Tests;

internal static class Adapters
{
    public static AdapterInfo Wifi(
        OperationalStatus status = OperationalStatus.Up,
        string[]? addresses = null,
        string[]? gateways = null,
        string description = "Intel(R) Wi-Fi 6 AX201 160MHz",
        string[]? dns = null)
    {
        return new AdapterInfo(
            "Wi-Fi",
            description,
            NetworkInterfaceType.Wireless80211,
            status,
            (addresses ?? []).Select(IPAddress.Parse).ToList(),
            (gateways ?? []).Select(IPAddress.Parse).ToList(),
            (dns ?? []).Select(IPAddress.Parse).ToList());
    }

    public static AdapterInfo Healthy() => Wifi(addresses: ["192.168.1.20"], gateways: ["192.168.1.1"]);
}

internal sealed class FakeNetworkInfo : INetworkInfoProvider
{
    private readonly IReadOnlyList<AdapterInfo> _adapters;

    public FakeNetworkInfo(params AdapterInfo[] adapters)
    {
        _adapters = adapters;
    }

    public IReadOnlyList<AdapterInfo> GetAdapters() => _adapters;
}

internal sealed class FakePinger : IPinger
{
    private readonly HashSet<string> _reachable;

    public FakePinger(params string[] reachable)
    {
        _reachable = reachable.ToHashSet();
    }

    public Task<bool> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken ct)
    {
        return Task.FromResult(_reachable.Contains(address.ToString()));
    }
}
