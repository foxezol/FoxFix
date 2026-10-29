using System.Runtime.Versioning;
using FoxFix.Core.Commands;
using Microsoft.Win32;

namespace FoxFix.Core.Network;

/// <summary>An adapter's static DNS servers. An empty list means "automatic (from the modem)".</summary>
public sealed record DnsSnapshot(string AdapterName, IReadOnlyList<string> V4Servers, IReadOnlyList<string> V6Servers);

/// <summary>The only mutating door to Windows DNS settings. Fakes stand in for it in tests.</summary>
public interface IDnsConfigurator
{
    /// <summary>Null when the adapter's settings cannot be read, so no backup is possible.</summary>
    Task<DnsSnapshot?> ReadAsync(AdapterInfo adapter, CancellationToken ct);

    Task<bool> SetAutomaticAsync(string adapterName, CancellationToken ct);

    /// <summary>Puts a snapshot back exactly: static servers, or automatic when the lists are empty.</summary>
    Task<bool> RestoreAsync(DnsSnapshot snapshot, CancellationToken ct);

    Task<bool> FlushCacheAsync(CancellationToken ct);

    /// <summary>Sets static servers for both address families. The IPv6 part is best effort when the list is empty.</summary>
    Task<bool> SetStaticAsync(string adapterName, IReadOnlyList<string> v4Servers, IReadOnlyList<string> v6Servers, CancellationToken ct);
}

/// <summary>
/// netsh / ipconfig argument lists. The adapter name is passed as its own token, never glued into a
/// string, so names with spaces or quotes cannot change the command.
/// </summary>
public static class NetshArguments
{
    public static IReadOnlyList<string> SetAutomaticV4(string adapter) => ["interface", "ip", "set", "dns", adapter, "dhcp"];

    public static IReadOnlyList<string> SetAutomaticV6(string adapter) => ["interface", "ipv6", "set", "dnsservers", adapter, "dhcp"];

    public static IReadOnlyList<string> SetStatic(bool ipv6, string adapter, string primary) =>
        ["interface", ipv6 ? "ipv6" : "ip", "set", ipv6 ? "dnsservers" : "dns", adapter, "static", primary];

    public static IReadOnlyList<string> AddStatic(bool ipv6, string adapter, string server, int index) =>
        ["interface", ipv6 ? "ipv6" : "ip", "add", ipv6 ? "dnsservers" : "dns", adapter, server, "index=" + index];

    public static IReadOnlyList<string> FlushCache() => ["/flushdns"];
}

/// <summary>
/// Real implementation (netsh, ipconfig, registry). NOT exercised by automated tests because it changes
/// the system; argument building is unit tested. Needs a hand test in a VM snapshot.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemDnsConfigurator : IDnsConfigurator
{
    private const string V4Key = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
    private const string V6Key = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";

    private static readonly string NetshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
    private static readonly string IpconfigPath = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");

    private readonly ICommandRunner _runner;

    public SystemDnsConfigurator(ICommandRunner runner)
    {
        _runner = runner;
    }

    public Task<DnsSnapshot?> ReadAsync(AdapterInfo adapter, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adapter.Id) || adapter.Id.Any(c => c is '\\' or '/'))
        {
            return Task.FromResult<DnsSnapshot?>(null);
        }

        var v4 = ReadStatic(V4Key + adapter.Id);
        var v6 = ReadStatic(V6Key + adapter.Id);
        if (v4 is null && v6 is null)
        {
            return Task.FromResult<DnsSnapshot?>(null); // neither interface key exists: cannot back up
        }

        return Task.FromResult<DnsSnapshot?>(new DnsSnapshot(adapter.Name, v4 ?? [], v6 ?? []));
    }

    public async Task<bool> SetAutomaticAsync(string adapterName, CancellationToken ct)
    {
        var v4 = await RunNetshAsync(NetshArguments.SetAutomaticV4(adapterName), ct);

        // Best effort: adapters with IPv6 switched off make this netsh call fail, which is not an error here.
        await RunNetshAsync(NetshArguments.SetAutomaticV6(adapterName), ct);
        return v4;
    }

    public async Task<bool> RestoreAsync(DnsSnapshot snapshot, CancellationToken ct)
    {
        var v4 = await RestoreFamilyAsync(ipv6: false, snapshot.AdapterName, snapshot.V4Servers, ct);
        var v6 = await RestoreFamilyAsync(ipv6: true, snapshot.AdapterName, snapshot.V6Servers, ct);
        return v4 && (v6 || snapshot.V6Servers.Count == 0);
    }

    public async Task<bool> SetStaticAsync(string adapterName, IReadOnlyList<string> v4Servers, IReadOnlyList<string> v6Servers, CancellationToken ct)
    {
        var v4 = await RestoreFamilyAsync(ipv6: false, adapterName, v4Servers, ct);

        // Best effort: an adapter with IPv6 switched off rejects IPv6 DNS, which must not fail the whole change.
        await RestoreFamilyAsync(ipv6: true, adapterName, v6Servers, ct);
        return v4;
    }

    public async Task<bool> FlushCacheAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync(IpconfigPath, NetshArguments.FlushCache(), ct);
        return result.Success;
    }

    private async Task<bool> RestoreFamilyAsync(bool ipv6, string adapter, IReadOnlyList<string> servers, CancellationToken ct)
    {
        if (servers.Count == 0)
        {
            return await RunNetshAsync(ipv6 ? NetshArguments.SetAutomaticV6(adapter) : NetshArguments.SetAutomaticV4(adapter), ct);
        }

        if (!await RunNetshAsync(NetshArguments.SetStatic(ipv6, adapter, servers[0]), ct))
        {
            return false;
        }

        for (var i = 1; i < servers.Count; i++)
        {
            if (!await RunNetshAsync(NetshArguments.AddStatic(ipv6, adapter, servers[i], i + 1), ct))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> RunNetshAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await _runner.RunAsync(NetshPath, arguments, ct);
        return result.Success;
    }

    /// <summary>Null when the key is missing; an empty list when DNS is automatic.</summary>
    private static List<string>? ReadStatic(string subKey)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        if (key is null)
        {
            return null;
        }

        var value = key.GetValue("NameServer") as string ?? "";
        return value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
