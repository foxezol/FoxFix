﻿using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Reporting;

namespace FoxFix.App;

/// <summary>The home screen and the pages next to it.</summary>
public enum AppPage
{
    Home,
    Connection,
    Tweaks,
    DnsDpi,
    Access,
    Overlay,
    Devices,
    Report,
    Settings,
}

public enum VerdictState
{
    Checking,
    Ok,

    /// <summary>The connection works, but something is worth a look.</summary>
    Attention,
    Problem,
    Failed,
}

/// <summary>Which theme color a row uses. The view maps it to a brush so it follows light/dark live.</summary>
public enum StatusKind
{
    Ok,
    Warn,
    Bad,
    Neutral,
}

/// <param name="Icon">A 16x16 line drawing (not a font glyph, so it can never spill out of its circle).</param>
/// <param name="StatusLabel">Spoken by screen readers, because the symbol and color alone say nothing to them.</param>
public sealed record CheckRow(Geometry Icon, string Title, string Summary, string? Detail, StatusKind Kind, string StatusLabel)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

public static class StatusIcons
{
    public static Geometry Ok { get; } = Draw("M3,8.5 L6.5,12 L13,4.5");

    public static Geometry Bad { get; } = Draw("M4.2,4.2 L11.8,11.8 M11.8,4.2 L4.2,11.8");

    /// <summary>An exclamation mark; its dot is a zero-length round-capped stroke.</summary>
    public static Geometry Warn { get; } = Draw("M8,3.2 L8,9 M8,12.3 L8,12.4");

    public static Geometry Neutral { get; } = Draw("M4,8 L12,8");

