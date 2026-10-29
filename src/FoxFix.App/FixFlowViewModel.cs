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

    public const int AutoRestartDelaySeconds = 5;
    public const int RestartNowDelaySeconds = 5;

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
        nameof(ShowAutoRestartOption), nameof(ShowRebootBanner), nameof(ShowBusyNote))]
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

    public bool HasAdminNote => AdminNote.Length > 0;

    /// <summary>While the change is prepared or runs the panel says so, because some steps keep the app busy for a while and it must not look frozen.</summary>
    public bool ShowBusyNote => Stage is FlowStage.Previewing or FlowStage.Applying;

    /// <summary>"İşlem başarılı" or "İşlem tamamlanamadı" above the details once a change has finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultTitle))]
    private string _resultTitle = "";

    [ObservableProperty]
    private bool _resultSucceeded;

    public bool HasResultTitle => ResultTitle.Length > 0;

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
        OnPropertyChanged(nameof(HasAdminNote));
        ResultTitle = "";
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
            using (var phase = new PhaseProgress(this, fixes.Count, PreviewSecondsPerFix))
            {
                for (var index = 0; index < fixes.Count; index++)
                {
                    var fix = fixes[index];
                    phase.BeginStep(index);

                    // Looking at the system can take a moment (the list of services); not on the window's thread, so the note stays alive.
                    previews.Add((fix, await Task.Run(() => fix.PreviewAsync(CancellationToken.None))));
                    phase.Finished(index);
                }
            }

            HasProgress = false;
            ProgressText = "";
            ProgressPercent = 0;

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

    /// <summary>What a fix usually takes, used only to fill the bar for fixes that cannot say how far they are.</summary>
    private const double PreviewSecondsPerFix = 2.5;

    private const double ApplySecondsPerFix = 6;

    private bool CanStart() => !IsBusy;

    /// <summary>
    /// The bar and the time left for one phase (looking, or changing). Real progress from a fix wins; without it the bar fills
    /// by the usual time of a step, never past 90% of it, so it cannot claim to be done before it is.
    /// </summary>
    private sealed class PhaseProgress : IDisposable
    {
        private readonly FixFlowViewModel _vm;
        private readonly int _count;
        private readonly double _perStep;
        private readonly System.Diagnostics.Stopwatch _phase = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _step = System.Diagnostics.Stopwatch.StartNew();
        private readonly CancellationTokenSource _stop = new();
        private int _index;
        private bool _real;

        public PhaseProgress(FixFlowViewModel vm, int count, double perStep)
        {
            _vm = vm;
            _count = Math.Max(1, count);
            _perStep = perStep;
            _vm.HasProgress = true;
            _vm.ProgressPercent = 0;
            Update();
            _ = TickAsync();
        }

        public void BeginStep(int index)
        {
            _index = index;
            _real = false;
            _step.Restart();
            Update();
        }

        public void Real(int index, FixProgress progress)
        {
            _real = true;
            var overall = (index + Math.Clamp(progress.Fraction, 0, 1)) / _count;
            _vm.ProgressPercent = overall * 100;
            var estimate = ProgressEstimate.Format(overall, _phase.Elapsed);
            _vm.ProgressText = string.IsNullOrEmpty(progress.Text) ? estimate : progress.Text + " · " + estimate;
        }

        public void Finished(int index)
        {
            _vm.ProgressPercent = (index + 1) / (double)_count * 100;
        }

        private void Update()
        {
            var stepFraction = Math.Min(0.9, _step.Elapsed.TotalSeconds / _perStep);
            var overall = (_index + stepFraction) / _count;
            _vm.ProgressPercent = overall * 100;

            var remaining = TimeSpan.FromSeconds(Math.Max(0, 1 - overall) * _count * _perStep);
            _vm.ProgressText = _step.Elapsed.TotalSeconds > _perStep
                ? $"%{(int)(overall * 100)} · birkaç saniye daha"
                : $"%{(int)(overall * 100)} · yaklaşık {ProgressEstimate.Duration(remaining)} kaldı";
        }

        private async Task TickAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await Task.Delay(250, _stop.Token);
                    if (!_real)
                    {
                        Update();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The phase ended.
            }
        }

        public void Dispose() => _stop.Cancel();
    }

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

        // The bar moves a step per finished fix. A fix that can tell how far it is (disk cleanup) fills its own step; for the
        // others the bar fills by the time such a step usually takes, so there is always an idea of how long it will be.
        var count = _activeFixes.Count;
        using var phase = new PhaseProgress(this, count, ApplySecondsPerFix);

        try
        {
            for (var index = 0; index < count; index++)
            {
                var current = index;
                var fix = _activeFixes[index];
                phase.BeginStep(current);
                // Off the window's thread: a step that waits for Windows (stopping a service, a driver) must not freeze the window.
                var progress = new UiProgress<FixProgress>(p => phase.Real(current, p));
                var result = await Task.Run(() => fix.ApplyAsync(_journal, progress, CancellationToken.None));
                summaries.Add(result.Summary);
                rebootNeeded |= result.RequiresReboot;
                allSucceeded &= result.Success;

                phase.Finished(current);
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
        ResultSucceeded = allSucceeded;
        ResultTitle = Strings.Get(allSucceeded ? Strings.FlowResultOk : Strings.FlowResultFailed);
        Stage = FlowStage.Finished;
        await RefreshUndoAsync();

        // The tick box was only offered because the preview said a restart is needed, so ticking it means "restart afterwards".
        // Only a fully clean run restarts by itself; with any error the user decides.
        if (allSucceeded && wantsAutoRestart)
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
