using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public RepairPickerViewModel(StartupViewModel startup, CleanupViewModel cleanup, FixFlowViewModel flow)
    {
        _startup = startup;
        _cleanup = cleanup;
        _flow = flow;

        // The startup and cleanup choices are made on the "Bilgisayarı hızlandır" page; the picker only reflects them.
        startup.PropertyChanged += (_, _) => Refresh();
        cleanup.PropertyChanged += (_, _) => Refresh();
    }

    [ObservableProperty]
    private bool _isOpen;

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
        ? $"Açılışta başlayan programlar: {_startup.ChangeCount} değişiklik, Bilgisayarı hızlandır sayfasında seçtiğin"
        : "Açılışta başlayan programlar: henüz bir şey seçmedin. Seçmek için Bilgisayarı hızlandır sayfasına bak.";

    public string CleanupText => _cleanup.HasSelection
        ? $"Disk temizliği: seçtiklerin {_cleanup.SelectedTotalText} yer açar"
        : "Disk temizliği: aşağıdan silinecekleri işaretle";

    public bool CanContinue => IncludeDpi || IncludeDns || (IncludeStartup && StartupAvailable) || _cleanup.HasSelection;

    [RelayCommand]
    private void Open()
    {
        IncludeStartup = _startup.HasChanges;
        Refresh();
        IsOpen = true;
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
        ContinueCommand.NotifyCanExecuteChanged();
    }
}
