using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Safety;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class TweaksViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-tweaks-vm-" + Guid.NewGuid().ToString("N"));

    private readonly Registry _registry = new();

    private int _started;

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

        public bool KeyExists(RegHive hive, string key) => _values.Keys.Any(k => k.Item1 == hive && (k.Item2 == key || k.Item2.StartsWith(key + "\\", StringComparison.Ordinal)));

        public void Write(RegHive hive, string key, string name, RegKind kind, string data) => Set(hive, key, name, kind, data);

        public void DeleteValue(RegHive hive, string key, string name) => _values.Remove((hive, key, name));

        public bool DeleteKeyIfEmpty(RegHive hive, string key) => false;
    }

    private const string Json = """
        {"categories":[{"id":"System","title":"Sistem"},{"id":"Taskbar","title":"Görev çubuğu"},{"id":"Empty","title":"Boş"}],
         "tweaks":[
           {"id":"Anim","category":"System","title":"Animasyonları kapat","description":"Pencereler hızlı açılır.","entries":[
              {"hive":"CurrentUser","key":"A","name":"V","kind":"dword","data":"0"}]},
           {"id":"Trans","category":"System","title":"Saydamlığı kapat","entries":[
              {"hive":"CurrentUser","key":"B","name":"V","kind":"dword","data":"0"}]},
           {"id":"Win11Only","category":"System","title":"Yalnızca yeni Windows","minBuild":26200,"entries":[
              {"hive":"CurrentUser","key":"C","name":"V","kind":"dword","data":"0"}]},
           {"id":"Left","category":"Taskbar","title":"Sola","entries":[{"hive":"CurrentUser","key":"T","name":"Al","kind":"dword","data":"0"}]},
           {"id":"Center","category":"Taskbar","title":"Ortaya","entries":[{"hive":"CurrentUser","key":"T","name":"Al","kind":"dword","data":"1"}]}
         ],
         "groups":[
           {"id":"Align","category":"Taskbar","title":"Hizalama","description":"Düğmelerin yeri","options":[
              {"label":"Ortada (Varsayılan)","tweakIds":["Center"]},{"label":"Solda","tweakIds":["Left"]}]}
         ]}
        """;

    private (TweaksViewModel Vm, WindowsTweakFix Fix) Create(int build = 26100)
    {
        var catalog = TweakCatalog.Parse(Json);
        var fix = new WindowsTweakFix(catalog, _registry, () => build);
        var vm = new TweaksViewModel(catalog, fix, build, () =>
        {
            _started++;
            return Task.CompletedTask;
        });
        vm.Refresh();
        return (vm, fix);
    }

    private static TweakRow Toggle(TweaksViewModel vm, string id) =>
        vm.Categories.SelectMany(c => c.Toggles).Single(t => t.Tweak.Id == id);

    private static TweakGroupRow Group(TweaksViewModel vm) => vm.Categories.SelectMany(c => c.Groups).Single();

    [Fact]
    public void Categories_with_nothing_to_show_and_settings_for_other_builds_are_left_out()
    {
        var (vm, _) = Create(build: 26100);

        vm.Categories.Select(c => c.Title).Should().Equal("Sistem", "Görev çubuğu");
        vm.Categories.SelectMany(c => c.Toggles).Select(t => t.Tweak.Id).Should().Equal("Anim", "Trans");
    }

    [Fact]
    public void A_newer_windows_shows_its_own_settings()
    {
        var (vm, _) = Create(build: 26200);

        vm.Categories.SelectMany(c => c.Toggles).Select(t => t.Tweak.Id).Should().Contain("Win11Only");
    }

    [Fact]
    public void A_group_not_offered_by_this_windows_is_left_out()
    {
        var catalog = TweakCatalog.Parse(Json.Replace("\"id\":\"Left\",\"category\":\"Taskbar\"", "\"id\":\"Left\",\"category\":\"Taskbar\",\"minBuild\":30000"));
        var fix = new WindowsTweakFix(catalog, _registry, () => 26100);

        var vm = new TweaksViewModel(catalog, fix, 26100, () => Task.CompletedTask);

        vm.Categories.SelectMany(c => c.Groups).Should().BeEmpty("one choice is not a choice");
    }

    [Fact]
    public void Nothing_is_ticked_at_first_and_applying_is_off()
    {
        var (vm, _) = Create();

        vm.SelectedCount.Should().Be(0);
        vm.SelectedText.Should().Be("Seçili ayar yok");
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Ticking_counts_and_switches_applying_on()
    {
        var (vm, _) = Create();

        Toggle(vm, "Anim").IsSelected = true;
        Toggle(vm, "Trans").IsSelected = true;

        vm.SelectedCount.Should().Be(2);
        vm.SelectedText.Should().Be("2 ayar seçili");
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void A_setting_that_already_holds_is_marked_and_cannot_be_ticked()
    {
        _registry.Set(RegHive.CurrentUser, "A", "V", RegKind.DWord, "0");

        var (vm, _) = Create();

        Toggle(vm, "Anim").IsApplied.Should().BeTrue();
        Toggle(vm, "Anim").CanSelect.Should().BeFalse();
        Toggle(vm, "Trans").IsApplied.Should().BeFalse();
    }

    [Fact]
    public void Even_if_ticked_an_applied_setting_is_not_counted_or_sent()
    {
        _registry.Set(RegHive.CurrentUser, "A", "V", RegKind.DWord, "0");
        var (vm, _) = Create();

        Toggle(vm, "Anim").IsSelected = true;

        vm.PickedIds().Should().BeEmpty();
        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void A_group_starts_on_leave_it_as_it_is_and_marks_the_choice_that_holds_now()
    {
        _registry.Set(RegHive.CurrentUser, "T", "Al", RegKind.DWord, "1");

        var (vm, _) = Create();

        var group = Group(vm);
        group.Choices.Select(c => c.Label).Should().Equal("Değiştirme", "Ortada (Varsayılan)", "Solda");
        group.Choices[0].IsSelected.Should().BeTrue();
        group.Choices.Single(c => c.IsCurrent).Label.Should().Be("Ortada (Varsayılan)");
        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void One_choice_of_a_group_at_a_time()
    {
        var (vm, _) = Create();
        var group = Group(vm);

        group.Choices[2].IsSelected = true;

        group.Choices.Count(c => c.IsSelected).Should().Be(1);
        group.Choices[2].IsSelected.Should().BeTrue();
        vm.PickedIds().Should().Equal("Left");

        group.Choices[1].IsSelected = true;
        group.Choices.Count(c => c.IsSelected).Should().Be(1);
        vm.PickedIds().Should().Equal("Center");
    }

    [Fact]
    public void Picking_the_choice_that_already_holds_changes_nothing()
    {
        _registry.Set(RegHive.CurrentUser, "T", "Al", RegKind.DWord, "1");
        var (vm, _) = Create();

        Group(vm).Choices[1].IsSelected = true;

        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public async Task Apply_hands_the_picks_to_the_fix_and_starts_the_preview()
    {
        var (vm, fix) = Create();
        Toggle(vm, "Trans").IsSelected = true;
        Group(vm).Choices[2].IsSelected = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _started.Should().Be(1);
        var preview = await fix.PreviewAsync(CancellationToken.None);
        preview.Changes.Should().Contain("Saydamlığı kapat").And.Contain("Sola");
        preview.Changes.Should().NotContain("Animasyonları kapat");
    }

    [Fact]
    public async Task Nothing_is_changed_by_ticking_or_by_the_preview()
    {
        var (vm, fix) = Create();
        Toggle(vm, "Anim").IsSelected = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        await fix.PreviewAsync(CancellationToken.None);

        _registry.Read(RegHive.CurrentUser, "A", "V").Exists.Should().BeFalse();
    }

    [Fact]
    public async Task After_the_changes_Refresh_marks_them_and_clears_the_ticks()
    {
        var (vm, fix) = Create();
        Toggle(vm, "Anim").IsSelected = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        vm.Refresh();

        Toggle(vm, "Anim").IsApplied.Should().BeTrue();
        Toggle(vm, "Anim").IsSelected.Should().BeFalse();
        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void Clear_selection_unticks_everything_and_returns_groups_to_leave_as_is()
    {
        var (vm, _) = Create();
        Toggle(vm, "Anim").IsSelected = true;
        Group(vm).Choices[2].IsSelected = true;

        vm.ClearSelectionCommand.Execute(null);

        vm.SelectedCount.Should().Be(0);
        Group(vm).Choices[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Search_finds_by_title_description_category_and_group_choice()
    {
        var (vm, _) = Create();

        vm.SearchText = "hızlı";
        Toggle(vm, "Anim").IsMatch.Should().BeTrue("the description says so");
        Toggle(vm, "Trans").IsMatch.Should().BeFalse();
        vm.Categories.Single(c => c.Title == "Sistem").HasVisible.Should().BeTrue();
        vm.Categories.Single(c => c.Title == "Görev çubuğu").HasVisible.Should().BeFalse();

        vm.SearchText = "solda";
        Group(vm).IsMatch.Should().BeTrue();
        vm.Categories.Single(c => c.Title == "Görev çubuğu").HasVisible.Should().BeTrue();

        vm.SearchText = "görev çubuğu";
        Group(vm).IsMatch.Should().BeTrue("the whole category matches");
    }

    [Fact]
    public void An_empty_search_shows_everything_and_a_hopeless_one_says_so()
    {
        var (vm, _) = Create();

        vm.SearchText = "hiçbir şey bu değil xyz";
        vm.HasResults.Should().BeFalse();

        vm.SearchText = "  ";
        vm.HasResults.Should().BeTrue();
        vm.Categories.Should().OnlyContain(c => c.HasVisible);
    }

    [Fact]
    public void The_real_catalog_gives_a_page_for_this_windows()
    {
        var catalog = TweakCatalog.Load();
        var fix = new WindowsTweakFix(catalog, new Registry(), () => 26100);

        var vm = new TweaksViewModel(catalog, fix, 26100, () => Task.CompletedTask);
        vm.Refresh();

        vm.Categories.Count.Should().BeGreaterThan(5);
        vm.Categories.SelectMany(c => c.Toggles).Count().Should().BeGreaterThan(40);
        vm.Categories.SelectMany(c => c.Groups).Count().Should().BeGreaterThan(5);
    }
}
