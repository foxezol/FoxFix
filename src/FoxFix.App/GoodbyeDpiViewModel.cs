using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Fixes;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>Remembers which GoodbyeDPI setting the user picked (or the trial found), so FoxDPI can start with it later.</summary>
public sealed class DpiChoiceStore
{
    private readonly string _path;

    public DpiChoiceStore(string? path = null)
    {
        _path = path ?? Path.Combine(FoxFix.Core.AppPaths.LocalRoot, "dpi-choice.txt");
    }

    public string? Load()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string CustomPath => Path.Combine(Path.GetDirectoryName(_path)!, "zapret-custom.txt");

    /// <summary>The Zapret setting the user edited by hand; null when there is none.</summary>
    public string? LoadCustom()
    {
        try
        {
            return File.Exists(CustomPath) && File.ReadAllText(CustomPath).Trim() is { Length: > 0 } text ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>An empty text forgets the edited setting.</summary>
    public void SaveCustom(string arguments)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                File.Delete(CustomPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CustomPath)!);
            File.WriteAllText(CustomPath, arguments.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience, like the choice itself.
        }
    }

    public void Save(string profileId)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, profileId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience: the next start simply begins with the standard setting.
        }
    }
}

/// <summary>One line of the trial: a ready-made setting and how it did.</summary>
public sealed partial class TrialRow : ObservableObject
{
    public TrialRow(GoodbyeDpiProfile profile)
    {
        Profile = profile;
    }

    public GoodbyeDpiProfile Profile { get; }

    public string Title => Profile.Title;

    [ObservableProperty]
    private string _statusText = "Sırada";

    /// <summary>"Ok", "Warn", "Bad" or "Neutral": picks the colour, like the diagnosis rows.</summary>
    [ObservableProperty]
    private string _kind = "Neutral";

    [ObservableProperty]
    private bool _isBest;

    /// <summary>The setting that install and FoxDPI will use. One row at a time.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public void Apply(TrialStep step)
    {
        (StatusText, Kind) = step.Status switch
        {
            TrialStatus.Testing => ("Deneniyor…", "Neutral"),
            TrialStatus.AllOpened => ($"Hepsi açıldı ({step.Reached}/{step.Total})", "Ok"),
            TrialStatus.SomeOpened => ($"Bir kısmı açıldı ({step.Reached}/{step.Total})", "Warn"),
            TrialStatus.NothingOpened => ("Hiçbiri açılmadı", "Bad"),
            TrialStatus.FailedToStart => ("Başlamadı", "Bad"),
            _ => ("Sırada", "Neutral"),
        };
    }
}

/// <summary>
/// GoodbyeDPI in FoxFix: find the setting that works on this connection, run it in a FoxDPI window or install it as a
/// service, and reset everything to Windows' defaults. Changes to the system go through the preview, confirm and undo flow.
/// </summary>
public sealed partial class GoodbyeDpiViewModel : ObservableObject
{
    public const string RelaunchArgument = "--foxdpi";

    private static readonly string[] ResetFixIds = [DpiCleanupFix.FixId, GoodbyeDpiRemoveFix.FixIdValue, ZapretRemoveFix.FixIdValue, DnsResetFix.FixIdValue];

    private readonly GoodbyeDpiTrial _trial;
    private readonly GoodbyeDpiInstallFix _install;
    private readonly GoodbyeDpiStatusReader _status;
    private readonly GoodbyeDpiTeardown _teardown;
    private readonly FoxDpiSession _foxDpi;
    private readonly FixFlowViewModel _flow;
    private readonly IElevation _elevation;
    private readonly DpiChoiceStore? _choice;
    private readonly NoticeViewModel? _notice;
    private readonly FoxDpiAutoStart? _autoStart;
    private readonly FoxDpiAutoStartFix? _autoStartFix;
    private readonly GoodbyeDpiTrial? _zapretTrial;
    private readonly GoodbyeDpiInstallFix? _zapretInstall;
    private readonly GoodbyeDpiTeardown? _zapretTeardown;
    private GoodbyeDpiStatus? _lastStatus;

