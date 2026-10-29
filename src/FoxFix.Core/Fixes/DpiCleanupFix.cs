using System.Globalization;
using FoxFix.Core.Dpi;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Fixes;

/// <summary>
/// Removes GoodbyeDPI / WinDivert / Zapret services, drivers and processes.
/// Order: services, then processes (so a service cannot respawn them), then drivers last.
/// Every service is written to the <see cref="UndoJournal"/> BEFORE it is touched.
/// </summary>
public sealed class DpiCleanupFix : IFix
{
    public const string FixId = "dpi-cleanup";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly DpiResidueDetector _detector;
    private readonly IServiceSource _serviceSource;
    private readonly IServiceManager _services;
    private readonly IProcessKiller _processes;
    private readonly TimeSpan _stopWait;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DpiCleanupFix(
        DpiResidueDetector detector,
        IServiceSource serviceSource,
        IServiceManager services,
        IProcessKiller processes,
        TimeSpan? stopWait = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _detector = detector;
        _serviceSource = serviceSource;
        _services = services;
        _processes = processes;
        _stopWait = stopWait ?? TimeSpan.FromSeconds(10);
        _delay = delay ?? Task.Delay;
    }

    public string Id => FixId;

    public string Title => Strings.Get(Strings.CleanupTitle);

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        var plan = DpiCleanupPolicy.BuildPlan(_detector.Scan());
        var lines = new List<string>();

        foreach (var service in plan.Services.Concat(plan.Drivers))
        {
            lines.Add(Strings.Format(Strings.PreviewRemoveService, service.Name, service.Path ?? "-"));
        }

        foreach (var name in DistinctProcessNames(plan))
        {
            lines.Add(Strings.Format(Strings.PreviewKillProcess, name));
        }

        foreach (var item in plan.Skipped)
        {
            lines.Add(Strings.Format(SkipKey(item.Reason), item.Finding.Name));
        }

        var needsReboot = plan.Drivers.Any(d => d.IsActive);
        if (plan.IsEmpty)
        {
            lines.Insert(0, Strings.Get(Strings.PreviewNothing));
        }
        else
        {
            lines.Add(Strings.Get(Strings.PreviewUndoNote));
            if (needsReboot)
            {
                lines.Add(Strings.Get(Strings.PreviewReboot));
            }
        }

        return Task.FromResult(new FixPreview(lines, needsReboot, !plan.IsEmpty));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        var plan = DpiCleanupPolicy.BuildPlan(_detector.Scan());
        if (plan.IsEmpty)
        {
            return new FixResult(true, Strings.Get(Strings.ResultNothing));
        }

        var state = new RunState();

        foreach (var service in plan.Services)
        {
            await RemoveServiceAsync(service, journal, state, ct);
        }

        foreach (var name in DistinctProcessNames(plan))
        {
            await KillProcessAsync(name, state, ct);
        }

        foreach (var driver in plan.Drivers)
        {
            await RemoveServiceAsync(driver, journal, state, ct);
        }

