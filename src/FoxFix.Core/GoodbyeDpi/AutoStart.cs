using System.Security;
using System.Text;
using FoxFix.Core.Commands;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.GoodbyeDpi;

public sealed record AutoStartResult(bool Success, string Error);

/// <summary>
/// Starts FoxDPI when the user signs in to Windows, without a service: a scheduled task that runs
/// <c>FoxFix.exe --sadece-foxdpi</c> with the highest rights. Windows skips elevated programs listed under "Run", so a task
/// is the way to do it without a UAC question at every sign-in.
/// </summary>
public sealed class FoxDpiAutoStart
{
    public const string TaskName = @"FoxFix\FoxDPI";
    public const string Arguments = "--sadece-foxdpi";

    private static readonly string SchTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    private readonly ICommandRunner _runner;

    public FoxDpiAutoStart(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<bool> ExistsAsync(CancellationToken ct) =>
        (await _runner.RunAsync(SchTasks, ["/Query", "/TN", TaskName], ct)).Success;

    public async Task<AutoStartResult> EnableAsync(string exePath, string user, CancellationToken ct)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), "foxfix-foxdpi-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            // Task Scheduler reads this file as UTF-16.
            await File.WriteAllTextAsync(xmlPath, BuildTaskXml(exePath, user), Encoding.Unicode, ct);
            var result = await _runner.RunAsync(SchTasks, ["/Create", "/TN", TaskName, "/XML", xmlPath, "/F"], ct);
            return result.Success ? new AutoStartResult(true, "") : new AutoStartResult(false, FirstLine(result));
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stray temp file is harmless.
            }
        }
    }

    public async Task<AutoStartResult> DisableAsync(CancellationToken ct)
    {
        if (!await ExistsAsync(ct))
        {
            return new AutoStartResult(true, "");
        }

        var result = await _runner.RunAsync(SchTasks, ["/Delete", "/TN", TaskName, "/F"], ct);
        return result.Success ? new AutoStartResult(true, "") : new AutoStartResult(false, FirstLine(result));
    }

    /// <summary>The task: at this user's sign-in (after a short wait for the network), highest rights, no time limit, on battery too.</summary>
    public static string BuildTaskXml(string exePath, string user)
    {
        var exe = SecurityElement.Escape(exePath);
        var account = SecurityElement.Escape(user);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>FoxDPI'yı Windows açılınca başlatır. FoxFix'te kapatılabilir.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{account}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{account}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>{Arguments}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string FirstLine(CommandResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? $"çıkış kodu {result.ExitCode}";
    }
}

/// <summary>Turns "FoxDPI starts with Windows" on or off, with the usual preview, journal and undo.</summary>
public sealed class FoxDpiAutoStartFix : IFix
{
    public const string FixIdValue = "foxdpi-autostart";

    private readonly FoxDpiAutoStart _autoStart;
    private readonly Func<string?> _exePath;
    private readonly Func<string> _user;

    private bool _enable = true;

    public FoxDpiAutoStartFix(FoxDpiAutoStart autoStart, Func<string?> exePath, Func<string> user)
    {
        _autoStart = autoStart;
        _exePath = exePath;
        _user = user;
    }

    public string Id => FixIdValue;

    public string Title => Strings.Get(Strings.AutoStartTitle);

    public bool IncludedInRepair => false;

    /// <summary>True to start FoxDPI with Windows, false to stop doing so.</summary>
    public void Configure(bool enable) => _enable = enable;

    public async Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var exists = await _autoStart.ExistsAsync(ct);

        if (_enable)
        {
            if (exists)
            {
                return new FixPreview([Strings.Get(Strings.AutoStartAlreadyOn)], HasChanges: false);
            }

            if (_exePath() is not { } exe)
            {
                return new FixPreview([Strings.Get(Strings.AutoStartNoPath)], HasChanges: false);
            }

            return new FixPreview(
            [
                Strings.Format(Strings.AutoStartPreviewOn, FoxDpiAutoStart.TaskName),
                Strings.Format(Strings.AutoStartPreviewPath, exe),
            ]);
        }

        return exists
            ? new FixPreview([Strings.Format(Strings.AutoStartPreviewOff, FoxDpiAutoStart.TaskName)])
            : new FixPreview([Strings.Get(Strings.AutoStartAlreadyOff)], HasChanges: false);
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var was = await _autoStart.ExistsAsync(ct);
        var exe = _exePath() ?? "";

        if (_enable && (was || exe.Length == 0))
        {
            return new FixResult(was, Strings.Get(was ? Strings.AutoStartAlreadyOn : Strings.AutoStartNoPath));
        }

        if (!_enable && !was)
        {
            return new FixResult(true, Strings.Get(Strings.AutoStartAlreadyOff));
        }

        // Journal first: what it was and where FoxFix was, so undo can put it back.
        var record = new UndoRecord(
            Guid.NewGuid(), FixIdValue, DateTimeOffset.UtcNow,
            Strings.Get(_enable ? Strings.AutoStartUndoOn : Strings.AutoStartUndoOff),
            new Dictionary<string, string>
            {
                ["was"] = was.ToString(),
                ["exe"] = exe,
                ["user"] = _user(),
            });
        await journal.AppendAsync(record, ct);

        var result = _enable
            ? await _autoStart.EnableAsync(exe, _user(), ct)
            : await _autoStart.DisableAsync(ct);

        if (!result.Success)
        {
            await journal.RemoveAsync(record.Id, ct);
            return new FixResult(false, Strings.Format(Strings.AutoStartFailed, result.Error));
        }

        return new FixResult(true, Strings.Get(_enable ? Strings.AutoStartResultOn : Strings.AutoStartResultOff));
    }

    public async Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        var was = record.Data.TryGetValue("was", out var value) && value == bool.TrueString;
        var exe = record.Data.GetValueOrDefault("exe", "");
        var user = record.Data.GetValueOrDefault("user", _user());

        var result = was && exe.Length > 0
            ? await _autoStart.EnableAsync(exe, user, ct)
            : await _autoStart.DisableAsync(ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(Strings.Format(Strings.AutoStartFailed, result.Error));
        }
    }
}
