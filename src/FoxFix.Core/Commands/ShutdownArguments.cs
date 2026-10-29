namespace FoxFix.Core.Commands;

/// <summary>
/// shutdown.exe arguments. Restart is deliberately NOT forced (no /f): apps with unsaved work
/// get to ask the user, and the countdown can be aborted with /a.
/// </summary>
public static class ShutdownArguments
{
    public const int MaxDelaySeconds = 600;

    public static IReadOnlyList<string> Restart(int delaySeconds, string comment)
    {
        if (delaySeconds is < 0 or > MaxDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds, $"0 ile {MaxDelaySeconds} arasında olmalı.");
        }

        return ["/r", "/t", delaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "/c", comment];
    }

    public static IReadOnlyList<string> Abort() => ["/a"];
}
