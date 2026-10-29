using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.App;

public enum FlowStage
{
    Idle,
    Previewing,
    AwaitingConfirmation,
    Applying,
    Finished,
}

public sealed record UndoItem(UndoRecord Record, string Text);

/// <summary>One preview row; headers name the fix when several fixes have changes.</summary>
public sealed record PreviewLine(string Text, bool IsHeader = false)
{
    public bool IsBullet => !IsHeader;
}

/// <summary>
/// "Beni Düzelt": preview, explicit confirmation, apply, undo. Nothing changes the system
/// before the user ticks the confirmation box and presses Apply.
/// </summary>
public sealed partial class FixFlowViewModel : ObservableObject
{
    private readonly IReadOnlyList<IFix> _fixes;
    private readonly UndoJournal _journal;
    private readonly IElevation _elevation;
    private readonly IRebootService _reboot;

    /// <summary>The fixes the current preview/apply run works on (repair set, or one optional fix).</summary>
    private IReadOnlyList<IFix> _activeFixes = [];

    public const int AutoRestartDelaySeconds = 20;
    public const int RestartNowDelaySeconds = 10;

    public FixFlowViewModel(IReadOnlyList<IFix> fixes, UndoJournal journal, IElevation elevation, IRebootService reboot)
    {
        _fixes = fixes;
        _journal = journal;
        _elevation = elevation;
        _reboot = reboot;
    }

    public ObservableCollection<PreviewLine> PreviewLines { get; } = [];

