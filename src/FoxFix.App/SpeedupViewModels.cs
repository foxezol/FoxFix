using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Startup;

namespace FoxFix.App;

public sealed partial class StartupItem : ObservableObject
{
    public StartupItem(StartupEntry entry)
    {
        Entry = entry;
        _isEnabled = entry.Enabled;
    }

    public StartupEntry Entry { get; }

    /// <summary>The program's own icon; null until it is loaded or when the file has none (the view then shows the first letter).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _icon;

    public bool HasIcon => Icon is not null;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public string Name => Entry.Name;

    public string Command => Entry.Command;

    public bool NeedsAdmin => Entry.NeedsAdmin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private bool _isEnabled;

    public bool IsChanged => IsEnabled != Entry.Enabled;
}

/// <summary>The list of startup programs with a checkbox each. Changes go through the normal preview, confirm and undo flow.</summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupStore _store;
    private readonly Action<IReadOnlyDictionary<string, bool>> _setDesired;
    private readonly Func<Task> _startFlow;
    private readonly Func<StartupEntry, ImageSource?> _loadIcon;

    public StartupViewModel(
        IStartupStore store,
        Action<IReadOnlyDictionary<string, bool>> setDesired,
        Func<Task> startFlow,
        Func<StartupEntry, ImageSource?>? loadIcon = null)
    {
        _store = store;
        _setDesired = setDesired;
        _startFlow = startFlow;
        _loadIcon = loadIcon ?? (_ => null);
    }

    public ObservableCollection<StartupItem> Items { get; } = [];

    /// <summary>Programs that start with Windows right now. Grouped by the state on disk, so a tick does not make a row jump.</summary>
    public ObservableCollection<StartupItem> EnabledItems { get; } = [];

    public ObservableCollection<StartupItem> DisabledItems { get; } = [];

    public string EnabledHeader => $"Açık olanlar ({EnabledItems.Count})";

    public string DisabledHeader => $"Kapalı olanlar ({DisabledItems.Count})";

    public bool IsEmpty => Items.Count == 0;

    public bool HasChanges => Items.Any(i => i.IsChanged);

    public int ChangeCount => Items.Count(i => i.IsChanged);

    [ObservableProperty]
    private string _loadError = "";

    public bool HasLoadError => LoadError.Length > 0;

    partial void OnLoadErrorChanged(string value) => OnPropertyChanged(nameof(HasLoadError));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        LoadError = "";

        try
        {
            // Icons are read next to the list, off the UI thread; a missing icon is not an error.
            var loaded = await Task.Run(() => _store.List().Select(e => (Entry: e, Icon: SafeIcon(e))).ToList());

            foreach (var old in Items)
            {
                old.PropertyChanged -= OnItemChanged;
            }

            Items.Clear();
            EnabledItems.Clear();
            DisabledItems.Clear();
            foreach (var (entry, icon) in loaded)
            {
                var item = new StartupItem(entry) { Icon = icon };
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
                (entry.Enabled ? EnabledItems : DisabledItems).Add(item);
            }
        }
        catch (Exception ex)
        {
            LoadError = $"Liste okunamadı: {ex.Message}";
        }

        NotifyChanged();
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task ApplyAsync()
    {
        StageChanges();
        await _startFlow();
    }

    /// <summary>Hands the ticked changes to the fix, without starting anything. The "Beni Düzelt" picker uses this.</summary>
    public void StageChanges() =>
        _setDesired(Items.Where(i => i.IsChanged).ToDictionary(i => i.Entry.Id, i => i.IsEnabled));

    private ImageSource? SafeIcon(StartupEntry entry)
    {
        try
        {
            return _loadIcon(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupItem.IsEnabled))
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(EnabledHeader));
        OnPropertyChanged(nameof(DisabledHeader));
        OnPropertyChanged(nameof(ChangeCount));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class CleanupItem : ObservableObject
{
    public CleanupItem(ICleanupTarget target)
    {
        Target = target;
    }

    public ICleanupTarget Target { get; }

    public string Title => Target.Title;

    public string Description => Target.Description;

    [ObservableProperty]
    private string _sizeText = "…";

    [ObservableProperty]
    private long _bytes;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Shows how much each cleanup target would free and lets the user pick. Deleting goes through the confirm flow.</summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly Action<IEnumerable<string>> _select;
    private readonly Func<Task> _startFlow;

    public CleanupViewModel(IReadOnlyList<ICleanupTarget> targets, Action<IEnumerable<string>> select, Func<Task> startFlow)
    {
        _select = select;
        _startFlow = startFlow;

        foreach (var target in targets)
        {
            var item = new CleanupItem(target);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CleanupItem.IsSelected) or nameof(CleanupItem.Bytes))
                {
                    NotifyChanged();
                }
            };
            Items.Add(item);
        }
    }

    public ObservableCollection<CleanupItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isScanning;

    public bool HasSelection => Items.Any(i => i.IsSelected);

    public bool CanClean => HasSelection && !IsScanning;

    public string SelectedTotalText => ByteSize.Format(Items.Where(i => i.IsSelected).Sum(i => i.Bytes));

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;

        try
        {
            foreach (var item in Items)
            {
                item.SizeText = "…";
                var target = item.Target;

                try
                {
                    item.Bytes = await Task.Run(() => target.MeasureBytes(CancellationToken.None));
                    item.SizeText = ByteSize.Format(item.Bytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    item.Bytes = 0;
                    item.SizeText = "?";
                }
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        StageSelection();
        await _startFlow();
    }

    /// <summary>Hands the ticked targets to the fix, without starting anything. The "Beni Düzelt" picker uses this.</summary>
    public void StageSelection() => _select(Items.Where(i => i.IsSelected).Select(i => i.Target.Id).ToList());

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(SelectedTotalText));
        CleanCommand.NotifyCanExecuteChanged();
    }
}
