using FoxFix.Core;
using FoxFix.Core.Diagnostics;

namespace FoxFix.App;

/// <summary>One check whose answer is not the same before and after a repair.</summary>
public sealed record ComparisonLine(string Title, CheckStatus Before, CheckStatus After)
{
    /// <summary>True when the check is better now (it was a problem, it is not any more).</summary>
    public bool Improved => Rank(After) < Rank(Before);

    private static int Rank(CheckStatus status) => status switch
    {
        CheckStatus.Passed => 0,
        CheckStatus.Skipped => 1,
        CheckStatus.Warning => 2,
        CheckStatus.Error => 2,
        _ => 3,
    };
}

/// <summary>"Önce / sonra": the connection check before a repair next to the one after it, for the checks that changed.</summary>
public static class RepairComparison
{
    public static IReadOnlyList<ComparisonLine> Changed(DiagnosticsReport before, DiagnosticsReport after)
    {
        var lines = new List<ComparisonLine>();
        foreach (var now in after.Reports)
        {
            var was = before.Reports.FirstOrDefault(r => r.CheckId == now.CheckId);
            if (was is not null && was.Result.Status != now.Result.Status)
            {
                lines.Add(new ComparisonLine(now.Title, was.Result.Status, now.Result.Status));
            }
        }

        return lines;
    }

    public static string Label(CheckStatus status) => Strings.Get(status switch
    {
        CheckStatus.Passed => Strings.StatusOkLabel,
        CheckStatus.Failed => Strings.StatusBadLabel,
        CheckStatus.Skipped => Strings.StatusSkippedLabel,
        _ => Strings.StatusWarnLabel,
    });

    /// <summary>The words shown on the overview: each change on its own line, or that nothing changed.</summary>
    public static string Text(IReadOnlyList<ComparisonLine> lines) =>
        lines.Count == 0
            ? Strings.Get(Strings.CompareSame)
            : string.Join(
                "\n",
                lines.Select(l => Strings.Format(Strings.CompareLine, l.Title, Label(l.Before), Label(l.After))));
}