    public GoodbyeDpiViewModel(
        GoodbyeDpiTrial trial,
        GoodbyeDpiInstallFix install,
        GoodbyeDpiStatusReader status,
        GoodbyeDpiTeardown teardown,
        FoxDpiSession foxDpi,
        FixFlowViewModel flow,
        IElevation elevation,
        DpiChoiceStore? choice = null,
        NoticeViewModel? notice = null,
        FoxDpiAutoStart? autoStart = null,
        FoxDpiAutoStartFix? autoStartFix = null,
        GoodbyeDpiTrial? zapretTrial = null,
        GoodbyeDpiInstallFix? zapretInstall = null,
        GoodbyeDpiTeardown? zapretTeardown = null)
    {
        _zapretTrial = zapretTrial;
        _zapretInstall = zapretInstall;
        _zapretTeardown = zapretTeardown;
        _autoStart = autoStart;
        _autoStartFix = autoStartFix;
        _notice = notice;
        _trial = trial;
        _install = install;
        _status = status;
        _teardown = teardown;
        _foxDpi = foxDpi;
        _flow = flow;
        _elevation = elevation;
        _choice = choice;

        _selectedProfile = GoodbyeDpiProfiles.Find(choice?.Load()) ?? GoodbyeDpiProfiles.All[0];
        if (_selectedProfile.Engine == DpiEngines.ZapretId && zapretTrial is null)
        {
            _selectedProfile = GoodbyeDpiProfiles.All[0];
        }

        BuildRows(DpiEngines.Of(_selectedProfile));

        // A setting edited by hand last time comes back as it was.
        if (zapretTrial is not null && IsZapret && choice?.LoadCustom() is { } savedCustom && ZapretProfiles.Validate(savedCustom) is null)
        {
            CustomArguments = savedCustom;
            IsEditingSetting = true;
        }

        // A finished install, reset or undo changes what is running, so read it again.
        flow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.Stage) && flow.Stage == FlowStage.Finished)
            {
                PendingRefresh = RefreshStatusAsync();
            }
        };
    }

    public const string DefaultHosts = "discord.com";

    /// <summary>One row per ready-made setting of the chosen program, with the current setting picked.</summary>
    private void BuildRows(DpiEngine engine)
    {
        Rows.Clear();

        foreach (var profile in engine.Profiles)
        {
            var row = new TrialRow(profile);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TrialRow.IsSelected) && row.IsSelected)
                {
                    SelectedProfile = row.Profile;
                }
            };
            Rows.Add(row);
        }

        var pick = engine.Profiles.FirstOrDefault(p => p.Id == SelectedProfile.Id) ?? engine.Profiles[0];
        Rows.First(r => r.Profile.Id == pick.Id).IsSelected = true;
    }

    /// <summary>The tick box under the Zapret list: run the setting with text the user changed.</summary>
    [ObservableProperty]
    private bool _isEditingSetting;

    [ObservableProperty]
    private string _customArguments = "";

    partial void OnIsEditingSettingChanged(bool value)
    {
        if (value && CustomArguments.Length == 0)
        {
            CustomArguments = SelectedProfile.Arguments;
        }

        if (value)
        {
            _choice?.SaveCustom(CustomArguments);
        }
        else
        {
            _choice?.SaveCustom("");
        }

        OnPropertyChanged(nameof(CustomArgumentsError));
        OnPropertyChanged(nameof(HasCustomArgumentsError));
    }

    partial void OnCustomArgumentsChanged(string value)
    {
        if (IsEditingSetting && ZapretProfiles.Validate(value) is null)
        {
            _choice?.SaveCustom(value);
        }

        OnPropertyChanged(nameof(CustomArgumentsError));
        OnPropertyChanged(nameof(HasCustomArgumentsError));
    }

    /// <summary>Empty when the edited text can be used (or is not in use).</summary>
    public string CustomArgumentsError => IsZapret && IsEditingSetting ? ZapretProfiles.Validate(CustomArguments) ?? "" : "";

    public bool HasCustomArgumentsError => CustomArgumentsError.Length > 0;

    /// <summary>What FoxDPI and the service actually run: the chosen ready-made setting, or the edited one.</summary>
    private GoodbyeDpiProfile EffectiveProfile => IsZapret && IsEditingSetting ? ZapretProfiles.Custom(CustomArguments) : SelectedProfile;

    /// <summary>Zapret is offered only when the app gave this view model a trial for it.</summary>
    public bool HasZapret => _zapretTrial is not null;

    public bool IsZapret => SelectedProfile.Engine == DpiEngines.ZapretId;

    public bool IsGoodbyeDpi => !IsZapret;

    /// <summary>The two-way choice above the list: which program the settings below belong to.</summary>
    [RelayCommand(CanExecute = nameof(IsNotTesting))]
    private void SelectEngine(string engineId)
    {
        var engine = engineId == DpiEngines.ZapretId ? DpiEngines.Zapret : DpiEngines.GoodbyeDpi;
        if (engine.Id == DpiEngines.Of(SelectedProfile).Id || (engine.Id == DpiEngines.ZapretId && !HasZapret))
        {
            return;
        }

        ResultText = "";
        BuildRows(engine);
    }

    /// <summary>Raised when FoxDPI is running; the app then opens its small FoxDPI window.</summary>
    public event Action<GoodbyeDpiProfile>? FoxDpiStarted;

    private void Notify(string title, string message)
    {
        if (_notice is null)
        {
            ResultText = message;
            return;
        }

        _notice.Show(title, message);
    }

    /// <summary>The status read that started when the last change finished; tests wait for it before cleaning up.</summary>
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;

    public ObservableCollection<TrialRow> Rows { get; } = [];

    public bool IsElevated => _elevation.IsElevated;

    public bool NeedsElevation => !IsElevated;

    [ObservableProperty]
    private string _hosts = DefaultHosts;

    [ObservableProperty]
    private GoodbyeDpiProfile _selectedProfile;

    partial void OnSelectedProfileChanged(GoodbyeDpiProfile value)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = row.Profile.Id == value.Id;
        }

        _choice?.Save(value.Id);
        OnPropertyChanged(nameof(SelectedProfileTitle));
        OnPropertyChanged(nameof(IsZapret));
        OnPropertyChanged(nameof(IsGoodbyeDpi));
        OnPropertyChanged(nameof(CustomArgumentsError));
        OnPropertyChanged(nameof(HasCustomArgumentsError));
        UpdateServiceFlag();
        InstallServiceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shown next to the FoxDPI button so it is clear which setting it will use.</summary>
    public string SelectedProfileTitle => SelectedProfile.Title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultText = "";

    public bool HasResult => ResultText.Length > 0;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>0 to 100: how many of the settings have been tried.</summary>
    [ObservableProperty]
    private double _trialPercent;

    [ObservableProperty]
    private string _trialProgressText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyCanExecuteChangedFor(nameof(RunTrialCommand), nameof(InstallServiceCommand), nameof(ToggleFoxDpiCommand), nameof(ResetCommand), nameof(ToggleAutoStartCommand))]
    private bool _isTesting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop), nameof(FoxDpiButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand), nameof(ToggleFoxDpiCommand))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveServiceCommand))]
    private bool _isServiceInstalled;

    /// <summary>True while the FoxDPI window of this very program is the one running GoodbyeDPI.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoxDpiButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFoxDpiCommand))]
    private bool _isOwnSession;

    /// <summary>There is no stop button: FoxDPI is closed by closing its window.</summary>
    public string FoxDpiButtonText => IsOwnSession ? "Pencereyi göster" : IsActive ? "Açık" : "FoxDPI'yı başlat";

    public bool CanAct => IsElevated && !IsTesting;

    public bool CanStop => IsElevated && IsActive && !IsTesting;

    /// <summary>Reads what is running and shows it. The scan asks Windows for every service, so the app uses the async version.</summary>
    [RelayCommand]
    public void RefreshStatus() => ShowStatus(ReadStatus());

    public async Task RefreshStatusAsync()
    {
        ShowStatus(await Task.Run(ReadStatus));

        if (_autoStart is not null)
        {
            try
            {
                IsAutoStart = await _autoStart.ExistsAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                IsAutoStart = false;
            }
        }
    }

    /// <summary>True when a scheduled task starts FoxDPI at sign-in.</summary>
    [ObservableProperty]
    private bool _isAutoStart;

    public bool CanAutoStart => _autoStartFix is not null;

    /// <summary>The tick box: turns "start FoxDPI with Windows" on or off, through the usual preview and confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ToggleAutoStartAsync()
    {
        _autoStartFix!.Configure(!IsAutoStart);
        return _flow.StartSingleCommand.ExecuteAsync(FoxDpiAutoStartFix.FixIdValue);
    }

    /// <summary>"Hizmeti kaldır" follows the program that is chosen.</summary>
    private void UpdateServiceFlag()
    {
        if (_lastStatus is { } status)
        {
            IsServiceInstalled = IsZapret ? status.ZapretServiceInstalled : status.ServiceInstalled;
        }
    }

    private (GoodbyeDpiStatus? Status, string? Error) ReadStatus()
    {
        try
        {
            return (_status.Read(), null);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    private void ShowStatus((GoodbyeDpiStatus? Status, string? Error) result)
    {
        if (result.Status is not { } status)
        {
            StatusText = $"Durum okunamadı: {result.Error}";
            return;
        }

        IsActive = status.IsActive;
        IsOwnSession = _foxDpi.IsRunning;
        _lastStatus = status;
        UpdateServiceFlag();

        var profile = GoodbyeDpiProfiles.Find(status.State?.ProfileId);
        StatusText = status switch
        {
            _ when IsOwnSession && _foxDpi.Profile is { Engine: DpiEngines.ZapretId } running => $"Zapret çalışıyor. Ayar: {running.Title}",
            { OtherRunning: true } => "Başka bir GoodbyeDPI ya da Zapret çalışıyor. FoxDPI onunla aynı anda çalışamaz.",
            { ZapretServiceRunning: true } => "Zapret hizmet olarak çalışıyor.",
            { ZapretServiceInstalled: true } when IsZapret => "Zapret hizmeti kurulu ama durmuş.",
            { ServiceRunning: true } => $"Hizmet olarak çalışıyor. Ayar: {profile?.Title ?? "bilinmiyor"}",
            { ManualRunning: true } => $"Çalışıyor. Ayar: {profile?.Title ?? "bilinmiyor"}",
            { ServiceInstalled: true } => $"Hizmet kurulu ama durmuş. Ayar: {profile?.Title ?? "bilinmiyor"}",
            _ => "Çalışmıyor.",
        };
    }

    [RelayCommand]
    private void Elevate() => Relaunch("--sayfa=dpi");

    private void Relaunch(string argument)
    {
        if (!_elevation.RestartElevated(argument))
        {
            Notify("Yönetici izni", "Yönetici izni verilmedi. Hiçbir şey değişmedi.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct), IncludeCancelCommand = true)]
    private async Task RunTrialAsync(CancellationToken ct)
    {
        var targets = TrialTargets.Parse(Hosts);
        if (targets.Count == 0)
        {
            ResultText = "Denenecek bir site adı yaz. Örnek: discord.com";
            return;
        }

        IsTesting = true;
        ResultText = "";
        TrialPercent = 0;
        TrialProgressText = "Başlıyor…";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var row in Rows)
        {
            row.StatusText = "Sırada";
            row.Kind = "Neutral";
            row.IsBest = false;
        }

        try
        {
            var report = await (IsZapret ? _zapretTrial! : _trial).RunAsync(targets, new InlineProgress<TrialStep>(step => Show(step, clock.Elapsed)), ct);
            ResultText = report.Message;

            if (report.Best is not null)
            {
                SelectedProfile = report.Best;
                Rows.First(r => r.Profile.Id == report.Best.Id).IsBest = true;
            }
        }
        catch (OperationCanceledException)
        {
            ResultText = "Deneme durduruldu.";
        }
        finally
        {
            IsTesting = false;
            await RefreshStatusAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallService))]
    private Task InstallServiceAsync()
    {
        if (IsZapret)
        {
            if (CustomArgumentsError.Length > 0)
            {
                Notify("Ayar geçersiz", CustomArgumentsError);
                return Task.CompletedTask;
            }

            _zapretInstall!.Configure(EffectiveProfile);
            return _flow.StartSingleCommand.ExecuteAsync(_zapretInstall.Id);
        }

        _install.Configure(SelectedProfile.Id);
        return _flow.StartSingleCommand.ExecuteAsync(GoodbyeDpiInstallFix.FixIdValue);
    }

    /// <summary>
    /// The button on the overview. Not running: starts FoxDPI (restarting the app for administrator rights if needed) and
    /// the app opens its small FoxDPI window. Already running in its window: brings that window forward. There is no
    /// stop here; FoxDPI ends when its window is closed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleFoxDpi))]
    private async Task ToggleFoxDpiAsync()
    {
        if (_foxDpi.IsRunning && _foxDpi.Profile is { } running)
        {
            FoxDpiStarted?.Invoke(running);
            return;
        }

        if (!IsElevated)
        {
            Relaunch(RelaunchArgument);
            return;
        }

        await StartFoxDpiAsync();
    }

    /// <summary>Not while a trial runs, and not when something else (the service) already runs GoodbyeDPI.</summary>
    private bool CanToggleFoxDpi() => !IsTesting && (IsOwnSession || !IsActive);

    private bool IsNotTesting() => !IsTesting;

    /// <summary>Remembers the setting once, so FoxDPI.exe can start on its own later. Opening FoxFix is the first run.</summary>
    public void RememberChoice()
    {
        if (_choice?.Load() is null)
        {
            _choice?.Save(SelectedProfile.Id);
        }
    }

    public async Task StartFoxDpiAsync()
    {
        if (CustomArgumentsError.Length > 0)
        {
            Notify("Ayar geçersiz", CustomArgumentsError);
            return;
        }

        var profile = EffectiveProfile;
        var result = await _foxDpi.StartAsync(profile, CancellationToken.None);

        if (result.Started)
        {
            FoxDpiStarted?.Invoke(profile);
            Notify(Strings.Get(Strings.FoxDpiExplainTitle), Strings.Get(Strings.FoxDpiExplain));
        }
        else
        {
            Notify("FoxDPI başlamadı", result.Message);
        }

        await RefreshStatusAsync();
    }
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        try
        {
            await _foxDpi.StopAsync();
            await _teardown.StopAsync(CancellationToken.None);

            if (_zapretTeardown is not null)
            {
                await _zapretTeardown.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Notify("FoxDPI durdurulamadı", ex.Message);
        }

        await RefreshStatusAsync();
    }

    /// <summary>Takes the installed service away again, with the usual preview and confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveService))]
    private Task RemoveServiceAsync() =>
        _flow.StartSingleCommand.ExecuteAsync(IsZapret ? ZapretRemoveFix.FixIdValue : GoodbyeDpiRemoveFix.FixIdValue);

    /// <summary>The service of whichever program is chosen; Zapret needs its own install fix.</summary>
    private bool CanInstallService() => CanAct && (IsGoodbyeDpi || _zapretInstall is not null);

    private bool CanRemoveService() => IsServiceInstalled && CanAct;
    /// <summary>
    /// "DPI Sıfırlama": old DPI tools, FoxFix's own GoodbyeDPI and hand-typed DNS are all taken back to the Windows
    /// default, in one preview.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsNotTesting))]
    private Task ResetAsync() => _flow.StartManyAsync(ResetFixIds);

    private void Show(TrialStep step, TimeSpan elapsed)
    {
        Rows.FirstOrDefault(r => r.Profile.Id == step.ProfileId)?.Apply(step);

        var tried = Rows.Count(r => r.StatusText != "Sırada" && r.StatusText != "Deneniyor…");
        var fraction = (double)tried / Rows.Count;
        TrialPercent = fraction * 100;
        TrialProgressText = $"{tried} / {Rows.Count} ayar denendi · " + ProgressEstimate.Format(fraction, elapsed);
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
