using System.Net;
using FoxFix.Core.Network;

namespace FoxFix.Core.Diagnostics.Checks;

/// <summary>Layer 2: did the adapter get a real IPv4 address and a gateway from the modem?</summary>
public sealed class IpConfigCheck : ICheck
{
    private readonly INetworkInfoProvider _network;

    public IpConfigCheck(INetworkInfoProvider network)
    {
        _network = network;
    }

    public string Id => "ipconfig";

    public string Title => Strings.Get(Strings.IpConfigTitle);

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var up = _network.GetAdapters().Where(a => a.IsPhysical && a.IsUp).ToList();
        if (up.Count == 0)
        {
            return Task.FromResult(new CheckResult(CheckStatus.Skipped, Strings.Get(Strings.IpConfigSkipped)));
        }

        foreach (var adapter in up)
        {
            var address = adapter.IPv4Addresses.FirstOrDefault(IsUsable);
            if (address is not null && adapter.Gateways.Count > 0)
            {
                var summary = Strings.Format(Strings.IpConfigOk, address, adapter.Gateways[0]);
                return Task.FromResult(new CheckResult(CheckStatus.Passed, summary));
            }
        }

        return Task.FromResult(Describe(up));
    }

    private static CheckResult Describe(IReadOnlyList<AdapterInfo> up)
    {
        var names = string.Join(", ", up.Select(a => a.Name));

        if (up.Any(a => a.IPv4Addresses.Any(IsApipa)))
        {
            return new CheckResult(CheckStatus.Failed, Strings.Get(Strings.IpConfigApipa), names);
        }

        if (up.All(a => !a.IPv4Addresses.Any(IsUsable)))
        {
            return new CheckResult(CheckStatus.Failed, Strings.Get(Strings.IpConfigNoAddress), names);
        }

        return new CheckResult(CheckStatus.Warning, Strings.Get(Strings.IpConfigNoGateway), names);
    }

    private static bool IsApipa(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsUsable(IPAddress address)
    {
        return !IsApipa(address) && !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any);
    }
}
