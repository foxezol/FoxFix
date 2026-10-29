using FoxFix.Core.Network;

namespace FoxFix.Core.Dpi;

/// <summary>
/// Finds GoodbyeDPI, Zapret, WinDivert, SplitWire and DnsCrypt leftovers. Strictly read-only:
/// it only talks to the read-only source interfaces and never changes anything.
/// </summary>
public sealed class DpiResidueDetector
{
    private readonly IServiceSource _services;
    private readonly IProcessSource _processes;
    private readonly IPathProbe _paths;
    private readonly INetworkInfoProvider _network;
    private readonly IReadOnlyList<string> _splitWireFolders;
    private readonly string? _managedFolder;

    /// <param name="managedFolder">FoxFix's own folder. Anything that runs from inside it is FoxFix's GoodbyeDPI and is not a leftover.</param>
    public DpiResidueDetector(
        IServiceSource services,
        IProcessSource processes,
        IPathProbe paths,
        INetworkInfoProvider network,
        IReadOnlyList<string>? splitWireFolders = null,
        string? managedFolder = null)
    {
        _managedFolder = managedFolder;
        _services = services;
        _processes = processes;
        _paths = paths;
        _network = network;
        _splitWireFolders = splitWireFolders ?? DpiSignatures.DefaultSplitWireFolders();
    }

    public IReadOnlyList<ResidueFinding> Scan()
    {
        var serviceEntries = _services.GetServicesAndDrivers();

        // While FoxFix's own GoodbyeDPI runs it may be using a WinDivert driver that an older install registered under
        // another path. That driver is in use by FoxFix, not left behind.
        var ownRunning = IsOwnGoodbyeDpiRunning(serviceEntries);
        var visible = serviceEntries.Where(e => !IsManaged(e.PathName) && !(ownRunning && e.IsDriver && IsWinDivertDriver(e))).ToList();

        var findings = new List<ResidueFinding>();
        findings.AddRange(ScanServices(visible));
        findings.AddRange(ScanProcesses());
        findings.AddRange(ScanSplitWire());
        findings.AddRange(ScanDns(serviceEntries));
        findings.AddRange(ScanConflict(findings));
        return findings;
    }

    private bool IsOwnGoodbyeDpiRunning(IReadOnlyList<ServiceEntry> entries) =>
        _managedFolder is not null
        && (entries.Any(e => !e.IsDriver && e.IsRunning && IsManaged(e.PathName))
            || _processes.GetProcesses().Any(p => DpiSignatures.ProcessNames.ContainsKey(p.Name) && IsManaged(p.Path)));

    private static bool IsWinDivertDriver(ServiceEntry entry)
    {
        var fileName = ServicePathParser.GetFileName(entry.PathName);
        return (fileName is not null && DpiSignatures.FileNames.TryGetValue(fileName, out var byFile) && byFile == DpiSignatures.WinDivert)
            || (DpiSignatures.ServiceNames.TryGetValue(entry.Name, out var byName) && byName == DpiSignatures.WinDivert);
    }

    private bool IsManaged(string? pathOrCommandLine) =>
        _managedFolder is not null && Core.GoodbyeDpi.GoodbyeDpiLayout.IsManaged(pathOrCommandLine, _managedFolder);

    private static IEnumerable<ResidueFinding> ScanServices(IReadOnlyList<ServiceEntry> entries)
    {
        foreach (var entry in entries)
        {
            var fileName = ServicePathParser.GetFileName(entry.PathName);

            string? tool;
            ResidueConfidence confidence;
            if (fileName is not null && DpiSignatures.FileNames.TryGetValue(fileName, out tool))
            {
                confidence = ResidueConfidence.Confirmed;
            }
            else if (DpiSignatures.ServiceNames.TryGetValue(entry.Name, out tool))
            {
                confidence = ResidueConfidence.Possible;
            }
            else
            {
                continue;
            }

            var kind = entry.IsDriver ? ResidueKind.Driver : ResidueKind.Service;
            yield return new ResidueFinding(
                tool, kind, entry.Name, ServicePathParser.GetPath(entry.PathName), entry.IsRunning,
                confidence, ResidueHandling.Removable, Describe(tool, entry.Name, entry.IsRunning, confidence));

            if (tool == DpiSignatures.GoodbyeDpi && !entry.IsDriver
                && GoodbyeDpiArguments.TryGetDnsRedirect(entry.PathName, out var redirect))
            {
                yield return new ResidueFinding(
                    "goodbyedpi-dns-redirect", ResidueKind.DnsRedirect, entry.Name, null, entry.IsRunning,
                    confidence, ResidueHandling.ReportOnly,
                    Strings.Format(Strings.FindingDnsRedirect, redirect.Address, redirect.Port));
            }
        }
    }

