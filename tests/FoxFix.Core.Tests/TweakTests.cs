using System.Globalization;
using FluentAssertions;
using FoxFix.Core.Safety;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.Core.Tests;

internal sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<(RegHive, string, string), (RegKind Kind, string Data)> _values = [];
    private readonly HashSet<(RegHive, string)> _keys = [];

    public List<string> Log { get; } = [];

    /// <summary>Throws when this many writes have happened (counted from 1), to simulate a failure half way.</summary>
    public int FailOnWrite { get; set; }

    private int _writes;

    public void Set(RegHive hive, string key, string name, RegKind kind, string data)
    {
        AddKey(hive, key);
        _values[(hive, key, name)] = (kind, data);
    }

    public void AddKey(RegHive hive, string key)
    {
        var parts = key.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            _keys.Add((hive, string.Join('\\', parts.Take(i))));
        }
    }

    public RegistryValueState Read(RegHive hive, string key, string name) =>
        _values.TryGetValue((hive, key, name), out var v) ? new RegistryValueState(true, v.Kind, v.Data) : RegistryValueState.Missing;

    public bool KeyExists(RegHive hive, string key) => _keys.Contains((hive, key));

    public void Write(RegHive hive, string key, string name, RegKind kind, string data)
    {
        if (FailOnWrite > 0 && ++_writes == FailOnWrite)
        {
            throw new UnauthorizedAccessException("erişim engellendi");
        }

        Log.Add($"write:{key}:{name}");
        Set(hive, key, name, kind, data);
    }

    public void DeleteValue(RegHive hive, string key, string name)
    {
        Log.Add($"delete:{key}:{name}");
        _values.Remove((hive, key, name));
    }

    public bool DeleteKeyIfEmpty(RegHive hive, string key)
    {
        var hasValues = _values.Keys.Any(k => k.Item1 == hive && k.Item2 == key);
        var hasChildren = _keys.Any(k => k.Item1 == hive && k.Item2.StartsWith(key + "\\", StringComparison.Ordinal));
        if (hasValues || hasChildren || !_keys.Contains((hive, key)))
        {
            return false;
        }

        Log.Add($"deletekey:{key}");
        _keys.Remove((hive, key));
        return true;
    }

    /// <summary>Everything in it, in a form two stores can be compared by.</summary>
    public string Dump() =>
        string.Join("\n", _keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2, StringComparer.Ordinal).Select(k => $"K {k.Item1} {k.Item2}"))
        + "\n"
        + string.Join("\n", _values.OrderBy(v => v.Key.Item1).ThenBy(v => v.Key.Item2, StringComparer.Ordinal).ThenBy(v => v.Key.Item3, StringComparer.Ordinal)
            .Select(v => $"V {v.Key.Item1} {v.Key.Item2} [{v.Key.Item3}] {v.Value.Kind} {v.Value.Data}"));
}

public sealed class TweakCatalogTests
{
    [Fact]
    public void The_English_catalog_is_inside_the_program_and_matches_the_Turkish_one_setting_for_setting()
    {
        var turkish = TweakCatalog.Load(Strings.Turkish);
        var english = TweakCatalog.Load(Strings.English);

        english.Tweaks.Select(t => t.Id).Should().Equal(turkish.Tweaks.Select(t => t.Id));
        english.Groups.Select(g => g.Id).Should().Equal(turkish.Groups.Select(g => g.Id));
        english.Categories.Select(c => c.Id).Should().Equal(turkish.Categories.Select(c => c.Id));

        // Only the words differ: what a setting writes into the registry is the same in both languages.
        foreach (var tweak in turkish.Tweaks)
        {
            english.Find(tweak.Id)!.Entries.Should().BeEquivalentTo(tweak.Entries, tweak.Id);
            english.Find(tweak.Id)!.Defaults.Should().BeEquivalentTo(tweak.Defaults, tweak.Id);
            english.Find(tweak.Id)!.Title.Should().NotBeNullOrWhiteSpace();
        }

        english.Tweaks.First().Title.Should().NotBe(turkish.Tweaks.First().Title);
    }

    private static readonly TweakCatalog Catalog = TweakCatalog.Load();