    public ObservableCollection<UndoItem> UndoItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsPanelVisible), nameof(IsBusy), nameof(ShowConfirmation), nameof(CanApply),
        nameof(ShowAutoRestartOption), nameof(ShowRebootBanner))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StartOptionalCommand), nameof(StartSingleCommand), nameof(ApplyCommand))]
    private FlowStage _stage = FlowStage.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(ShowConfirmation), nameof(ShowAutoRestartOption))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _hasChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage), nameof(ShowPanelMessage))]
    private string _message = "";

    /// <summary>0 to 100. Only meaningful while HasProgress is true; otherwise the bar just keeps moving.</summary>
    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private bool _hasProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressText))]
    private string _progressText = "";

    public bool IsIndeterminate => !HasProgress;

    public bool HasProgressText => ProgressText.Length > 0;

    /// <summary>What the preview expects: a restart will probably be needed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoRestartOption))]
    private bool _requiresReboot;

    /// <summary>The "restart by itself" tick, chosen before applying. Reset after every apply.</summary>
    [ObservableProperty]
    private bool _autoRestart;

    /// <summary>What the finished cleanup reported: the restart is now actually required.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRebootBanner))]
    private bool _rebootNeeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRebootBanner), nameof(ShowPanelMessage))]
    private bool _rebootScheduled;

    /// <summary>While a restart is scheduled its own notice shows the message, so the panel stays quiet.</summary>
    public bool ShowPanelMessage => HasMessage && !RebootScheduled;

    public bool ShowAutoRestartOption => ShowConfirmation && RequiresReboot;

    public bool ShowRebootBanner => Stage == FlowStage.Finished && RebootNeeded && !RebootScheduled;

    public bool IsPanelVisible => Stage != FlowStage.Idle;

    public bool IsBusy => Stage is FlowStage.Previewing or FlowStage.Applying;

    public bool ShowConfirmation => Stage == FlowStage.AwaitingConfirmation && HasChanges;

    public bool CanApply => ShowConfirmation && IsConfirmed;

    public bool HasMessage => Message.Length > 0;

    public bool HasUndoItems => UndoItems.Count > 0;

    /// <summary>Administrator rights are only asked for when the fixes in this run actually need them.</summary>
    public bool NeedsElevation => !_elevation.IsElevated && _activeFixes.Any(f => f.RequiresAdmin);

    public string ApplyLabel => Strings.Get(NeedsElevation ? Strings.FlowApplyElevatedLabel : Strings.FlowApplyLabel);

    public string AdminNote => NeedsElevation ? Strings.Get(Strings.FlowNeedsAdmin) : "";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunPreviewAsync(_fixes.Where(f => f.IncludedInRepair).ToList());

    /// <summary>The same preview, confirm and apply flow for one optional fix (e.g. "startup").</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartOptionalAsync(string? fixId)
    {
        var fix = _fixes.FirstOrDefault(f => !f.IncludedInRepair && f.Id == fixId);
        return fix is null ? Task.CompletedTask : RunPreviewAsync([fix]);
    }

    /// <summary>Previews and applies one specific fix by id, repair fix or optional one (used by the buttons on the DNS and DPI page).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartSingleAsync(string? fixId)
    {
        var fix = _fixes.FirstOrDefault(f => f.Id == fixId);
        return fix is null ? Task.CompletedTask : RunPreviewAsync([fix]);
    }

    /// <summary>One preview for several fixes at once, in the order they are registered (used by the "Beni Düzelt" picker).</summary>
    public Task StartManyAsync(IEnumerable<string> fixIds)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        var wanted = fixIds.ToHashSet();
        var chosen = _fixes.Where(f => wanted.Contains(f.Id)).ToList();
        return chosen.Count == 0 ? Task.CompletedTask : RunPreviewAsync(chosen);
    }

    private async Task RunPreviewAsync(IReadOnlyList<IFix> fixes)
    {
        _activeFixes = fixes;
        OnPropertyChanged(nameof(NeedsElevation));
        OnPropertyChanged(nameof(ApplyLabel));
        OnPropertyChanged(nameof(AdminNote));
        HasProgress = false;
        ProgressText = "";
        ProgressPercent = 0;
        Stage = FlowStage.Previewing;
        PreviewLines.Clear();
        Message = "";
        IsConfirmed = false;
        AutoRestart = false;
        RequiresReboot = false;
        HasChanges = false;

        try
        {
            var previews = new List<(IFix Fix, FixPreview Preview)>();
            foreach (var fix in fixes)
            {
                previews.Add((fix, await fix.PreviewAsync(CancellationToken.None)));
            }

            // Only fixes that have something to do are listed (with a header when there are several);
            // when none does, every "nothing to do" note is shown instead.
            var active = previews.Where(p => p.Preview.HasChanges).ToList();
            foreach (var (fix, preview) in active.Count > 0 ? active : previews)
            {
                if (active.Count > 1)
                {
                    PreviewLines.Add(new PreviewLine(fix.Title, IsHeader: true));
                }

                foreach (var line in preview.Changes)
                {
                    PreviewLines.Add(new PreviewLine(line));
                }
            }

            HasChanges = active.Count > 0;
            RequiresReboot = previews.Any(p => p.Preview.RequiresReboot);

            Stage = FlowStage.AwaitingConfirmation;
        }
        catch (Exception ex)
        {
            Message = Strings.Format(Strings.FlowPreviewFailed, ex.Message);
            Stage = FlowStage.Finished;
        }
    }

    private bool CanStart() => !IsBusy;

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            return;
        }

        PreviewLines.Clear();
        Message = "";
        IsConfirmed = false;
        Stage = FlowStage.Idle;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (NeedsElevation)
        {
            Message = _elevation.RestartElevated()
                ? Strings.Get(Strings.FlowRestarting)
                : Strings.Get(Strings.FlowElevationDenied);
            return;
        }

        Stage = FlowStage.Applying;
        IsConfirmed = false;

        var summaries = new List<string>();
        var rebootNeeded = false;
        var allSucceeded = true;
        var wantsAutoRestart = AutoRestart;
        AutoRestart = false;
        RebootScheduled = false;

        // With several fixes the bar moves a step per finished fix; a fix that can tell more (disk cleanup) fills its own step.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var count = _activeFixes.Count;
        ProgressPercent = 0;
        HasProgress = count > 1;
        ProgressText = count > 1 ? ProgressEstimate.Format(0, TimeSpan.Zero) : "";

        void Show(int index, FixProgress progress)
        {
            var overall = (index + Math.Clamp(progress.Fraction, 0, 1)) / count;
            HasProgress = true;
            ProgressPercent = overall * 100;
            var estimate = ProgressEstimate.Format(overall, clock.Elapsed);
            ProgressText = string.IsNullOrEmpty(progress.Text) ? estimate : progress.Text + " · " + estimate;
        }

        try
        {
            for (var index = 0; index < count; index++)
            {
                var current = index;
                var fix = _activeFixes[index];
                var result = await fix.ApplyAsync(_journal, new UiProgress<FixProgress>(p => Show(current, p)), CancellationToken.None);
                summaries.Add(result.Summary);
                rebootNeeded |= result.RequiresReboot;
                allSucceeded &= result.Success;

                if (count > 1)
                {
                    Show(current, new FixProgress(1));
                }
            }
        }
        catch (Exception ex)
        {
            summaries.Add(Strings.Format(Strings.FlowApplyFailed, ex.Message));
            allSucceeded = false;
        }

        ProgressText = "";
        Message = string.Join(Environment.NewLine, summaries);
        RebootNeeded = rebootNeeded;
        Stage = FlowStage.Finished;
        await RefreshUndoAsync();

        // Restarting by itself is only for a fully clean run; with any error the user decides.
        if (rebootNeeded && allSucceeded && wantsAutoRestart)
        {
            await ScheduleRestartAsync(AutoRestartDelaySeconds);
        }
    }

    [RelayCommand]
    private Task RestartNowAsync() => ScheduleRestartAsync(RestartNowDelaySeconds);

    [RelayCommand]
    private async Task CancelRestartAsync()
    {
        Message = await _reboot.CancelAsync(CancellationToken.None)
            ? Strings.Get(Strings.RebootCancelled)
            : Strings.Get(Strings.RebootCancelFailed);
        RebootScheduled = false;
    }

    private async Task ScheduleRestartAsync(int delaySeconds)
    {
        try
        {
            if (await _reboot.ScheduleRestartAsync(delaySeconds, CancellationToken.None))
            {
                RebootScheduled = true;
                Message = Strings.Format(Strings.RebootScheduled, delaySeconds);
                return;
            }
        }
        catch (Exception ex)
        {
            Message = Strings.Get(Strings.RebootScheduleFailed) + " " + ex.Message;
            return;
        }

        Message = Strings.Get(Strings.RebootScheduleFailed);
    }

    [RelayCommand]
    private async Task UndoAsync(UndoItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        var fix = _fixes.FirstOrDefault(f => f.Id == item.Record.FixId);
        if (fix is null)
        {
            Message = Strings.Get(Strings.FlowUndoUnknownFix);
            return;
        }

        try
        {
            await fix.UndoAsync(item.Record, CancellationToken.None);
            await _journal.RemoveAsync(item.Record.Id, CancellationToken.None);
            Message = Strings.Format(Strings.FlowUndone, item.Record.Description);
        }
        catch (Exception ex)
        {
            Message = Strings.Format(Strings.FlowUndoFailed, ex.Message);
        }

        if (Stage == FlowStage.Idle)
        {
            Stage = FlowStage.Finished;
        }

        await RefreshUndoAsync();
    }

    /// <summary>Loads the journal so changes from an earlier session can still be undone.</summary>
    public async Task RefreshUndoAsync()
    {
        var records = await _journal.LoadAsync(CancellationToken.None);

        UndoItems.Clear();
        foreach (var record in records.OrderByDescending(r => r.Timestamp))
        {
            UndoItems.Add(new UndoItem(record, $"{record.Description}  ({record.Timestamp.LocalDateTime:g})"));
        }

        OnPropertyChanged(nameof(HasUndoItems));
    }
}