    private IEnumerable<ResidueFinding> ScanProcesses()
    {
        foreach (var process in _processes.GetProcesses())
        {
            if (!DpiSignatures.ProcessNames.TryGetValue(process.Name, out var tool) || IsManaged(process.Path))
            {
                continue;
            }

            yield return new ResidueFinding(
                tool, ResidueKind.Process, process.Name, process.Path, true,
                ResidueConfidence.Confirmed, ResidueHandling.Removable,
                Describe(tool, process.Name, true, ResidueConfidence.Confirmed));
        }
    }

    private IEnumerable<ResidueFinding> ScanSplitWire()
    {
        var folder = _splitWireFolders.FirstOrDefault(_paths.DirectoryExists);
        if (folder is null)
        {
            yield break;
        }

        yield return new ResidueFinding(
            "splitwire", ResidueKind.Folder, "SplitWire-Turkey", folder, false,
            ResidueConfidence.Confirmed, ResidueHandling.ReportOnly,
            Strings.Format(Strings.FindingSplitWire, folder));
    }

    private IEnumerable<ResidueFinding> ScanDns(IReadOnlyList<ServiceEntry> serviceEntries)
    {
        var loopbackAdapters = _network.GetAdapters()
            .Where(a => a.IsPhysical && a.IsUp && a.Dns.Any(System.Net.IPAddress.IsLoopback))
            .Select(a => a.Name)
            .ToList();

        if (loopbackAdapters.Count == 0)
        {
            yield break;
        }

        var names = string.Join(", ", loopbackAdapters);
        var proxyRunning = IsDnsCryptRunning(serviceEntries);

        yield return proxyRunning
            ? new ResidueFinding(
                "dns-loopback-proxy", ResidueKind.DnsLoopback, names, null, true,
                ResidueConfidence.Confirmed, ResidueHandling.ReportOnly,
                Strings.Format(Strings.FindingDnsProxy, names))
            : new ResidueFinding(
                "dns-loopback-orphan", ResidueKind.DnsLoopback, names, null, true,
                ResidueConfidence.Confirmed, ResidueHandling.Removable,
                Strings.Format(Strings.FindingDnsOrphan, names));
    }

    private static IEnumerable<ResidueFinding> ScanConflict(IReadOnlyList<ResidueFinding> findings)
    {
        var goodbyeActive = findings.Any(f => f.RuleId == DpiSignatures.GoodbyeDpi && f.IsActive);
        var zapretActive = findings.Any(f => f.RuleId == DpiSignatures.Zapret && f.IsActive);

        if (goodbyeActive && zapretActive)
        {
            yield return new ResidueFinding(
                "dpi-conflict", ResidueKind.Conflict, "GoodbyeDPI + Zapret", null, true,
                ResidueConfidence.Confirmed, ResidueHandling.ReportOnly,
                Strings.Get(Strings.FindingConflict));
        }
    }

    /// <summary>True when a DnsCrypt-Proxy service is running, so a loopback DNS may be a deliberate setup.</summary>
    public static bool IsDnsCryptRunning(IReadOnlyList<ServiceEntry> entries) =>
        entries.Any(s => !s.IsDriver && s.IsRunning && IsDnsCrypt(s));

    private static bool IsDnsCrypt(ServiceEntry entry)
    {
        var fileName = ServicePathParser.GetFileName(entry.PathName);
        return string.Equals(entry.Name, DpiSignatures.DnsCryptServiceName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "dnscrypt-proxy.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(string tool, string name, bool active, ResidueConfidence confidence)
    {
        var message = Strings.Format(active ? Strings.FindingActive : Strings.FindingLeftover, ToolName(tool), name);
        return confidence == ResidueConfidence.Possible ? message + Strings.Get(Strings.FindingPossibleSuffix) : message;
    }

    private static string ToolName(string tool)
    {
        var key = tool switch
        {
            DpiSignatures.GoodbyeDpi => Strings.ToolGoodbyeDpi,
            DpiSignatures.WinDivert => Strings.ToolWinDivert,
            DpiSignatures.Zapret => Strings.ToolZapret,
            DpiSignatures.ByeDpi => Strings.ToolByeDpi,
            DpiSignatures.ProxiFyre => Strings.ToolProxiFyre,
            DpiSignatures.DnsCrypt => Strings.ToolDnsCrypt,
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Bilinmeyen araç etiketi."),
        };

        return Strings.Get(key);
    }
}
