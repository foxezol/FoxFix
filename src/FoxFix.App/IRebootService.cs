using System.IO;
using FoxFix.Core;
using FoxFix.Core.Commands;

namespace FoxFix.App;

public interface IRebootService
{
    /// <summary>Starts a restart countdown. Windows shows its own notice and the user can still abort it.</summary>
    Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct);

    Task<bool> CancelAsync(CancellationToken ct);
}

/// <summary>Real implementation via shutdown.exe. Needs a hand test: it would restart this computer.</summary>
public sealed class WindowsRebootService : IRebootService
{
    private static readonly string ShutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

    private readonly ICommandRunner _runner;

    public WindowsRebootService(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct)
    {
        var result = await _runner.RunAsync(ShutdownPath, ShutdownArguments.Restart(delaySeconds, Strings.Get(Strings.RebootComment)), ct);
        return result.Success;
    }

    public async Task<bool> CancelAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync(ShutdownPath, ShutdownArguments.Abort(), ct);
        return result.Success;
    }
}
