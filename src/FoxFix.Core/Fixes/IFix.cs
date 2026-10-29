using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>HasChanges is false when there is nothing to do; the UI then offers no Apply button.</summary>
public sealed record FixPreview(IReadOnlyList<string> Changes, bool RequiresReboot = false, bool HasChanges = true);

public sealed record FixResult(bool Success, string Summary, bool RequiresReboot = false);

/// <param name="Fraction">How far this fix is, 0 to 1.</param>
/// <param name="Text">Optional detail shown next to the bar, for example "1,2 GB / 6,2 GB".</param>
public sealed record FixProgress(double Fraction, string? Text = null);

/// <summary>
/// Changes something. Must be idempotent, show a preview first and write
/// everything needed to revert into the <see cref="UndoJournal"/> before changing it.
/// </summary>
public interface IFix
{
    string Id { get; }

    /// <summary>Turkish, plain-language title shown to the user.</summary>
    string Title { get; }

    /// <summary>
    /// False for optional tweaks (e.g. ad blocking DNS) that are a choice, not a repair:
    /// "Beni Düzelt" skips them and they get their own button.
    /// </summary>
    bool IncludedInRepair => true;

    /// <summary>False when the current selection can be done without administrator rights (e.g. only per-user startup entries).</summary>
    bool RequiresAdmin => true;

    Task<FixPreview> PreviewAsync(CancellationToken ct);

    Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct);

    /// <summary>The same, reporting how far it is. Fixes that cannot tell just run without reporting.</summary>
    Task<FixResult> ApplyAsync(UndoJournal journal, IProgress<FixProgress>? progress, CancellationToken ct) => ApplyAsync(journal, ct);

    Task UndoAsync(UndoRecord record, CancellationToken ct);
}
