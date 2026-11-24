using FoxFix.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Fixes;

namespace FoxFix.App;

/// <summary>
/// The window "Beni Düzelt" opens: the user ticks which jobs to run (old DPI tools, DNS, startup programs, disk cleanup).
/// Continuing only opens the usual change preview; nothing is changed before the confirmation there.
/// </summary>
public sealed partial class RepairPickerViewModel : ObservableObject
{
    private readonly StartupViewModel _startup;
    private readonly CleanupViewModel _cleanup;
    private readonly FixFlowViewModel _flow;

    private readonly Action? _openDevices;

    public RepairPickerViewModel(StartupViewModel startup, CleanupViewModel cleanup, FixFlowViewModel flow, Action? openDevices = null)
    {
        _startup = startup;
        _cleanup = cleanup;
        _flow = flow;
        _openDevices = openDevices;

        // The startup and cleanup choices are made on the "Hızlandırma ve ayarlar" page; the picker only reflects them.
        startup.PropertyChanged += (_, _) => Refresh();
        cleanup.PropertyChanged += (_, _) => Refresh();
    }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>What the last connection check said about the two jobs the picker can ask for by itself.</summary>
    public CheckFinding DpiFinding { get; private set; } = CheckFinding.Unknown;

    public CheckFinding DnsFinding { get; private set; } = CheckFinding.Unknown;

    /// <summary>Called when a check starts (null: nothing is known yet) and when it ends (its report).</summary>
    public void UpdateFromDiagnosis(DiagnosticsReport? report)
    {
        DpiFinding = FindingOf(report, "dpi-residue");
        DnsFinding = FindingOf(report, "dns");
        OnPropertyChanged(nameof(DpiNote));
        OnPropertyChanged(nameof(DnsNote));
        OnPropertyChanged(nameof(DpiRecommended));
        OnPropertyChanged(nameof(DnsRecommended));
        OnPropertyChanged(nameof(SummaryText));
    }

    private static CheckFinding FindingOf(DiagnosticsReport? report, string id)
    {
        var status = report?.Reports.FirstOrDefault(r => r.CheckId == id)?.Result.Status;
        return status switch
        {
            CheckStatus.Warning or CheckStatus.Failed => CheckFinding.Found,
            CheckStatus.Passed => CheckFinding.Clean,
            _ => CheckFinding.Unknown,
        };
    }

    public bool DpiRecommended => DpiFinding == CheckFinding.Found;

    public bool DnsRecommended => DnsFinding == CheckFinding.Found;

    public string DpiNote => Note(DpiFinding, Strings.PickerDpiFound, Strings.PickerDpiClean);

    public string DnsNote => Note(DnsFinding, Strings.PickerDnsFound, Strings.PickerDnsOk);

    private static string Note(CheckFinding finding, string found, string clean) => finding switch
    {
        CheckFinding.Found => Strings.Get(found),
        CheckFinding.Clean => Strings.Get(clean),
        _ => Strings.Get(Strings.PickerNotChecked),
    };

    /// <summary>One line over the list: how many jobs the check recommends (and ticked), or that none is needed.</summary>
    public string SummaryText
    {
        get
        {
            if (DpiFinding == CheckFinding.Unknown && DnsFinding == CheckFinding.Unknown)
            {
                return Strings.Get(Strings.PickerNotChecked);
            }

            var count = (DpiRecommended ? 1 : 0) + (DnsRecommended ? 1 : 0);
            return count == 0 ? Strings.Get(Strings.PickerSummaryNone) : Strings.Format(Strings.PickerSummarySome, count);
        }
    }

    /// <summary>Why "Önizlemeyi göster" is off, when it is.</summary>
    public bool ShowNothingTicked => !CanContinue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _includeDpi = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _includeDns = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _includeStartup;

    public CleanupViewModel Cleanup => _cleanup;

    public bool StartupAvailable => _startup.HasChanges;

    public string StartupText => _startup.HasChanges
        ? Strings.Format(Strings.PickerStartupChosen, _startup.ChangeCount)
        : Strings.Get(Strings.PickerStartupNone);

    public string CleanupText => _cleanup.HasSelection
        ? Strings.Format(Strings.PickerCleanupChosen, _cleanup.SelectedTotalText)
        : Strings.Get(Strings.PickerCleanupNone);

    public bool CanContinue => IncludeDpi || IncludeDns || (IncludeStartup && StartupAvailable) || _cleanup.HasSelection;

    [RelayCommand]
    private void Open()
    {
        // What the check found is ticked; what it found clean is not (an unknown result keeps the old default: ticked).
        IncludeDpi = DpiFinding != CheckFinding.Clean;
        IncludeDns = DnsFinding != CheckFinding.Clean;
        IncludeStartup = _startup.HasChanges;
        Refresh();
        IsOpen = true;
    }

    /// <summary>Sound, microphone and camera problems need no system change: the wizard is a page of its own.</summary>
    [RelayCommand]
    private void OpenDevices()
    {
        IsOpen = false;
        _openDevices?.Invoke();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueAsync()
    {
        var ids = new List<string>();

        if (IncludeDpi)
        {
            ids.Add(DpiCleanupFix.FixId);
        }

        if (IncludeDns)
        {
            ids.Add(DnsRepairFix.FixId);
        }

        if (IncludeStartup && _startup.HasChanges)
        {
            _startup.StageChanges();
            ids.Add(StartupFix.FixIdValue);
        }

        if (_cleanup.HasSelection)
        {
            _cleanup.StageSelection();
            ids.Add(DiskCleanupFix.FixIdValue);
        }

        IsOpen = false;
        await _flow.StartManyAsync(ids);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(StartupAvailable));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(CleanupText));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ShowNothingTicked));
        ContinueCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>What a check found for a job of the picker.</summary>
public enum CheckFinding
{
    /// <summary>No result yet, or the check could not say.</summary>
    Unknown,

    Clean,

    Found,
}
