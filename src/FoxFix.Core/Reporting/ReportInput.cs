using FoxFix.Core.Diagnostics;
using FoxFix.Core.Dpi;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Reporting;

public sealed record ReportAdapter(
    string Name,
    string Description,
    bool IsUp,
    string? MacAddress,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool? DnsIsStatic = null);

/// <param name="Checks">Null when no diagnosis has been run in this session.</param>
public sealed record ReportInput(
    DateTimeOffset Time,
    string FoxFixVersion,
    string OsDescription,
    string UserName,
    string MachineName,
    IReadOnlyList<ReportAdapter> Adapters,
    IReadOnlyList<CheckReport>? Checks,
    IReadOnlyList<ResidueFinding> Findings,
    IReadOnlyList<UndoRecord> Changes);
