using System.Reflection;
using System.Runtime.InteropServices;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Dpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Reporting;

/// <summary>Facts about this computer and this run, behind an interface so tests do not depend on the machine.</summary>
public interface ISystemInfo
{
    string OsDescription { get; }

    string UserName { get; }

    string MachineName { get; }

    string FoxFixVersion { get; }

    DateTimeOffset Now { get; }
}

public sealed class SystemInfo : ISystemInfo
{
    public string OsDescription => DescribeWindows(Environment.OSVersion.Version) ?? RuntimeInformation.OSDescription;

    /// <summary>Windows 11 still reports itself as version 10.0, so the build number tells them apart (22000+ is 11).</summary>
    public static string? DescribeWindows(Version version)
    {
        if (version.Major != 10)
        {
            return null;
        }

        return $"{(version.Build >= 22000 ? "Windows 11" : "Windows 10")} (sürüm {version})";
    }

    public string UserName => Environment.UserName;

    public string MachineName => Environment.MachineName;

    public string FoxFixVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "?";

    public DateTimeOffset Now => DateTimeOffset.Now;
}

public interface IDiagnosticReportService
{
    /// <param name="lastDiagnosis">The most recent diagnosis of this session, or null if none was run.</param>
    Task<string> CreateAsync(DiagnosticsReport? lastDiagnosis, bool maskPersonalData, CancellationToken ct);
}

/// <summary>Gathers the live facts (adapters, leftovers, FoxFix's own changes) and builds the report text. Read-only.</summary>
public sealed class DiagnosticReportService : IDiagnosticReportService
{
    private readonly INetworkInfoProvider _network;
    private readonly DpiResidueDetector _detector;
    private readonly UndoJournal _journal;
    private readonly ISystemInfo _system;

    public DiagnosticReportService(INetworkInfoProvider network, DpiResidueDetector detector, UndoJournal journal, ISystemInfo system)
    {
        _network = network;
        _detector = detector;
        _journal = journal;
        _system = system;
    }

    /// <param name="lastDiagnosis">The most recent diagnosis of this session, or null if none was run.</param>
    public async Task<string> CreateAsync(DiagnosticsReport? lastDiagnosis, bool maskPersonalData, CancellationToken ct)
    {
        var findings = await Task.Run(_detector.Scan, ct);
        var changes = await _journal.LoadAsync(ct);

        var input = new ReportInput(
            _system.Now,
            _system.FoxFixVersion,
            _system.OsDescription,
            _system.UserName,
            _system.MachineName,
            _network.GetAdapters().Where(a => a.IsPhysical).Select(ToReportAdapter).ToList(),
            lastDiagnosis?.Reports,
            findings,
            changes);

        return DiagnosticReportBuilder.Build(input, maskPersonalData);
    }

    private static ReportAdapter ToReportAdapter(AdapterInfo adapter) => new(
        adapter.Name,
        adapter.Description,
        adapter.IsUp,
        adapter.MacAddress,
        adapter.IPv4Addresses.Select(a => a.ToString()).ToList(),
        adapter.Gateways.Select(a => a.ToString()).ToList(),
        adapter.Dns.Select(a => a.ToString()).ToList(),
        adapter.DnsIsStatic);
}
