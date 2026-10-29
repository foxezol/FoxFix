namespace FoxFix.Core.Diagnostics;

/// <summary>Inspects one thing and never changes the system.</summary>
public interface ICheck
{
    string Id { get; }

    /// <summary>Turkish, plain-language title shown to the user.</summary>
    string Title { get; }

    /// <summary>
    /// True for checks that explain an earlier failure (e.g. DPI leftovers) and so must
    /// still run after a layer failed. Ordinary layers are skipped once something below them broke.
    /// </summary>
    bool RunsAfterFailure => false;

    Task<CheckResult> RunAsync(CancellationToken ct);
}