    [Fact]
    public void The_catalog_inside_the_program_loads_with_its_settings_groups_and_categories()
    {
        Catalog.Tweaks.Count.Should().BeGreaterThan(60);
        Catalog.Groups.Should().HaveCount(9);
        Catalog.Categories.Should().HaveCount(12);
        Catalog.Toggles.Count.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Ids_are_unique_and_every_setting_has_a_turkish_title_and_something_to_change()
    {
        Catalog.Tweaks.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        Catalog.Tweaks.Should().OnlyContain(t => t.Title.Length > 3 && t.Entries.Count > 0);
        Catalog.Toggles.Should().OnlyContain(t => t.CategoryId != null && Catalog.Categories.Any(c => c.Id == t.CategoryId));
    }

    [Fact]
    public void Every_entry_is_well_formed()
    {
        foreach (var tweak in Catalog.Tweaks)
        {
            foreach (var e in tweak.Entries)
            {
                var where = $"{tweak.Id}: {e.Key} [{e.Name}]";
                e.Key.Should().NotBeNullOrWhiteSpace(where);
                e.Key.Should().NotStartWith("\\", where).And.NotEndWith("\\", where).And.NotContain("..", where);

                switch (e.Kind)
                {
                    case RegKind.DWord:
                        int.TryParse(e.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out _).Should().BeTrue(where);
                        break;
                    case RegKind.Binary:
                        var act = () => TweakData.TextToBytes(e.Data);
                        act.Should().NotThrow(where);
                        TweakData.BytesToText(TweakData.TextToBytes(e.Data)).Should().Be(e.Data, where);
                        break;
                    case RegKind.Delete:
                        e.Data.Should().BeEmpty(where);
                        break;
                    case RegKind.String:
                        break;
                    default:
                        Assert.Fail($"{where}: type {e.Kind} may not be written");
                        break;
                }
            }
        }
    }

    [Fact]
    public void Groups_point_at_real_settings_and_offer_a_real_choice()
    {
        foreach (var group in Catalog.Groups)
        {
            group.Options.Count.Should().BeGreaterThanOrEqualTo(2, group.Id);
            group.Options.Should().OnlyContain(o => o.TweakIds.Count > 0 && o.Label.Length > 0, group.Id);
            group.Options.SelectMany(o => o.TweakIds).Should().OnlyContain(id => Catalog.Find(id) != null, group.Id);
            Catalog.Categories.Should().Contain(c => c.Id == group.CategoryId, group.Id);
        }
    }

    [Fact]
    public void A_setting_belongs_to_at_most_one_group()
    {
        Catalog.Groups.SelectMany(g => g.Options).SelectMany(o => o.TweakIds).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Nothing_that_starts_programs_or_services_or_logs_in_is_ever_touched_except_the_one_known_AI_service()
    {
        var keys = Catalog.Tweaks.SelectMany(t => t.Entries.Select(e => (t.Id, e.Key)));

        foreach (var (id, key) in keys)
        {
            key.Should().NotContain("\\CurrentVersion\\Run", id);
            key.Should().NotContain("Image File Execution Options", id);
            key.Should().NotContain("Winlogon", id);
            key.Should().NotContain("\\Policies\\Microsoft\\Windows Defender", id);

            if (key.StartsWith("SYSTEM\\CurrentControlSet\\Services", StringComparison.OrdinalIgnoreCase))
            {
                key.Should().Be("SYSTEM\\CurrentControlSet\\Services\\WSAIFabricSvc", id);
            }
        }
    }

    [Theory]
    [InlineData("DisableAnimations")]
    [InlineData("DisableTransparency")]
    [InlineData("DisableDVR")]
    [InlineData("DisableBing")]
    [InlineData("DisableSuggestions")]
    [InlineData("DisableCopilot")]
    [InlineData("DisableTelemetry")]
    public void The_settings_people_asked_for_first_are_there(string id)
    {
        Catalog.Find(id).Should().NotBeNull();
    }

    [Fact]
    public void Build_limits_decide_where_a_setting_applies()
    {
        var copilot = Catalog.Find("DisableCopilot")!;

        copilot.AppliesTo(22000).Should().BeFalse("Copilot settings need Windows 11 22H2 or newer");
        copilot.AppliesTo(22621).Should().BeTrue();
        copilot.AppliesTo(26100).Should().BeTrue();

        var chat = Catalog.Find("HideChat")!;
        chat.AppliesTo(22621).Should().BeTrue();
        chat.AppliesTo(26100).Should().BeFalse();
    }

    [Fact]
    public void A_setting_that_writes_to_the_machine_hive_needs_administrator_rights()
    {
        Catalog.Find("DisableDVR")!.NeedsAdmin.Should().BeTrue();
        Catalog.Find("DisableTransparency")!.NeedsAdmin.Should().BeFalse();
    }

    [Fact]
    public void The_animation_setting_is_stored_as_bytes_and_round_trips()
    {
        var entry = Catalog.Find("DisableAnimations")!.Entries.Single();

        entry.Kind.Should().Be(RegKind.Binary);
        entry.Data.Should().Be("90,12,07,80,10,00,00,00");
        TweakData.TextToBytes(entry.Data).Should().Equal(0x90, 0x12, 0x07, 0x80, 0x10, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void Json_with_unknown_hive_or_kind_is_refused()
    {
        var act = () => TweakCatalog.Parse("""{"tweaks":[{"id":"x","title":"t","entries":[{"hive":"ClassesRoot","key":"k","kind":"dword","data":"1"}]}]}""");

        act.Should().Throw<System.Text.Json.JsonException>();
    }
}

public sealed class WindowsTweakFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-tweaks-" + Guid.NewGuid().ToString("N"));

    private readonly FakeRegistry _registry = new();

    private const string Catalog = """
        {"categories":[{"id":"System","title":"Sistem"}],
         "tweaks":[
           {"id":"Anim","category":"System","title":"Animasyonları kapat","entries":[
              {"hive":"CurrentUser","key":"Control Panel\\Desktop","name":"UserPreferencesMask","kind":"binary","data":"90,12,07,80,10,00,00,00"}]},
           {"id":"Dvr","category":"System","title":"Oyun kaydını kapat","entries":[
              {"hive":"CurrentUser","key":"System\\GameConfigStore","name":"GameDVR_Enabled","kind":"dword","data":"0"},
              {"hive":"LocalMachine","key":"SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR","name":"AllowGameDVR","kind":"dword","data":"0"}]},
           {"id":"Del","category":"System","title":"Değeri kaldır","entries":[
              {"hive":"CurrentUser","key":"Software\\Siuf\\Rules","name":"PeriodInNanoSeconds","kind":"delete","data":""},
              {"hive":"CurrentUser","key":"Software\\Siuf\\Rules","name":"NumberOfSIUFInPeriod","kind":"dword","data":"0"}]},
           {"id":"New","category":"System","title":"Yeni anahtar","entries":[
              {"hive":"CurrentUser","key":"Software\\A\\B\\C","name":"","kind":"string","data":"x"},
              {"hive":"CurrentUser","key":"Software\\A\\B\\C","name":"V","kind":"dword","data":"1"}]},
           {"id":"Future","category":"System","title":"Yeni Windows","minBuild":26200,"entries":[
              {"hive":"CurrentUser","key":"Software\\F","name":"V","kind":"dword","data":"1"}]},
           {"id":"Old","category":"System","title":"Eski Windows","maxBuild":19999,"entries":[
              {"hive":"CurrentUser","key":"Software\\O","name":"V","kind":"dword","data":"1"}]}
         ],
         "groups":[]}
        """;

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private WindowsTweakFix Fix(params string[] ids)
    {
        var fix = new WindowsTweakFix(TweakCatalog.Parse(Catalog), _registry, () => 26100);
        fix.Select(ids);
        return fix;
    }

    private static readonly string Mask = "90,12,07,80,10,00,00,00";

    [Fact]
    public async Task Preview_lists_what_will_change_and_touches_nothing()
    {
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var before = _registry.Dump();

        var preview = await Fix("Anim", "Dvr").PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain("Animasyonları kapat").And.Contain("Oyun kaydını kapat");
        preview.Changes.Should().Contain(l => l.Contains("oturumu kapatıp"));
        preview.Changes.Should().Contain(l => l.Contains("Geri Al"));
        _registry.Dump().Should().Be(before);
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_selected_means_nothing_to_do()
    {
        var preview = await Fix().PreviewAsync(CancellationToken.None);
        var result = await Fix().ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_id_is_ignored()
    {
        var result = await Fix("YokBoyleBirSey").ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_writes_the_values_and_saves_the_old_ones_in_the_journal_first()
    {
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var journal = Journal;
        IReadOnlyList<UndoRecord>? seen = null;
        // The first write happens only after the journal holds the old value.
        var spy = new SpyRegistry(_registry, async () => seen ??= await journal.LoadAsync(CancellationToken.None));
        var guarded = new WindowsTweakFix(TweakCatalog.Parse(Catalog), spy, () => 26100);
        guarded.Select(["Anim"]);

        var result = await guarded.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("1 ayar uygulandı");
        _registry.Read(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask").Data.Should().Be(Mask);
        seen.Should().ContainSingle().Which.Data["values"].Should().Contain("9e,1e,07,80,12,00,00,00");
        (await journal.LoadAsync(CancellationToken.None)).Single().FixId.Should().Be(WindowsTweakFix.FixIdValue);
    }

    private sealed class SpyRegistry(FakeRegistry inner, Func<Task> beforeFirstWrite) : IRegistryStore
    {
        private bool _done;

        public RegistryValueState Read(RegHive hive, string key, string name) => inner.Read(hive, key, name);

        public bool KeyExists(RegHive hive, string key) => inner.KeyExists(hive, key);

        public void Write(RegHive hive, string key, string name, RegKind kind, string data)
        {
            if (!_done)
            {
                _done = true;
                beforeFirstWrite().GetAwaiter().GetResult();
            }

            inner.Write(hive, key, name, kind, data);
        }

        public void DeleteValue(RegHive hive, string key, string name) => inner.DeleteValue(hive, key, name);

        public bool DeleteKeyIfEmpty(RegHive hive, string key) => inner.DeleteKeyIfEmpty(hive, key);
    }

    [Fact]
    public async Task Running_twice_changes_nothing_the_second_time()
    {
        var fix = Fix("Anim", "Dvr", "Del");
        await fix.ApplyAsync(Journal, CancellationToken.None);
        _registry.Log.Clear();

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var second = await fix.ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        second.Success.Should().BeTrue();
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Settings_for_another_windows_build_are_left_out()
    {
        var fix = Fix("Future", "Old");

        var preview = await fix.PreviewAsync(CancellationToken.None);
        await fix.ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public void Administrator_rights_are_asked_for_only_when_a_machine_value_will_change()
    {
        Fix("Anim").RequiresAdmin.Should().BeFalse();
        Fix("Anim", "Dvr").RequiresAdmin.Should().BeTrue();

        // A machine setting that already holds does not need them.
        _registry.Set(RegHive.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR", "AllowGameDVR", RegKind.DWord, "0");
        _registry.Set(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", RegKind.DWord, "0");
        Fix("Dvr").RequiresAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task A_value_to_remove_is_removed_and_comes_back_with_Geri_Al()
    {
        _registry.Set(RegHive.CurrentUser, "Software\\Siuf\\Rules", "PeriodInNanoSeconds", RegKind.DWord, "5");
        var journal = Journal;
        var fix = Fix("Del");

        await fix.ApplyAsync(journal, CancellationToken.None);
        _registry.Read(RegHive.CurrentUser, "Software\\Siuf\\Rules", "PeriodInNanoSeconds").Exists.Should().BeFalse();

        await fix.UndoAsync((await journal.LoadAsync(CancellationToken.None)).Single(), CancellationToken.None);
        _registry.Read(RegHive.CurrentUser, "Software\\Siuf\\Rules", "PeriodInNanoSeconds").Should().Be(new RegistryValueState(true, RegKind.DWord, "5"));
        _registry.Read(RegHive.CurrentUser, "Software\\Siuf\\Rules", "NumberOfSIUFInPeriod").Exists.Should().BeFalse("it was not there before");
    }

    [Fact]
    public async Task Undo_puts_back_old_values_removes_new_ones_and_deletes_only_the_keys_it_made()
    {
        _registry.AddKey(RegHive.CurrentUser, "Software\\A");
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var before = _registry.Dump();
        var journal = Journal;
        var fix = Fix("Anim", "New");

        await fix.ApplyAsync(journal, CancellationToken.None);
        _registry.Dump().Should().NotBe(before);
        _registry.KeyExists(RegHive.CurrentUser, "Software\\A\\B\\C").Should().BeTrue();

        foreach (var record in (await journal.LoadAsync(CancellationToken.None)).Reverse())
        {
            await fix.UndoAsync(record, CancellationToken.None);
        }

        _registry.Dump().Should().Be(before);
        _registry.KeyExists(RegHive.CurrentUser, "Software\\A").Should().BeTrue("it existed before and is not ours to remove");
    }

    [Fact]
    public async Task A_key_that_gained_other_values_since_is_not_removed_by_undo()
    {
        var journal = Journal;
        var fix = Fix("New");
        await fix.ApplyAsync(journal, CancellationToken.None);
        _registry.Set(RegHive.CurrentUser, "Software\\A\\B\\C", "Baska", RegKind.DWord, "7");

        await fix.UndoAsync((await journal.LoadAsync(CancellationToken.None)).Single(), CancellationToken.None);

        _registry.Read(RegHive.CurrentUser, "Software\\A\\B\\C", "Baska").Exists.Should().BeTrue();
        _registry.KeyExists(RegHive.CurrentUser, "Software\\A\\B\\C").Should().BeTrue();
    }

    [Fact]
    public async Task A_setting_that_fails_half_way_is_put_back_and_its_journal_entry_withdrawn()
    {
        _registry.Set(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", RegKind.DWord, "1");
        _registry.FailOnWrite = 2;
        var journal = Journal;
        var before = _registry.Dump();

        var result = await Fix("Dvr").ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("[FX-4009]").And.Contain("Oyun kaydını kapat").And.Contain("geri alındı");
        _registry.Dump().Should().Be(before);
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task One_failing_setting_does_not_stop_the_others()
    {
        _registry.FailOnWrite = 1;

        var result = await Fix("Anim", "Del").ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("1 ayar uygulandı, 1 ayarda hata oldu");
        _registry.Read(RegHive.CurrentUser, "Software\\Siuf\\Rules", "NumberOfSIUFInPeriod").Exists.Should().BeTrue();
    }

    [Fact]
    public async Task A_value_of_a_type_FoxFix_cannot_write_back_is_not_touched()
    {
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Other, "");
        var journal = Journal;

        var result = await Fix("Anim").ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("[FX-4009]");
        _registry.Log.Should().BeEmpty();
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_damaged_journal_entry_is_reported_on_undo()
    {
        var fix = Fix("Anim");
        var broken = new UndoRecord(Guid.NewGuid(), WindowsTweakFix.FixIdValue, DateTimeOffset.UtcNow, "x", new Dictionary<string, string> { ["values"] = "bozuk", ["createdKeys"] = "[]" });

        var act = () => fix.UndoAsync(broken, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Undo_failing_to_write_back_says_so()
    {
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var journal = Journal;
        var fix = Fix("Anim");
        await fix.ApplyAsync(journal, CancellationToken.None);
        _registry.FailOnWrite = 1;
        _registry.Log.Clear();

        var act = async () => await fix.UndoAsync((await journal.LoadAsync(CancellationToken.None)).Single(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

public sealed class WholeCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-tweaks-all-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Every_setting_applies_cleanly_and_Geri_Al_leaves_the_registry_exactly_as_it_was()
    {
        var catalog = TweakCatalog.Load();

        foreach (var tweak in catalog.Tweaks)
        {
            var registry = new FakeRegistry();

            // A registry that already has some of the values (as a real one has), so both "was there" and "was not" are exercised.
            foreach (var entry in tweak.Entries.Where((_, i) => i % 2 == 0))
            {
                registry.Set(entry.Hive, entry.Key, entry.Name, RegKind.String, "eski");
            }

            var before = registry.Dump();
            var journal = new UndoJournal(Path.Combine(_dir, tweak.Id + ".json"));
            var fix = new WindowsTweakFix(catalog, registry, () => 99999);
            fix.Select([tweak.Id]);

            // Build limits do not matter here: the build is made to fit the setting.
            var build = tweak.MinBuild > 0 ? tweak.MinBuild : tweak.MaxBuild > 0 ? tweak.MaxBuild : 26100;
            fix = new WindowsTweakFix(catalog, registry, () => build);
            fix.Select([tweak.Id]);

            var result = await fix.ApplyAsync(journal, CancellationToken.None);

            result.Success.Should().BeTrue(tweak.Id);
            fix.IsApplied(tweak).Should().BeTrue($"{tweak.Id} must hold after it was applied");

            foreach (var record in (await journal.LoadAsync(CancellationToken.None)).Reverse())
            {
                await fix.UndoAsync(record, CancellationToken.None);
            }

            registry.Dump().Should().Be(before, $"{tweak.Id} must be fully undone");
        }
    }
}
