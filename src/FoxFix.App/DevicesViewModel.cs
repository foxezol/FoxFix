using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Devices;

namespace FoxFix.App;

/// <summary>Plays a short test sound on the default output. Started and stopped by the person.</summary>
public interface IToneTester
{
    /// <summary>Plays until the sound is over or <see cref="Stop"/> is called. Throws when it cannot play.</summary>
    Task PlayAsync(CancellationToken ct);

    void Stop();
}

public sealed record DeviceRow(string Name, string StateText, StatusKind Kind);

public enum TestPhase
{
    Idle,
    Starting,
    Running,
    Done,
}

/// <summary>One step of the wizard: speakers, microphone or camera. Only the speakers can be tested.</summary>
public sealed partial class DeviceStepViewModel : ObservableObject
{
    public DeviceStepViewModel(DeviceKind kind)
    {
        Kind = kind;
        Title = DeviceChecker.KindName(kind);
    }

    public DeviceKind Kind { get; }

    public string Title { get; }

    public bool IsSpeaker => Kind == DeviceKind.Speaker;

    public ObservableCollection<DeviceRow> Devices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDevices))]
    private bool _hasDevices;

    public bool HasNoDevices => !HasDevices;

    [ObservableProperty]
    private bool _isChecking;

    /// <summary>"Devices Windows can see (3)": the folding list's title.</summary>
    [ObservableProperty]
    private string _devicesHeader = "";

    [ObservableProperty]
    private string _verdictText = "";

    [ObservableProperty]
    private StatusKind _verdictKind = StatusKind.Neutral;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrivacy))]
    private string _privacyText = "";

    public bool HasPrivacy => PrivacyText.Length > 0;

    [ObservableProperty]
    private StatusKind _privacyKind = StatusKind.Neutral;

    /// <summary>A Windows Settings page that fits the finding, and the label of the button that opens it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingsLink))]
    private string? _settingsUri;

    [ObservableProperty]
    private string _settingsLabel = "";

    public bool HasSettingsLink => SettingsUri is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTesting))]
    [NotifyPropertyChangedFor(nameof(CanStartTest))]
    private TestPhase _phase = TestPhase.Idle;

    public bool IsTesting => Phase is TestPhase.Starting or TestPhase.Running;

    public bool CanStartTest => !IsTesting && !IsChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestStatus))]
    private string _testStatus = "";

    public bool HasTestStatus => TestStatus.Length > 0;

    [ObservableProperty]
    private StatusKind _testKind = StatusKind.Neutral;

    partial void OnIsCheckingChanged(bool value) => OnPropertyChanged(nameof(CanStartTest));
}

