using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Tweaks;

namespace FoxFix.App;

/// <summary>One stand-alone Windows setting: a tick box, with what it is about and a rough idea of how much it matters.</summary>
public sealed partial class TweakRow : ObservableObject
{
    public TweakRow(WindowsTweak tweak)
    {
        Tweak = tweak;
        Info = TweakInfos.For(tweak.Id);
    }

    public WindowsTweak Tweak { get; }

    public TweakInfo? Info { get; }

    public string Title => Tweak.Title;

    public string Description => Tweak.Description;

    public bool HasDescription => Description.Length > 0;

    public bool IsRecommended => Info?.Recommended == true;

    public bool HasInfo => Info is not null;

    /// <summary>"Hız · tahmini etki %12". It is a guess, and the page says so.</summary>
    public string ImpactText => Info is null ? "" : $"{Info.AreaName} · tahmini etki %{Info.Percent}";

    /// <summary>"High", "Medium" or "Low": the view turns it into red, yellow or green.</summary>
    public string ImpactLevel => Info?.Level.ToString() ?? "Low";

    /// <summary>What the setting is about ("Hız", "Gizlilik"...), shown in plain muted text.</summary>
    public string AreaName => Info?.AreaName ?? "";

    /// <summary>"Yüksek etki %30": the badge says it in words and a number, the colour only underlines it.</summary>
    public string ImpactBadgeText => Info is null
        ? ""
        : $"{Info.Level switch { Core.Tweaks.ImpactLevel.High => "Yüksek", Core.Tweaks.ImpactLevel.Medium => "Orta", _ => "Düşük" }} etki %{Info.Percent}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect), nameof(CanRevert))]
    private bool _isApplied;

    /// <summary>A setting that already holds cannot be ticked again; "Geri aç" (or "Geri Al") is how it goes back.</summary>
    public bool CanSelect => !IsApplied;

    /// <summary>"Geri aç": it holds now and Windows' own value is known, so it can be switched back to the default.</summary>
    public bool CanRevert => IsApplied && Tweak.Defaults is { Count: > 0 };

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isMatch = true;
}

/// <summary>One choice of a group. The first one of each group, "Değiştirme", stands for leaving things as they are.</summary>
public sealed partial class TweakChoice : ObservableObject
{
    public TweakChoice(string label, IReadOnlyList<string> tweakIds, bool keep = false)
    {
        Label = label;
        TweakIds = tweakIds;
        IsKeep = keep;
    }

    public string Label { get; }

    public IReadOnlyList<string> TweakIds { get; }

    public bool IsKeep { get; }

    /// <summary>The choice Windows itself has: its label says "(Varsayılan)".</summary>
    public bool IsWindowsDefault => Label.Contains("(Varsayılan)", StringComparison.Ordinal);

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>This is how the setting is right now.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>A group of settings of which one holds: a short list of radio buttons.</summary>
public sealed class TweakGroupRow : ObservableObject
{
    public TweakGroupRow(TweakGroup group, IEnumerable<TweakChoice> choices)
    {
        Group = group;
        Choices = new ObservableCollection<TweakChoice>(choices);

        foreach (var choice in Choices)
        {
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakChoice.IsSelected) && choice.IsSelected)
                {
                    foreach (var other in Choices.Where(c => !ReferenceEquals(c, choice)))
                    {
                        other.IsSelected = false;
                    }
                }
            };
        }
    }

    public TweakGroup Group { get; }

    public string Title => Group.Title;

    public string Description => Group.Description;

    public bool HasDescription => Description.Length > 0;

    public ObservableCollection<TweakChoice> Choices { get; }

    private bool _isMatch = true;

    public bool IsMatch
    {
        get => _isMatch;
        set => SetProperty(ref _isMatch, value);
    }

    /// <summary>The settings the person picked in this group (none when "Değiştirme" is chosen or the choice already holds).</summary>
    public IEnumerable<string> PickedIds =>
        Choices.Where(c => c.IsSelected && !c.IsKeep && !c.IsCurrent).SelectMany(c => c.TweakIds);

    /// <summary>The settings that make up the choice Windows has by default (empty when the group names none).</summary>
    public IEnumerable<string> WindowsDefaultIds =>
        Choices.Where(c => c.IsWindowsDefault && !c.IsCurrent).SelectMany(c => c.TweakIds);
}

