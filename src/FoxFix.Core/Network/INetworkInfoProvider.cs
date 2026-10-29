using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FoxFix.Core.Network;

public interface INetworkInfoProvider
{
    IReadOnlyList<AdapterInfo> GetAdapters();
}

public sealed class SystemNetworkInfoProvider : INetworkInfoProvider
{
    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(ToAdapterInfo)
            .ToList();
    }

    /// <summary>True when somebody typed DNS servers into the adapter (they are in the registry), false when it takes them from the modem.</summary>
    private static bool? HasHandTypedDns(string adapterId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(adapterId) || adapterId.Any(c => c is '\\' or '/'))
        {
            return null;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + adapterId);
            return key is null ? null : !string.IsNullOrWhiteSpace(key.GetValue("NameServer") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? FormatMac(NetworkInterface nic)
    {
        var bytes = nic.GetPhysicalAddress().GetAddressBytes();
        return bytes.Length == 0 ? null : BitConverter.ToString(bytes);
    }

    private static AdapterInfo ToAdapterInfo(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();

        var addresses = properties.UnicastAddresses
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();

        var gateways = properties.GatewayAddresses
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
            .ToList();

        var dns = properties.DnsAddresses.ToList();

        return new AdapterInfo(
            nic.Name, nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus, addresses, gateways, dns, nic.Id, FormatMac(nic),
            HasHandTypedDns(nic.Id));
    }
}
