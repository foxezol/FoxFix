namespace FoxFix.Core.Dpi;

public enum ResidueKind
{
    Service,
    Driver,
    Process,
    Folder,
    DnsLoopback,

    /// <summary>GoodbyeDPI rewrites DNS packets to another server (--dns-addr). Not a Windows DNS setting.</summary>
    DnsRedirect,
    Conflict,
}

/// <summary>Confirmed: the executable/driver file name matched. Possible: only the service name matched.</summary>
public enum ResidueConfidence
{
    Confirmed,
    Possible,
}

/// <summary>Removable findings may later be cleaned by a fix; ReportOnly ones are never touched by FoxFix.</summary>
public enum ResidueHandling
{
    Removable,
    ReportOnly,
}

public sealed record ResidueFinding(
    string RuleId,
    ResidueKind Kind,
    string Name,
    string? Path,
    bool IsActive,
    ResidueConfidence Confidence,
    ResidueHandling Handling,
    string Message);