/// <summary>A folding section of the page: the title always shows; the list, and the buttons that act on it, only when it is open.</summary>
public sealed partial class TweakCategoryRow : ObservableObject
{
    public TweakCategoryRow(string title)
    {
        Title = title;
        Toggles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasToggles));
    }

    public string Title { get; }

    public ObservableCollection<TweakRow> Toggles { get; } = [];

    /// <summary>For the view: a list of single settings (not only groups of choices) gets the "önerilenleri seç" button.</summary>
    public bool HasToggles => Toggles.Count > 0;

    public ObservableCollection<TweakGroupRow> Groups { get; } = [];

    /// <summary>False when the search leaves nothing of this category to show.</summary>
    [ObservableProperty]
    private bool _hasVisible = true;

    /// <summary>Closed at first: forty rows at once is a wall.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _summary = "";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Ticks every setting of this list that FoxFix recommends (and that is not already in place).</summary>
    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var toggle in Toggles.Where(t => t.IsMatch))
        {
            toggle.IsSelected = toggle.CanSelect && toggle.IsRecommended;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var toggle in Toggles.Where(t => t.IsMatch))
        {
            toggle.IsSelected = toggle.CanSelect;
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var toggle in Toggles)
        {
            toggle.IsSelected = false;
        }

        foreach (var group in Groups)
        {
            group.Choices[0].IsSelected = true;
        }
    }
}

/// <summary>One saved set of Windows settings in the list.</summary>
public sealed class BackupRow
{
    public BackupRow(TweakBackup backup)
    {
        Id = backup.Id;
        Name = backup.Name;
        DateText = backup.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    }

    public string Id { get; }

    public string Name { get; }

    public string DateText { get; }
}

/// <summary>
/// The "Windows ayarları" page: Win11Debloat's registry settings (privacy, suggestions, AI, taskbar, Explorer, animations...)
/// as tick boxes and short choices, in folding lists, with ready-made picks on top (the recommended ones, everything, back to
/// the Windows default, and the settings the person saved earlier). Ticking changes nothing; "Seçilenleri uygula" goes through
/// the usual preview, confirmation and undo. Because these are changes to Windows itself the page asks to save the current
/// settings first, so there is always a way back.
/// </summary>
public sealed partial class TweaksViewModel : ObservableObject
{
    private readonly TweakCatalog _catalog;
    private readonly WindowsTweakFix _fix;
    private readonly Func<Task> _startFlow;
    private readonly TweakBackupService? _backupService;
    private readonly TweakBackupStore? _backupStore;
    private readonly TweakRestoreFix? _restoreFix;
    private readonly Func<Task>? _startRestore;