        return Summarize(state);
    }

    public async Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        var config = ReadConfig(record);
        var wasRunning = Field(record, "wasRunning") == bool.TrueString;

        if (!await _services.CreateAsync(config, ct))
        {
            throw new InvalidOperationException(Strings.Format(Strings.UndoCreateFailed, config.Name));
        }

        if (wasRunning && !await _services.StartAsync(config.Name, ct))
        {
            throw new InvalidOperationException(Strings.Format(Strings.UndoStartFailed, config.Name));
        }
    }

    private async Task RemoveServiceAsync(ResidueFinding finding, UndoJournal journal, RunState state, CancellationToken ct)
    {
        var config = await _services.ReadConfigAsync(finding.Name, ct);
        if (config is null)
        {
            return; // already gone, nothing to back up or remove
        }

        var record = new UndoRecord(
            Guid.NewGuid(), FixId, DateTimeOffset.UtcNow,
            Strings.Format(Strings.UndoDescription, finding.Name),
            ToData(config, finding.IsActive));

        // Journal first: if anything below crashes, the way back already exists.
        await journal.AppendAsync(record, ct);

        var stop = await _services.StopAsync(finding.Name, ct);
        if (stop == StopOutcome.Failed)
        {
            await journal.RemoveAsync(record.Id, ct);
            state.Failures.Add(Strings.Format(Strings.FailStop, finding.Name));
            return;
        }

        if (stop == StopOutcome.Stopped && !await WaitUntilStoppedAsync(finding.Name, ct))
        {
            state.NeedsReboot = true; // stuck in STOP_PENDING; only a full restart unloads it
        }

        switch (await _services.DeleteAsync(finding.Name, ct))
        {
            case DeleteOutcome.Deleted or DeleteOutcome.NotFound:
                state.Removed++;
                break;
            case DeleteOutcome.MarkedForDeletion:
                state.Removed++;
                state.NeedsReboot = true;
                break;
            default:
                await journal.RemoveAsync(record.Id, ct);
                state.Failures.Add(Strings.Format(Strings.FailDelete, finding.Name));
                break;
        }
    }

    private async Task KillProcessAsync(string name, RunState state, CancellationToken ct)
    {
        try
        {
            await _processes.KillAsync(name, ct);
            state.Removed++;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Failures.Add(Strings.Format(Strings.FailKill, name) + " " + ex.Message);
        }
    }

    private async Task<bool> WaitUntilStoppedAsync(string name, CancellationToken ct)
    {
        var waited = TimeSpan.Zero;

        while (true)
        {
            var running = _serviceSource.GetServicesAndDrivers()
                .Any(s => s.IsRunning && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!running)
            {
                return true;
            }

            if (waited >= _stopWait)
            {
                return false;
            }

            await _delay(PollInterval, ct);
            waited += PollInterval;
        }
    }

    private static FixResult Summarize(RunState state)
    {
        var summary = state.Failures.Count == 0
            ? Strings.Format(Strings.ResultDone, state.Removed)
            : Strings.Format(Strings.ResultPartial, state.Removed, state.Failures.Count)
              + Environment.NewLine + string.Join(Environment.NewLine, state.Failures);

        if (state.NeedsReboot)
        {
            summary += Strings.Get(Strings.ResultReboot);
        }

        return new FixResult(state.Failures.Count == 0, summary, state.NeedsReboot);
    }

    private static IEnumerable<string> DistinctProcessNames(CleanupPlan plan) =>
        plan.Processes.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string SkipKey(SkipReason reason) => reason switch
    {
        SkipReason.NotSure => Strings.PreviewSkipNotSure,
        SkipReason.OwnedBySplitWire => Strings.PreviewSkipSplitWire,
        SkipReason.UnsafeName => Strings.PreviewSkipUnsafe,
        _ => Strings.PreviewSkipOutOfScope,
    };

    private static Dictionary<string, string> ToData(ServiceConfig config, bool wasRunning) => new()
    {
        ["name"] = config.Name,
        ["displayName"] = config.DisplayName,
        ["binaryPath"] = config.BinaryPath,
        ["startType"] = config.StartType.ToString(CultureInfo.InvariantCulture),
        ["serviceType"] = config.ServiceType.ToString(CultureInfo.InvariantCulture),
        ["isDriver"] = config.IsDriver.ToString(),
        ["wasRunning"] = wasRunning.ToString(),
    };

    private static ServiceConfig ReadConfig(UndoRecord record)
    {
        return new ServiceConfig(
            Field(record, "name"),
            Field(record, "displayName"),
            Field(record, "binaryPath"),
            int.Parse(Field(record, "startType"), CultureInfo.InvariantCulture),
            int.Parse(Field(record, "serviceType"), CultureInfo.InvariantCulture),
            Field(record, "isDriver") == bool.TrueString);
    }

    private static string Field(UndoRecord record, string key)
    {
        return record.Data.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, key));
    }

    private sealed class RunState
    {
        public int Removed { get; set; }

        public bool NeedsReboot { get; set; }

        public List<string> Failures { get; } = [];
    }
}