    private static Geometry Draw(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DiagnosticsEngine _engine;
    private readonly Action<string>? _openLink;
    private readonly IDiagnosticReportService _reports;
    private readonly IClipboardService _clipboard;
    private readonly IReportFileSaver _saver;

    private DiagnosticsReport? _lastDiagnosis;

    public MainViewModel(
        DiagnosticsEngine engine,
        FixFlowViewModel fix,
        IDiagnosticReportService reports,
        IClipboardService clipboard,
        IReportFileSaver saver,
        SpeedTestViewModel speed,
        StartupViewModel startup,
        CleanupViewModel cleanup,
        GoodbyeDpiViewModel? dpi = null,
        Action<string>? openLink = null,
        NoticeViewModel? notice = null,
        AccessViewModel? access = null,
        SettingsViewModel? settings = null,
        UpdateViewModel? updates = null,
        TweaksViewModel? tweaks = null,
        DevicesViewModel? devices = null,
        OnboardingViewModel? onboarding = null,
        OverlayViewModel? overlay = null)
    {
        Overlay = overlay;
        Access = access;
        Devices = devices;
        Onboarding = onboarding;
        Tweaks = tweaks;
        Settings = settings;

        // Switching the experimental page off while it is open takes the person back to the overview.
        if (settings is not null)
        {
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.ExperimentalDevices) && !settings.ExperimentalDevices && Page == AppPage.Devices)
                {
                    OpenPage(AppPage.Home);
                }
            };
        }
        Updates = updates;
        Notice = notice ?? new NoticeViewModel();
        Info = new InfoViewModel(TryOpenLink);
        Dpi = dpi;
        _openLink = openLink;
        _engine = engine;
        Fix = fix;
        Speed = speed;
        Startup = startup;
        Cleanup = cleanup;
        RepairPicker = new RepairPickerViewModel(startup, cleanup, fix, () => OpenPage(AppPage.Devices));

        // After any change was applied the lists show old data, so read them again.
        fix.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.Stage) && fix.Stage == FlowStage.Finished)
            {
                startup.RefreshCommand.Execute(null);
                cleanup.ScanCommand.Execute(null);
                tweaks?.Refresh();
                _ = CheckAfterRepairAsync();
            }
        };
        _reports = reports;
        _clipboard = clipboard;
        _saver = saver;
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsHome), nameof(IsConnection), nameof(IsTweaks), nameof(IsDnsDpi), nameof(IsAccess), nameof(IsDevices), nameof(IsOverlay), nameof(IsReport), nameof(IsSettings),
        nameof(ShowBack), nameof(PageTitle), nameof(PageSubtitle))]
    private AppPage _page = AppPage.Home;

    /// <summary>A one-off message ("Rapor kopyalandı.") belongs to the page it came from; it goes when the person moves on.</summary>
    partial void OnPageChanged(AppPage value) => StatusText = "";

    public bool IsHome => Page == AppPage.Home;

    public bool IsConnection => Page == AppPage.Connection;

    public bool IsTweaks => Page == AppPage.Tweaks;

    public bool IsDnsDpi => Page == AppPage.DnsDpi;

    public bool IsAccess => Page == AppPage.Access;

    public bool IsDevices => Page == AppPage.Devices;

    public bool IsOverlay => Page == AppPage.Overlay;

    public bool IsReport => Page == AppPage.Report;

    public bool IsSettings => Page == AppPage.Settings;

    /// <summary>The access test; null in tests that do not need it.</summary>
    public AccessViewModel? Access { get; }

    /// <summary>The game overlay page; null in tests that do not need it.</summary>
    public OverlayViewModel? Overlay { get; }

    /// <summary>The sound, microphone and camera wizard; null in tests that do not need it.</summary>
    public DevicesViewModel? Devices { get; }

    /// <summary>The first-start question, the tour and the FoxDPI hint; null in tests that do not need them.</summary>
    public OnboardingViewModel? Onboarding { get; }

    /// <summary>The Windows settings page (from Win11Debloat); null in tests that do not need it.</summary>
    public TweaksViewModel? Tweaks { get; }

    /// <summary>The Ayarlar page; null in tests that do not need it.</summary>
    public SettingsViewModel? Settings { get; }

    /// <summary>Update checks and their pop-up; null in tests that do not need them.</summary>
    public UpdateViewModel? Updates { get; }

    public bool ShowBack => Page != AppPage.Home;

    public string PageTitle => Page switch
    {
        AppPage.Connection => Strings.Get(Strings.PageConnection),
        AppPage.Tweaks => Strings.Get(Strings.PageTweaks),
        AppPage.DnsDpi => Strings.Get(Strings.PageDnsDpi),
        AppPage.Access => Strings.Get(Strings.PageAccess),
        AppPage.Devices => Strings.Get(Strings.PageDevices),
        AppPage.Overlay => Strings.Get(Strings.PageOverlay),
        AppPage.Report => Strings.Get(Strings.PageReport),
        AppPage.Settings => Strings.Get(Strings.PageSettings),
        _ => "",
    };

    /// <summary>One line under the page title: what the page is for, in the person's words.</summary>
    public string PageSubtitle => Page switch
    {
        AppPage.Connection => Strings.Get(Strings.SubConnection),
        AppPage.Tweaks => Strings.Get(Strings.SubTweaks),
        AppPage.DnsDpi => Strings.Get(Strings.SubDnsDpi),
        AppPage.Access => Strings.Get(Strings.SubAccess),
        AppPage.Devices => Strings.Get(Strings.SubDevices),
        AppPage.Overlay => Strings.Get(Strings.SubOverlay),
        AppPage.Report => Strings.Get(Strings.SubReport),
        AppPage.Settings => Strings.Get(Strings.SubSettings),
        _ => "",
    };

    [RelayCommand]
    private void OpenPage(AppPage page)
    {
        var left = Page;
        Page = page;

        // A running microphone, speaker or camera test never outlives the page it was started on.
        if (left == AppPage.Devices && page != AppPage.Devices && Devices is not null)
        {
            _ = Devices.OnLeftAsync();
        }

        if (page == AppPage.Devices && left != AppPage.Devices && Devices is not null)
        {
            _ = Devices.OnEnteredAsync();
        }

        if (left == AppPage.Overlay && page != AppPage.Overlay)
        {
            Overlay?.StopPlacing();
        }

        Onboarding?.OnPageEntered(page);

        // The Windows settings change Windows itself: on arriving, the page reads the saved settings and warns when there are none.
        if (page == AppPage.Tweaks)
        {
            Tweaks?.OnEntered();
        }
    }

    [RelayCommand]
    private void GoHome() => OpenPage(AppPage.Home);

    public FixFlowViewModel Fix { get; }

    public SpeedTestViewModel Speed { get; }

    public StartupViewModel Startup { get; }

    public CleanupViewModel Cleanup { get; }

    public RepairPickerViewModel RepairPicker { get; }

    /// <summary>Messages that appear in the middle of the window.</summary>
    public NoticeViewModel Notice { get; }

    /// <summary>The version and project links pop-up.</summary>
    public InfoViewModel Info { get; }

    /// <summary>The GoodbyeDPI manager; null in tests that do not need it.</summary>
    public GoodbyeDpiViewModel? Dpi { get; }

    /// <summary>Where feedback goes: the maker's Discord profile.</summary>
    public const string FeedbackUrl = "https://discord.com/users/381533632852131841";

    [RelayCommand]
    private void OpenFeedback() => TryOpenLink(FeedbackUrl);

    /// <summary>The red button on the Ayarlar page: closes any pop-up and shows the usual preview of what will be removed, then asks.</summary>
    [RelayCommand]
    private async Task UninstallAsync()
    {
        Info.CloseCommand.Execute(null);
        await Fix.StartSingleCommand.ExecuteAsync(FoxFix.Core.GoodbyeDpi.UninstallFix.FixIdValue);
    }

    /// <summary>Where the source code lives.</summary>
    public const string RepoUrl = "https://github.com/foxezol/FoxFix";

    [RelayCommand]
    private void OpenRepo() => TryOpenLink(RepoUrl);

    [RelayCommand]
    private void OpenWin11Debloat() => TryOpenLink(InfoViewModel.Win11DebloatUrl);

    /// <summary>Raised after the project address is on the clipboard; the window shows a small "Kopyalandı" that floats up and fades.</summary>
    public event Action? RepoLinkCopied;

    [RelayCommand]
    private void ShareRepo()
    {
        try
        {
            _clipboard.SetText(RepoUrl);
        }
        catch (Exception ex)
        {
            Notice.Show(Strings.Get(Strings.LinkCopyFailedTitle), ErrorCodes.Tag(ErrorCodes.ClipboardFailed) + ex.Message);
            return;
        }

        RepoLinkCopied?.Invoke();
    }
    private void TryOpenLink(string url)
    {
        try
        {
            _openLink?.Invoke(url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Notice.Show(Strings.Get(Strings.LinkFailedTitle), ErrorCodes.Tag(ErrorCodes.LinkFailed) + ex.Message);
        }
    }
    /// <summary>Off by default: the report hides user name, computer name and MAC addresses.</summary>
    [ObservableProperty]
    private bool _showPersonalData;

    [RelayCommand]
    private async Task CopyReportAsync()
    {
        var text = await TryBuildReportAsync();
        if (text is null)
        {
            return;
        }

        try
        {
            _clipboard.SetText(text);
            StatusText = Strings.Get(Strings.ReportCopied);
        }
        catch (Exception ex)
        {
            StatusText = Strings.Format(Strings.ReportCopyFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        var text = await TryBuildReportAsync();
        if (text is null)
        {
            return;
        }

        try
        {
            var fileName = $"FoxFix-rapor-{DateTime.Now:yyyyMMdd-HHmm}.txt";
            var path = await _saver.SaveAsync(fileName, text, CancellationToken.None);

            // null means the user closed the dialog: nothing happened, so nothing to announce.
            if (path is not null)
            {
                StatusText = Strings.Format(Strings.ReportSaved, path);
            }
        }
        catch (Exception ex)
        {
            StatusText = Strings.Format(Strings.ReportSaveFailed, ex.Message);
        }
    }

    private async Task<string?> TryBuildReportAsync()
    {
        try
        {
            return await _reports.CreateAsync(_lastDiagnosis, maskPersonalData: !ShowPersonalData, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = Strings.Format(Strings.ReportBuildFailed, ex.Message);
            return null;
        }
    }

    public ObservableCollection<CheckRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDiagnosticsCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    /// <summary>"Beni Düzelt" waits for a running check: it would be fixing from numbers that are about to change.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Short one-off messages (e.g. "Edge açıldı"). The main message is the verdict.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    private string _statusText = "";

    public bool HasStatusText => StatusText.Length > 0;

    [ObservableProperty]
    private VerdictState _verdict = VerdictState.Checking;

    [ObservableProperty]
    private string _verdictTitle = Strings.Get(Strings.VerdictChecking);

    [ObservableProperty]
    private string _verdictDetail = Strings.Get(Strings.VerdictCheckingDetail);

    /// <summary>"Önce / sonra" under the verdict: which checks changed because of the repair. Gone when the person checks again by hand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComparison))]
    private string _comparisonText = "";

    public bool HasComparison => ComparisonText.Length > 0;

    private bool _checkingAfterRepair;

    /// <summary>
    /// After a repair the connection check runs again by itself and is set against the one from before it, so the person sees
    /// what the repair changed instead of having to remember what was wrong.
    /// </summary>
    private async Task CheckAfterRepairAsync()
    {
        var before = _lastDiagnosis;
        if (before is null || IsBusy)
        {
            return;
        }

        _checkingAfterRepair = true;
        ComparisonText = Strings.Get(Strings.CompareChecking);
        try
        {
            await RunDiagnosticsAsync();
            ComparisonText = _lastDiagnosis is { } after && !ReferenceEquals(after, before)
                ? RepairComparison.Text(RepairComparison.Changed(before, after))
                : "";
        }
        finally
        {
            _checkingAfterRepair = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunDiagnosticsAsync()
    {
        IsBusy = true;
        Rows.Clear();
        StatusText = "";
        if (!_checkingAfterRepair)
        {
            ComparisonText = "";
        }

        DiagnosisPercent = 0;
        DiagnosisProgressText = Strings.Get(Strings.Starting);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        SetVerdict(VerdictState.Checking, Strings.Get(Strings.VerdictChecking), Strings.Get(Strings.VerdictCheckingDetail));
        RepairPicker.UpdateFromDiagnosis(null);

        try
        {
            // Not Progress<T>: it posts asynchronously, so rows could arrive after the run (or after a new run
            // cleared the list). The engine reports from the UI thread's own continuation, so inline is safe.
            var progress = new InlineProgress<CheckReport>(r =>
            {
                Rows.Add(ToRow(r));
                var total = Math.Max(_engine.Count, Rows.Count);
                var fraction = (double)Rows.Count / total;
                DiagnosisPercent = fraction * 100;
                DiagnosisProgressText = $"{Rows.Count} / {total} kontrol · " + ProgressEstimate.Format(fraction, clock.Elapsed);
            });
            var report = await _engine.RunAsync(progress, CancellationToken.None);
            _lastDiagnosis = report;
            RepairPicker.UpdateFromDiagnosis(report);

            // A check that itself broke (Error) is worth a note too, it just is not a verdict on the network.
            var notes = report.Reports.Count(r => r.Result.Status is CheckStatus.Warning or CheckStatus.Error);

            if (report.FirstFailure is { } failure)
            {
                SetVerdict(VerdictState.Problem, Strings.Get(Strings.VerdictProblem), Strings.Format(Strings.VerdictProblemDetail, failure.Title));
            }
            else if (notes > 0)
            {
                SetVerdict(VerdictState.Attention, Strings.Format(Strings.VerdictAttention, notes), Strings.Get(Strings.VerdictAttentionDetail));
            }
            else
            {
                SetVerdict(VerdictState.Ok, Strings.Get(Strings.VerdictOk), Strings.Get(Strings.VerdictOkDetail));
            }
        }
        catch (Exception ex)
        {
            SetVerdict(VerdictState.Failed, Strings.Get(Strings.VerdictFailed), ex.Message);
        }
        finally
        {
            DiagnosisPercent = 100;
            IsBusy = false;
        }
    }

    private void SetVerdict(VerdictState state, string title, string detail)
    {
        Verdict = state;
        VerdictTitle = title;
        VerdictDetail = detail;
    }

    /// <summary>0 to 100: how many of the checks have answered.</summary>
    [ObservableProperty]
    private double _diagnosisPercent;

    [ObservableProperty]
    private string _diagnosisProgressText = "";

    private bool CanRun() => !IsBusy;

    private static CheckRow ToRow(CheckReport report)
    {
        var (icon, kind, label) = report.Result.Status switch
        {
            CheckStatus.Passed => (StatusIcons.Ok, StatusKind.Ok, Strings.StatusOkLabel),
            CheckStatus.Failed => (StatusIcons.Bad, StatusKind.Bad, Strings.StatusBadLabel),
            CheckStatus.Skipped => (StatusIcons.Neutral, StatusKind.Neutral, Strings.StatusSkippedLabel),
            _ => (StatusIcons.Warn, StatusKind.Warn, Strings.StatusWarnLabel),
        };

        return new CheckRow(icon, report.Title, report.Result.Summary, report.Result.Detail, kind, Strings.Get(label));
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
