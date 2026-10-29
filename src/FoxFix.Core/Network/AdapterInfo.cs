using System.Net;
using System.Net.NetworkInformation;

namespace FoxFix.Core.Network;

public sealed record AdapterInfo(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    IReadOnlyList<IPAddress> IPv4Addresses,
    IReadOnlyList<IPAddress> Gateways,
    IReadOnlyList<IPAddress>? DnsServers = null,
    string? Id = null,
    string? MacAddress = null,
    bool? DnsIsStatic = null)
{
    public IReadOnlyList<IPAddress> Dns => DnsServers ?? [];

    public bool IsPhysical => AdapterClassifier.IsPhysical(Type, Description);

    public bool IsUp => Status == OperationalStatus.Up;
}

public static class AdapterClassifier
{
    private static readonly NetworkInterfaceType[] PhysicalTypes =
    [
        NetworkInterfaceType.Ethernet,
        NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.FastEthernetT,
        NetworkInterfaceType.FastEthernetFx,
        NetworkInterfaceType.Wireless80211,
    ];

    // Virtual, VPN and Wi-Fi Direct adapters must never be touched (plan section 7, item 8).
    private static readonly string[] VirtualMarkers =
    [
        "virtual", "vmware", "hyper-v", "vethernet", "virtualbox", "tap-", "vpn", "wintun",
        "wireguard", "wi-fi direct", "bluetooth", "loopback", "tunnel", "pseudo", "npcap", "wan miniport",
        // Filter-driver bindings Windows lists as extra "Ethernet" interfaces next to every real adapter.
        "lightweight filter", "packet scheduler", "kernel debug", "winpkfilter",
    ];

    public static bool IsPhysical(NetworkInterfaceType type, string description)
    {
        if (!PhysicalTypes.Contains(type))
        {
            return false;
        }

        return !VirtualMarkers.Any(m => description.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
