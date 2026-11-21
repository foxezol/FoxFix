using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Safety;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class TweaksPresetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-presets-" + Guid.NewGuid().ToString("N"));

    private readonly Registry _registry = new();

    private int _applyStarted;
    private int _restoreStarted;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class Registry : IRegistryStore
    {
        private readonly Dictionary<(RegHive, string, string), (RegKind, string)> _values = [];

        public void Set(RegHive hive, string key, string name, RegKind kind, string data) => _values[(hive, key, name)] = (kind, data);

        public RegistryValueState Read(RegHive hive, string key, string name) =>
            _values.TryGetValue((hive, key, name), out var v) ? new RegistryValueState(true, v.Item1, v.Item2) : RegistryValueState.Missing;

        public bool KeyExists(RegHive hive, string key) =>
            _values.Keys.Any(k => k.Item1 == hive && (k.Item2 == key || k.Item2.StartsWith(key + "\\", StringComparison.Ordinal)));

        public void Write(RegHive hive, string key, string name, RegKind kind, string data) => Set(hive, key, name, kind, data);

        public void DeleteValue(RegHive hive, string key, string name) => _values.Remove((hive, key, name));

        public bool DeleteKeyIfEmpty(RegHive hive, string key) => false;
    }

    private static readonly TweakCatalog Catalog = TweakCatalog.Load();

    private TweakBackupStore Store => new(Path.Combine(_dir, "yedekler"));

    private (TweaksViewModel Vm, WindowsTweakFix Fix, TweakRestoreFix Restore) Create(bool withBackups = true)
    {
        var fix = new WindowsTweakFix(Catalog, _registry, () => 26100);
        var restore = new TweakRestoreFix(Store, _registry);
        var service = new TweakBackupService(Catalog, _registry, Store, () => 26100);
        var vm = withBackups
            ? new TweaksViewModel(Catalog, fix, 26100, () => { _applyStarted++; return Task.CompletedTask; }, service, Store, restore, () => { _restoreStarted++; return Task.CompletedTask; })
            : new TweaksViewModel(Catalog, fix, 26100, () => { _applyStarted++; return Task.CompletedTask; });
        vm.Refresh();
        return (vm, fix, restore);
    }

    private static TweakRow Toggle(TweaksViewModel vm, string id) => vm.Categories.SelectMany(c => c.Toggles).Single(t => t.Tweak.Id == id);

    // ---------------- impact and recommendation ----------------

    [Fact]
    public void A_setting_shows_what_it_is_about_and_a_rough_estimate()
    {
        var (vm, _, _) = Create();

        var dvr = Toggle(vm, "DisableDVR");

        dvr.ImpactText.Should().Be("Hız · tahmini etki %15");
        dvr.IsRecommended.Should().BeTrue();
        dvr.HasInfo.Should().BeTrue();
    }

    [Theory]
    [InlineData("DisableTelemetry", "High")]
    [InlineData("DisableDVR", "Medium")]
    [InlineData("DisableCopilot", "Low")]
    public void The_colour_level_follows_the_percentage(string id, string level)
    {
        var (vm, _, _) = Create();

        Toggle(vm, id).ImpactLevel.Should().Be(level);
    }

    [Fact]
    public void Every_stand_alone_setting_on_the_page_has_an_estimate()
    {
        var (vm, _, _) = Create();

        vm.Categories.SelectMany(c => c.Toggles).Should().OnlyContain(t => t.HasInfo);
    }

    [Fact]
    public void The_impact_badge_says_the_level_in_words_so_colour_is_never_the_only_signal()
    {
        var (vm, _, _) = Create();

        foreach (var row in vm.Categories.SelectMany(c => c.Toggles))
        {
            var word = row.ImpactLevel switch { "High" => "Yüksek", "Medium" => "Orta", _ => "Düşük" };
            row.ImpactBadgeText.Should().Be($"{word} etki %{row.Info!.Percent}");
            row.AreaName.Should().Be(row.Info.AreaName);
        }
    }

    [Fact]
    public void A_list_says_whether_it_has_single_settings_for_its_recommended_button()
    {
        // The view shows "Bu listedeki önerilenleri seç" by this flag (a count bound to a yes/no converter never showed it).
        var (vm, _, _) = Create();

        vm.Categories.Should().OnlyContain(c => c.HasToggles == (c.Toggles.Count > 0));
        vm.Categories.Should().Contain(c => c.HasToggles);

        var row = new TweakCategoryRow("deneme");
        var raised = false;
        row.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(TweakCategoryRow.HasToggles);
        row.HasToggles.Should().BeFalse();
        row.Toggles.Add(vm.Categories.First(c => c.HasToggles).Toggles[0]);
        row.HasToggles.Should().BeTrue();
        raised.Should().BeTrue();
    }

    // ---------------- ready-made picks ----------------

    [Fact]
    public void The_recommended_button_ticks_exactly_the_recommended_settings_and_changes_nothing()
    {
        var (vm, _, _) = Create();

        vm.SelectRecommendedCommand.Execute(null);

        var ticked = vm.Categories.SelectMany(c => c.Toggles).Where(t => t.IsSelected).Select(t => t.Tweak.Id).ToList();
        ticked.Should().NotBeEmpty();
        ticked.Should().OnlyContain(id => TweakInfos.IsRecommended(id));
        vm.Categories.SelectMany(c => c.Toggles).Where(t => TweakInfos.IsRecommended(t.Tweak.Id)).Should().OnlyContain(t => t.IsSelected);
        vm.SelectedCount.Should().Be(ticked.Count);
        _applyStarted.Should().Be(0);
    }

    [Fact]
    public void Select_all_ticks_every_stand_alone_setting_that_is_not_already_in_place()
    {
        _registry.Set(RegHive.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\\TaskbarDeveloperSettings", "TaskbarEndTask", RegKind.DWord, "1");
        var (vm, _, _) = Create();

        vm.SelectAllCommand.Execute(null);

        var toggles = vm.Categories.SelectMany(c => c.Toggles).ToList();
        toggles.Where(t => t.CanSelect).Should().OnlyContain(t => t.IsSelected);
        toggles.Where(t => !t.CanSelect).Should().OnlyContain(t => !t.IsSelected);
    }

    [Fact]
    public void A_list_has_its_own_recommended_all_and_clear_buttons()
    {
        var (vm, _, _) = Create();
        var list = vm.Categories.First(c => c.Toggles.Count > 3);

        list.SelectRecommendedCommand.Execute(null);
        list.Toggles.Where(t => t.IsSelected).Should().OnlyContain(t => t.IsRecommended);
        vm.Categories.Where(c => c != list).SelectMany(c => c.Toggles).Should().OnlyContain(t => !t.IsSelected);

        list.SelectAllCommand.Execute(null);
        list.Toggles.Where(t => t.CanSelect).Should().OnlyContain(t => t.IsSelected);

        list.ClearAllCommand.Execute(null);
        list.Toggles.Should().OnlyContain(t => !t.IsSelected);
        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void A_list_button_only_touches_what_the_search_shows()
    {
        var (vm, _, _) = Create();
        vm.SearchText = "telemetri";
        var list = vm.Categories.First(c => c.Toggles.Any(t => t.IsMatch));

        list.SelectAllCommand.Execute(null);

        list.Toggles.Where(t => t.IsSelected).Should().OnlyContain(t => t.IsMatch);
        list.Toggles.Any(t => !t.IsMatch && t.IsSelected).Should().BeFalse();
    }

    [Fact]
    public void The_summary_of_a_list_counts_its_settings_and_what_is_ticked()
    {
        var (vm, _, _) = Create();
        var list = vm.Categories.First(c => c.Toggles.Count > 3);
        var total = list.Toggles.Count + list.Groups.Count;

        list.Summary.Should().Be($"{total} ayar");

        list.Toggles[0].IsSelected = true;
        list.Summary.Should().Be($"{total} ayar · 1 seçili");
    }

    // ---------------- folding lists ----------------

    [Fact]
    public void Every_list_starts_closed_and_opens_and_closes_by_its_header()
    {
        var (vm, _, _) = Create();

        vm.Categories.Should().OnlyContain(c => !c.IsExpanded);

        vm.Categories[0].ToggleExpandedCommand.Execute(null);
        vm.Categories[0].IsExpanded.Should().BeTrue();
        vm.Categories[0].ToggleExpandedCommand.Execute(null);
        vm.Categories[0].IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Open_all_and_close_all()
    {
        var (vm, _, _) = Create();

        vm.ExpandAllCommand.Execute(null);
        vm.Categories.Should().OnlyContain(c => c.IsExpanded);

        vm.CollapseAllCommand.Execute(null);
        vm.Categories.Should().OnlyContain(c => !c.IsExpanded);
    }

    [Fact]
    public void A_search_opens_the_lists_it_found_something_in()
    {
        var (vm, _, _) = Create();

        vm.SearchText = "telemetri";

        vm.Categories.Where(c => c.HasVisible).Should().OnlyContain(c => c.IsExpanded);
        vm.Categories.Where(c => !c.HasVisible).Should().OnlyContain(c => !c.IsExpanded);
    }

    // ---------------- Windows default ----------------

    [Fact]
    public async Task Back_to_the_Windows_default_hands_over_the_settings_and_the_default_choice_of_each_group()
    {
        var (vm, fix, _) = Create();
        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);
        vm.Refresh();

        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        _applyStarted.Should().Be(1);
        var preview = await fix.PreviewAsync(CancellationToken.None);
        preview.Changes.Should().Contain(l => l.StartsWith("Windows varsayılanına dönecek:") && l.Contains("oyun"));
    }

    [Fact]
    public async Task Applying_after_a_default_reset_does_not_carry_the_reset_along()
    {
        var (vm, fix, _) = Create();
        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);
        vm.Refresh();
        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        Toggle(vm, "DisableTransparency").IsSelected = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        var preview = await fix.PreviewAsync(CancellationToken.None);
        preview.Changes.Should().NotContain(l => l.StartsWith("Windows varsayılanına dönecek:"));
        preview.Changes.Should().Contain(l => l.Contains("Saydamlık"));
    }

    // ---------------- switching one back on ----------------

    [Fact]
    public async Task An_applied_setting_with_a_known_default_can_be_switched_back_and_others_cannot()
    {
        var (vm, fix, _) = Create();
        Toggle(vm, "DisableDVR").CanRevert.Should().BeFalse("it does not hold yet");

        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);
        vm.Refresh();

        Toggle(vm, "DisableDVR").CanRevert.Should().BeTrue();
        Toggle(vm, "DisableTransparency").CanRevert.Should().BeFalse();
    }

    [Fact]
    public async Task Switching_one_back_on_previews_only_that_one_and_goes_through_the_usual_flow()
    {
        var (vm, fix, _) = Create();
        fix.Select(["DisableDVR", "DisableTransparency"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);
        vm.Refresh();

        await vm.RevertCommand.ExecuteAsync(Toggle(vm, "DisableDVR"));

        _applyStarted.Should().Be(1);
        var preview = await fix.PreviewAsync(CancellationToken.None);
        preview.Changes.Should().Contain(l => l.StartsWith("Windows varsayılanına dönecek:") && l.Contains("oyun"));
        preview.Changes.Should().NotContain(l => l.Contains("Saydamlık"));
    }

    [Fact]
    public async Task Switching_back_a_setting_that_does_not_hold_does_nothing()
    {
        var (vm, _, _) = Create();

        await vm.RevertCommand.ExecuteAsync(Toggle(vm, "DisableDVR"));
        await vm.RevertCommand.ExecuteAsync(null);

        _applyStarted.Should().Be(0);
    }

    [Fact]
    public async Task Switching_back_really_restores_the_default_in_the_registry()
    {
        var (vm, fix, _) = Create();
        var journal = new UndoJournal(Path.Combine(_dir, "undo.json"));
        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(journal, CancellationToken.None);
        vm.Refresh();
        await vm.RevertCommand.ExecuteAsync(Toggle(vm, "DisableDVR"));

        await fix.ApplyAsync(journal, CancellationToken.None);
        vm.Refresh();

        Toggle(vm, "DisableDVR").IsApplied.Should().BeFalse();
        Toggle(vm, "DisableDVR").CanSelect.Should().BeTrue("it can be applied again");
        _registry.Read(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled").Data.Should().Be("1");
    }
    // ---------------- saved settings ----------------

    [Fact]
    public void Without_anything_saved_the_page_warns()
    {
        var (vm, _, _) = Create();

        vm.OnEntered();

        vm.NeedsBackup.Should().BeTrue();
        vm.HasBackups.Should().BeFalse();
        vm.IsBackupPromptOpen.Should().BeTrue("the red pop-up appears on entering");
    }

    [Fact]
    public void With_something_saved_there_is_no_pop_up_and_no_warning()
    {
        var (vm, _, _) = Create();
        vm.SaveBackupCommand.Execute(null);

        vm.OnEntered();

        vm.NeedsBackup.Should().BeFalse();
        vm.HasBackups.Should().BeTrue();
        vm.IsBackupPromptOpen.Should().BeFalse();
    }

    [Fact]
    public void Saving_lists_the_backup_closes_the_pop_up_and_says_so()
    {
        var (vm, _, _) = Create();
        vm.OnEntered();

        vm.SaveBackupCommand.Execute(null);

        vm.Backups.Should().ContainSingle();
        vm.Backups[0].Name.Should().StartWith("Ayarlarım");
        vm.Backups[0].DateText.Should().MatchRegex(@"^\d\d\.\d\d\.\d{4} \d\d:\d\d$");
        vm.IsBackupPromptOpen.Should().BeFalse();
        vm.BackupStatus.Should().Contain("kaydedildi");
        vm.BackupStatusIsError.Should().BeFalse();
    }

    [Fact]
    public void Later_closes_the_pop_up_but_the_red_warning_stays_on_the_page()
    {
        var (vm, _, _) = Create();
        vm.OnEntered();

        vm.CloseBackupPromptCommand.Execute(null);

        vm.IsBackupPromptOpen.Should().BeFalse();
        vm.NeedsBackup.Should().BeTrue();
    }

    [Fact]
    public void A_backup_that_cannot_be_written_is_said_in_red_with_its_code()
    {
        var blocker = _dir + "-dosya";
        File.WriteAllText(blocker, "x");
        var fix = new WindowsTweakFix(Catalog, _registry, () => 26100);
        var brokenStore = new TweakBackupStore(Path.Combine(blocker, "alt"));
        var vm = new TweaksViewModel(Catalog, fix, 26100, () => Task.CompletedTask,
            new TweakBackupService(Catalog, _registry, brokenStore, () => 26100), brokenStore, new TweakRestoreFix(brokenStore, _registry), () => Task.CompletedTask);

        vm.SaveBackupCommand.Execute(null);
        File.Delete(blocker);

        vm.BackupStatus.Should().StartWith("[FX-4010]");
        vm.BackupStatusIsError.Should().BeTrue();
        vm.Backups.Should().BeEmpty();
    }

    [Fact]
    public void A_saved_file_that_cannot_be_read_is_reported_with_its_code()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "yedekler"));
        File.WriteAllText(Path.Combine(_dir, "yedekler", "yedek-bozuk.json"), "{ bozuk");
        var (vm, _, _) = Create();

        vm.OnEntered();

        vm.BackupStatus.Should().StartWith("[FX-4012]").And.Contain("yedek-bozuk.json");
        vm.BackupStatusIsError.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_hands_the_chosen_backup_to_the_fix_and_starts_the_preview()
    {
        var (vm, _, restore) = Create();
        vm.SaveBackupCommand.Execute(null);
        var fix = new WindowsTweakFix(Catalog, _registry, () => 26100);
        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        await vm.RestoreBackupCommand.ExecuteAsync(vm.Backups[0]);

        _restoreStarted.Should().Be(1);
        var preview = await restore.PreviewAsync(CancellationToken.None);
        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(l => l.Contains(vm.Backups[0].Name));
    }

    [Fact]
    public async Task Restore_without_a_row_does_nothing()
    {
        var (vm, _, _) = Create();

        await vm.RestoreBackupCommand.ExecuteAsync(null);

        _restoreStarted.Should().Be(0);
    }

    [Fact]
    public void Delete_removes_the_backup_from_the_list_and_the_disk()
    {
        var (vm, _, _) = Create();
        vm.SaveBackupCommand.Execute(null);
        var row = vm.Backups[0];

        vm.DeleteBackupCommand.Execute(row);

        vm.Backups.Should().BeEmpty();
        vm.NeedsBackup.Should().BeTrue();
        vm.BackupStatus.Should().Contain("silindi");
        Store.Find(row.Id).Should().BeNull();
    }

    [Fact]
    public void Without_a_backup_service_nothing_warns_and_nothing_breaks()
    {
        var (vm, _, _) = Create(withBackups: false);

        vm.OnEntered();
        vm.SaveBackupCommand.Execute(null);
        vm.RestoreBackupCommand.Execute(null);

        vm.NeedsBackup.Should().BeFalse();
        vm.IsBackupPromptOpen.Should().BeFalse();
        vm.CanBackup.Should().BeFalse();
    }
}
