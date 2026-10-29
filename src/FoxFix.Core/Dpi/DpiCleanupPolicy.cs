namespace FoxFix.Core.Dpi;

public enum SkipReason
{
    /// <summary>Matched by service name only; could belong to another program.</summary>
    NotSure,

    /// <summary>Lives under SplitWire-Turkey; its own uninstaller must remove it (plan section 16.3).</summary>
    OwnedBySplitWire,

    /// <summary>Recognized, but not something this fix removes (dnscrypt, ByeDPI, ProxiFyre).</summary>
    OutOfScope,

    UnsafeName,
}

public sealed record SkippedItem(ResidueFinding Finding, SkipReason Reason);

public sealed record CleanupPlan(
    IReadOnlyList<ResidueFinding> Services,
    IReadOnlyList<ResidueFinding> Processes,
    IReadOnlyList<ResidueFinding> Drivers,
    IReadOnlyList<SkippedItem> Skipped)
{
    public bool IsEmpty => Services.Count == 0 && Processes.Count == 0 && Drivers.Count == 0;

    public int Count => Services.Count + Processes.Count + Drivers.Count;
}

/// <summary>
/// The second lock on the door: whatever the detector reports, only this exact allowlist is ever removed.
/// </summary>
public static class DpiCleanupPolicy
{
    private static readonly HashSet<string> CleanableRules =
        [DpiSignatures.GoodbyeDpi, DpiSignatures.WinDivert, DpiSignatures.Zapret];

    public static CleanupPlan BuildPlan(IReadOnlyList<ResidueFinding> findings)
    {
        var services = new List<ResidueFinding>();
        var processes = new List<ResidueFinding>();
        var drivers = new List<ResidueFinding>();
        var skipped = new List<SkippedItem>();

        foreach (var finding in findings)
        {
            if (finding.Kind is not (ResidueKind.Service or ResidueKind.Driver or ResidueKind.Process)
                || finding.Handling != ResidueHandling.Removable)
            {
                continue;
            }

            var skip = ShouldSkip(finding);
            if (skip is { } reason)
            {
                skipped.Add(new SkippedItem(finding, reason));
                continue;
            }

            switch (finding.Kind)
            {
                case ResidueKind.Service:
                    services.Add(finding);
                    break;
                case ResidueKind.Driver:
                    drivers.Add(finding);
                    break;
                default:
                    processes.Add(finding);
                    break;
            }
        }

        return new CleanupPlan(services, processes, drivers, skipped);
    }

    /// <summary>Windows service names cannot contain slashes; quotes and control characters are never legitimate.</summary>
    public static bool IsSafeServiceName(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.Length <= 256
            && name.All(c => !char.IsControl(c) && c is not ('\\' or '/' or '"'));
    }

    private static SkipReason? ShouldSkip(ResidueFinding finding)
    {
        if (!CleanableRules.Contains(finding.RuleId))
        {
            return SkipReason.OutOfScope;
        }

        if (finding.Kind != ResidueKind.Process && !IsSafeServiceName(finding.Name))
        {
            return SkipReason.UnsafeName;
        }

        if (IsUnderSplitWire(finding.Path))
        {
            return SkipReason.OwnedBySplitWire;
        }

        return finding.Confidence == ResidueConfidence.Confirmed ? null : SkipReason.NotSure;
    }

    private static bool IsUnderSplitWire(string? path)
    {
        return path is not null
            && path.Split('\\', '/').Any(segment => segment.Equals("SplitWire-Turkey", StringComparison.OrdinalIgnoreCase));
    }
}