/// <summary>
/// The "Ses ve kamera" wizard: for the speakers, the microphone and the camera it shows what Windows sees and whether the privacy
/// settings allow access; the speakers also have a short test sound. Everything is read only; no setting is changed, nothing is
/// recorded, and the microphone and the camera are never switched on. A finding that cannot be read is shown as "could not be checked", never as a diagnosis.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly DeviceChecker _checker;
    private readonly IToneTester _tone;
    private readonly Action<string> _openSettings;

    private CancellationTokenSource? _testCts;

    public DevicesViewModel(
        DeviceChecker checker, IToneTester tone, Action<string> openSettings)
    {
        _checker = checker;
        _tone = tone;
        _openSettings = openSettings;

        Steps = [new DeviceStepViewModel(DeviceKind.Speaker), new DeviceStepViewModel(DeviceKind.Microphone), new DeviceStepViewModel(DeviceKind.Camera)];
        _current = Steps[0];
    }

    public IReadOnlyList<DeviceStepViewModel> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepIndex), nameof(StepText), nameof(CanGoBack), nameof(CanGoNext), nameof(IsSpeaker))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    private DeviceStepViewModel _current;

    public int StepIndex => Steps.ToList().IndexOf(Current);

    public string StepText => Strings.Format(Strings.DevStepOf, StepIndex + 1, Steps.Count);

    public bool CanGoBack => StepIndex > 0;

    public bool CanGoNext => StepIndex < Steps.Count - 1;

    public bool IsSpeaker => Current.Kind == DeviceKind.Speaker;

    [ObservableProperty]
    private string _actionError = "";

    /// <summary>Called when the page is opened: looks at every kind once (read-only).</summary>
    public async Task OnEnteredAsync()
    {
        foreach (var step in Steps)
        {
            await CheckAsync(step);
        }
    }

    /// <summary>Called when the person leaves the page: every test stops.</summary>
    public async Task OnLeftAsync() => await StopTestAsync(Current, DevicesStopReason.Left);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task PreviousAsync()
    {
        await StopTestAsync(Current, DevicesStopReason.Left);
        Current = Steps[StepIndex - 1];
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        await StopTestAsync(Current, DevicesStopReason.Left);
        Current = Steps[StepIndex + 1];
    }

    [RelayCommand]
    private async Task CheckAgainAsync()
    {
        await StopTestAsync(Current, DevicesStopReason.Left);
        await CheckAsync(Current);
    }

    /// <summary>Asks Windows about one kind and fills the step. Anything unreadable becomes "could not be checked".</summary>
    public async Task CheckAsync(DeviceStepViewModel step)
    {
        step.IsChecking = true;

        // Windows is asked off the window's thread; what comes back is shown on it.
        var result = await Task.Run(() => _checker.Check(step.Kind));
        Fill(step, result);
    }

    private void Fill(DeviceStepViewModel step, DeviceCheckResult result)
    {
        try
        {
            step.Devices.Clear();
            // Devices Windows still remembers but that are gone (not present) are history, not something it sees: they are left out.
            foreach (var device in result.Devices.Where(d => d.State != DeviceState.NotPresent))
            {
                step.Devices.Add(new DeviceRow(device.Name, StateText(device.State), StateKind(device.State)));
            }

            step.HasDevices = step.Devices.Count > 0;
            step.DevicesHeader = Strings.Format(Strings.DevDevicesHeaderCount, step.Devices.Count);

            if (result.Verdict == DeviceVerdict.Unknown)
            {
                step.VerdictText = Strings.Format(Strings.DevCheckFailed, DeviceChecker.KindName(step.Kind), result.Error ?? "");
            }
            else
            {
                step.VerdictText = result.Message;
            }

            step.VerdictKind = result.Verdict switch
            {
                DeviceVerdict.Ok => StatusKind.Ok,
                DeviceVerdict.Unknown => StatusKind.Neutral,
                DeviceVerdict.NoDevice => StatusKind.Warn,
                _ => StatusKind.Bad,
            };

            (step.PrivacyText, step.PrivacyKind) = PrivacyLine(result.Privacy);
            step.SettingsUri = result.SettingsUri;
            step.SettingsLabel = result.SettingsUri is null ? "" : LinkLabel(result.SettingsUri);
        }
        finally
        {
            step.IsChecking = false;
        }
    }

    public static string StateText(DeviceState state) => Strings.Get(state switch
    {
        DeviceState.Active => Strings.DevStateActive,
        DeviceState.Disabled => Strings.DevStateDisabled,
        DeviceState.Unplugged => Strings.DevStateUnplugged,
        DeviceState.NotPresent => Strings.DevStateNotPresent,
        DeviceState.Problem => Strings.DevStateProblem,
        _ => Strings.DevStateUnknown,
    });

    public static StatusKind StateKind(DeviceState state) => state switch
    {
        DeviceState.Active => StatusKind.Ok,
        DeviceState.Disabled or DeviceState.Problem => StatusKind.Bad,
        DeviceState.Unplugged or DeviceState.NotPresent => StatusKind.Warn,
        _ => StatusKind.Neutral,
    };

    public static (string Text, StatusKind Kind) PrivacyLine(PrivacyAccess access) => access switch
    {
        PrivacyAccess.NotApplicable => ("", StatusKind.Neutral),
        PrivacyAccess.Allowed => (Strings.Get(Strings.DevPrivacyAllowed), StatusKind.Ok),
        PrivacyAccess.DeniedByUser => (Strings.Get(Strings.DevPrivacyDeniedUser), StatusKind.Bad),
        PrivacyAccess.DeniedForDesktopApps => (Strings.Get(Strings.DevPrivacyDeniedDesktop), StatusKind.Bad),
        PrivacyAccess.DeniedByPolicy => (Strings.Get(Strings.DevPrivacyPolicy), StatusKind.Bad),
        _ => (Strings.Get(Strings.DevPrivacyUnknown), StatusKind.Neutral),
    };

    private static string LinkLabel(string uri) => Strings.Get(uri switch
    {
        DeviceSettingsLinks.MicrophonePrivacy => Strings.DevOpenMicPrivacy,
        DeviceSettingsLinks.CameraPrivacy => Strings.DevOpenCameraPrivacy,
        DeviceSettingsLinks.Camera => Strings.DevOpenCameraSettings,
        _ => Strings.DevOpenSound,
    });

    [RelayCommand]
    private void OpenSettings(string? uri)
    {
        ActionError = "";
        if (uri is null || !uri.StartsWith("ms-settings:", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _openSettings(uri);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ActionError = Strings.Format(Strings.DevOpenFailed, ex.Message);
        }
    }

    // ---------------------------------------------------------------- tests (always started and stopped by the person)

    [RelayCommand]
    private async Task StartTestAsync()
    {
        var step = Current;
        if (step.Kind != DeviceKind.Speaker || step.IsTesting)
        {
            return;
        }

        await StopTestAsync(step, DevicesStopReason.Left);
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        step.Phase = TestPhase.Starting;
        step.TestKind = StatusKind.Neutral;

        try
        {
            await RunToneAsync(step, ct);
        }
        catch (OperationCanceledException)
        {
            // Stopped by the person: StopTestAsync has already said so.
        }
    }

    private async Task RunToneAsync(DeviceStepViewModel step, CancellationToken ct)
    {
        step.TestStatus = Strings.Get(Strings.DevToneRunning);
        step.Phase = TestPhase.Running;

        try
        {
            await _tone.PlayAsync(ct);
            if (ct.IsCancellationRequested)
            {
                // Stopped or left: the words for that were already put there; the sound ending afterwards must not replace them.
                return;
            }

            step.TestStatus = Strings.Get(Strings.DevToneDone);
            step.TestKind = StatusKind.Neutral;
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            step.TestStatus = Strings.Format(Strings.DevToneFailed, ex.Message);
            step.TestKind = StatusKind.Bad;
        }
        finally
        {
            step.Phase = TestPhase.Done;
        }
    }

    /// <summary>What the test sound can throw when the output device or the driver says no. Anything else is a bug and is not hidden.</summary>
    private static bool IsDeviceError(Exception ex) =>
        ex is InvalidOperationException or IOException or Win32Exception or UnauthorizedAccessException or NotSupportedException
            or System.Runtime.InteropServices.COMException or InvalidCastException or ArgumentException or TimeoutException;

    [RelayCommand]
    private Task StopTestAsync() => StopTestAsync(Current, DevicesStopReason.Person);

    private Task StopTestAsync(DeviceStepViewModel step, DevicesStopReason reason)
    {
        if (!step.IsTesting)
        {
            return Task.CompletedTask;
        }

        var cts = _testCts;
        _testCts = null;
        cts?.Cancel();
        _tone.Stop();
        cts?.Dispose();

        step.Phase = TestPhase.Done;
        if (reason == DevicesStopReason.Left)
        {
            step.TestStatus = "";
        }
        else
        {
            step.TestStatus = Strings.Get(Strings.DevStopped);
            step.TestKind = StatusKind.Neutral;
        }

        return Task.CompletedTask;
    }
}

public enum DevicesStopReason
{
    /// <summary>The person pressed Stop (or the time limit ended).</summary>
    Person,

    /// <summary>The person went to another step or page.</summary>
    Left,
}
