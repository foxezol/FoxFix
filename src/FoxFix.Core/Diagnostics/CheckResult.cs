namespace FoxFix.Core.Diagnostics;

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
    Skipped,

    /// <summary>The check itself broke; says nothing about the user's network.</summary>
    Error,
}

public sealed record CheckResult(CheckStatus Status, string Summary, string? Detail = null);

public sealed record CheckReport(string CheckId, string Title, CheckResult Result);