    public TweaksViewModel(
        TweakCatalog catalog,
        WindowsTweakFix fix,
        int windowsBuild,
        Func<Task> startFlow,
        TweakBackupService? backupService = null,
        TweakBackupStore? backupStore = null,
        TweakRestoreFix? restoreFix = null,
        Func<Task>? startRestore = null)
    {
        _catalog = catalog;
        _fix = fix;
        _startFlow = startFlow;
        _backupService = backupService;
        _backupStore = backupStore;
        _restoreFix = restoreFix;
        _startRestore = startRestore;

        foreach (var category in catalog.Categories)
        {
            var row = new TweakCategoryRow(category.Title);

            foreach (var tweak in catalog.Toggles.Where(t => t.CategoryId == category.Id && t.AppliesTo(windowsBuild)))
            {
                var toggle = new TweakRow(tweak);
                toggle.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TweakRow.IsSelected))
                    {
                        UpdateSelection();
                    }
                };
                row.Toggles.Add(toggle);
            }

            foreach (var group in catalog.Groups.Where(g => g.CategoryId == category.Id))
            {
                var options = group.Options.Where(o => o.TweakIds.All(id => catalog.Find(id)?.AppliesTo(windowsBuild) == true)).ToList();
                if (options.Count < 2)
                {
                    continue;
                }

                var choices = new List<TweakChoice> { new("Değiştirme", [], keep: true) { IsSelected = true } };
                choices.AddRange(options.Select(o => new TweakChoice(o.Label, o.TweakIds)));

                var groupRow = new TweakGroupRow(group, choices);
                foreach (var choice in groupRow.Choices)
                {
                    choice.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(TweakChoice.IsSelected))
                        {
                            UpdateSelection();
                        }
                    };
                }

                row.Groups.Add(groupRow);
            }

            if (row.Toggles.Count + row.Groups.Count > 0)
            {
                Categories.Add(row);
            }
        }

        UpdateSummaries();
    }

    public ObservableCollection<TweakCategoryRow> Categories { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearch))]
    private string _searchText = "";

    public bool HasSearch => SearchText.Length > 0;

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _selectedCount;

    public string SelectedText => SelectedCount == 0 ? "Seçili ayar yok" : $"{SelectedCount} ayar seçili";

    public bool HasResults => Categories.Any(c => c.HasVisible);

    partial void OnSearchTextChanged(string value)
    {
        var text = value.Trim();
        foreach (var category in Categories)
        {
            foreach (var toggle in category.Toggles)
            {
                toggle.IsMatch = Matches(text, category.Title, toggle.Title, toggle.Description);
            }

            foreach (var group in category.Groups)
            {
                group.IsMatch = Matches(text, category.Title, group.Title, group.Description)
                    || group.Choices.Any(c => c.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase));
            }

            category.HasVisible = category.Toggles.Any(t => t.IsMatch) || category.Groups.Any(g => g.IsMatch);

            // A search opens what it found: nobody searches in order to look at a closed list.
            if (text.Length > 0 && category.HasVisible)
            {
                category.IsExpanded = true;
            }
        }

        OnPropertyChanged(nameof(HasResults));
    }

    private static bool Matches(string text, params string[] fields) =>
        text.Length == 0 || fields.Any(f => f.Contains(text, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Reads how the settings are right now: what already holds is marked, and the ticks are cleared.</summary>
    [RelayCommand]
    public void Refresh()
    {
        foreach (var category in Categories)
        {
            foreach (var toggle in category.Toggles)
            {
                toggle.IsApplied = _fix.IsApplied(toggle.Tweak);
                toggle.IsSelected = false;
            }

            foreach (var group in category.Groups)
            {
                foreach (var choice in group.Choices)
                {
                    choice.IsCurrent = !choice.IsKeep && choice.TweakIds.All(id => _catalog.Find(id) is { } t && _fix.IsApplied(t));
                }

                group.Choices[0].IsSelected = true;
            }
        }

        UpdateSelection();
    }

    /// <summary>The ids of everything the person picked that is not already in place.</summary>
    public IReadOnlyList<string> PickedIds() =>
        Categories
            .SelectMany(c => c.Toggles.Where(t => t.IsSelected && t.CanSelect).Select(t => t.Tweak.Id)
                .Concat(c.Groups.SelectMany(g => g.PickedIds)))
            .ToList();

    private void UpdateSelection()
    {
        SelectedCount = PickedIds().Count;
        UpdateSummaries();
    }

    private void UpdateSummaries()
    {
        foreach (var category in Categories)
        {
            var count = category.Toggles.Count + category.Groups.Count;
            var picked = category.Toggles.Count(t => t.IsSelected && t.CanSelect) + category.Groups.Count(g => g.PickedIds.Any());
            category.Summary = picked > 0 ? $"{count} ayar · {picked} seçili" : $"{count} ayar";
        }
    }

    /// <summary>"Seçilenleri uygula": hands the picks to the fix and starts the usual preview.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync()
    {
        _fix.SelectDefaults([]);
        _fix.Select(PickedIds());
        return _startFlow();
    }

    private bool CanApply() => SelectedCount > 0;

    /// <summary>Clears every tick and choice.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var category in Categories)
        {
            category.ClearAllCommand.Execute(null);
        }
    }

    /// <summary>Ticks every setting FoxFix recommends, in every list.</summary>
    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var category in Categories)
        {
            category.SelectRecommendedCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var category in Categories)
        {
            category.SelectAllCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = false;
        }
    }

    /// <summary>"Geri aç" on one setting: it goes back to the Windows default, through the usual preview and confirmation.</summary>
    [RelayCommand]
    private Task RevertAsync(TweakRow? row)
    {
        if (row is null || !row.CanRevert)
        {
            return Task.CompletedTask;
        }

        _fix.Select([]);
        _fix.SelectDefaults([row.Tweak.Id]);
        return _startFlow();
    }

    /// <summary>
    /// "Windows varsayılanına döndür": everything FoxFix changed (or that holds the changed value) goes back to what Windows has by
    /// default, and every group to its default choice. Through the usual preview and confirmation, and it can be undone.
    /// </summary>
    [RelayCommand]
    private Task ResetToDefaultsAsync()
    {
        _fix.Select(Categories.SelectMany(c => c.Groups).SelectMany(g => g.WindowsDefaultIds));
        _fix.SelectDefaults(Categories.SelectMany(c => c.Toggles).Select(t => t.Tweak.Id));
        return _startFlow();
    }

    // ---------------- the saved settings ----------------

    public ObservableCollection<BackupRow> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    /// <summary>True until the person has saved the settings once: the page then warns in red.</summary>
    public bool NeedsBackup => _backupService is not null && Backups.Count == 0;

    public bool CanBackup => _backupService is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackupStatus))]
    private string _backupStatus = "";

    public bool HasBackupStatus => BackupStatus.Length > 0;

    [ObservableProperty]
    private bool _backupStatusIsError;

    /// <summary>The red pop-up shown when the page is opened and nothing has been saved yet.</summary>
    [ObservableProperty]
    private bool _isBackupPromptOpen;

    /// <summary>The page was opened: reads the saved settings, and warns when there are none.</summary>
    public void OnEntered()
    {
        RefreshBackups();
        IsBackupPromptOpen = NeedsBackup;
    }

    public void RefreshBackups()
    {
        if (_backupStore is null)
        {
            return;
        }

        var listing = _backupStore.List();
        Backups.Clear();
        foreach (var backup in listing.Backups)
        {
            Backups.Add(new BackupRow(backup));
        }

        if (listing.Unreadable.Count > 0)
        {
            SetStatus(Strings.Format(Strings.BackupListProblem, listing.Unreadable.Count, string.Join(", ", listing.Unreadable)), error: true);
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(NeedsBackup));
    }

    private void SetStatus(string text, bool error)
    {
        BackupStatus = text;
        BackupStatusIsError = error;
    }

    /// <summary>"Şu anki ayarları kaydet": keeps how every value the settings could touch is right now.</summary>
    [RelayCommand]
    private void SaveBackup()
    {
        if (_backupService is null)
        {
            return;
        }

        var result = _backupService.Create();
        SetStatus(result.Message, error: !result.Success);
        if (result.Success)
        {
            IsBackupPromptOpen = false;
        }

        RefreshBackups();
    }

    [RelayCommand]
    private void CloseBackupPrompt() => IsBackupPromptOpen = false;

    /// <summary>"Geri yükle": the usual preview first, and what it replaces can be undone too.</summary>
    [RelayCommand]
    private Task RestoreBackupAsync(BackupRow? row)
    {
        if (row is null || _restoreFix is null || _startRestore is null)
        {
            return Task.CompletedTask;
        }

        _restoreFix.Select(row.Id);
        return _startRestore();
    }

    [RelayCommand]
    private void DeleteBackup(BackupRow? row)
    {
        if (row is null || _backupStore is null)
        {
            return;
        }

        if (_backupStore.Delete(row.Id) is { } error)
        {
            SetStatus(Strings.Format(Strings.BackupDeleteFailed, error), error: true);
        }
        else
        {
            SetStatus("Kayıt silindi.", error: false);
        }

        RefreshBackups();
    }
}
