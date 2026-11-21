using FluentAssertions;
using FoxFix.Core.Safety;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class TweakInfoTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.Load();

    [Fact]
    public void Every_stand_alone_setting_has_an_area_and_an_estimate()
    {
        foreach (var tweak in Catalog.Toggles)
        {
            TweakInfos.For(tweak.Id).Should().NotBeNull($"{tweak.Id} needs an entry in TweakInfos");
        }
    }

    [Fact]
    public void No_entry_belongs_to_a_setting_that_does_not_exist()
    {
        TweakInfos.Ids.Should().OnlyContain(id => Catalog.Find(id) != null);
        TweakInfos.Ids.Should().OnlyContain(id => TweakInfos.For(id)!.Percent >= 1 && TweakInfos.For(id)!.Percent <= 100);
    }

    [Theory]
    [InlineData(1, ImpactLevel.Low)]
    [InlineData(9, ImpactLevel.Low)]
    [InlineData(10, ImpactLevel.Medium)]
    [InlineData(24, ImpactLevel.Medium)]
    [InlineData(25, ImpactLevel.High)]
    [InlineData(100, ImpactLevel.High)]
    public void The_percentage_decides_the_colour_level(int percent, ImpactLevel expected)
    {
        new TweakInfo(TweakArea.Speed, percent, false).Level.Should().Be(expected);
    }

    [Fact]
    public void Areas_have_turkish_names()
    {
        TweakInfos.For("DisableDVR")!.AreaName.Should().Be("Hız");
        TweakInfos.For("DisableTelemetry")!.AreaName.Should().Be("Gizlilik");
        TweakInfos.For("EnableDarkMode")!.AreaName.Should().Be("Görünüm");
        TweakInfos.For("ShowKnownFileExt")!.AreaName.Should().Be("Kullanım");
        TweakInfos.For("DisableModernStandbyNetworking")!.AreaName.Should().Be("Pil");
    }

    [Fact]
    public void Recommended_settings_are_a_real_but_not_overwhelming_share()
    {
        var recommended = Catalog.Toggles.Count(t => TweakInfos.IsRecommended(t.Id));

        recommended.Should().BeGreaterThan(10);
        recommended.Should().BeLessThan(Catalog.Toggles.Count / 2 + 5);
    }

    [Theory]
    [InlineData("DisableNotifications")]
    [InlineData("DisableLocationServices")]
    [InlineData("DisableStorageSense")]
    [InlineData("DisableFastStartup")]
    [InlineData("DisableBitlockerAutoEncryption")]
    [InlineData("DisableDriverUpdates")]
    [InlineData("DisableFindMyDevice")]
    [InlineData("DisableWindowSnapping")]
    [InlineData("EnableDarkMode")]
    public void Settings_that_switch_off_something_many_people_use_are_never_recommended(string id)
    {
        TweakInfos.IsRecommended(id).Should().BeFalse();
    }

    [Fact]
    public void Every_recommended_setting_can_be_taken_back_to_the_Windows_default()
    {
        foreach (var tweak in Catalog.Toggles.Where(t => TweakInfos.IsRecommended(t.Id)))
        {
            tweak.Defaults.Should().NotBeNullOrEmpty($"{tweak.Id} is recommended, so it must be reversible to the default");
        }
    }

    [Fact]
    public void Unknown_ids_have_no_info()
    {
        TweakInfos.For("Yok").Should().BeNull();
        TweakInfos.IsRecommended("Yok").Should().BeFalse();
    }
}

public sealed class WindowsDefaultsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-defaults-" + Guid.NewGuid().ToString("N"));

    private static readonly TweakCatalog Catalog = TweakCatalog.Load();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void The_catalog_knows_the_Windows_default_of_almost_every_stand_alone_setting()
    {
        Catalog.Toggles.Count(t => t.Defaults is { Count: > 0 }).Should().BeGreaterThan(40);
        Catalog.Toggles.Where(t => t.Defaults != null).Should().OnlyContain(t => t.UndoTitle != null || t.Id.Length > 0);
    }

    [Fact]
    public async Task Applying_then_going_back_to_the_default_changes_the_registry_both_ways_and_Geri_Al_undoes_each_step()
    {
        foreach (var tweak in Catalog.Toggles.Where(t => t.Defaults != null))
        {
            var registry = new FakeRegistry();
            var build = tweak.MinBuild > 0 ? tweak.MinBuild : tweak.MaxBuild > 0 ? tweak.MaxBuild : 26100;
            var journal = new UndoJournal(Path.Combine(_dir, tweak.Id + ".json"));
            var fix = new WindowsTweakFix(Catalog, registry, () => build);
            var start = registry.Dump();

            fix.Select([tweak.Id]);
            (await fix.ApplyAsync(journal, CancellationToken.None)).Success.Should().BeTrue(tweak.Id);
            fix.IsApplied(tweak).Should().BeTrue(tweak.Id);
            var applied = registry.Dump();

            fix.Select([]);
            fix.SelectDefaults([tweak.Id]);
            (await fix.ApplyAsync(journal, CancellationToken.None)).Success.Should().BeTrue(tweak.Id);
            fix.IsDefault(tweak).Should().BeTrue($"{tweak.Id} must hold its Windows default after the reset");
            fix.IsApplied(tweak).Should().BeFalse($"{tweak.Id} must no longer be applied after the reset");

            var records = await journal.LoadAsync(CancellationToken.None);
            await fix.UndoAsync(records[1], CancellationToken.None);
            registry.Dump().Should().Be(applied, $"{tweak.Id}: undoing the reset");
            await fix.UndoAsync(records[0], CancellationToken.None);
            registry.Dump().Should().Be(start, $"{tweak.Id}: undoing the change");
        }
    }

    [Fact]
    public async Task Going_back_to_the_default_touches_only_what_holds_now()
    {
        var registry = new FakeRegistry();
        var fix = new WindowsTweakFix(Catalog, registry, () => 26100);
        fix.SelectDefaults(Catalog.Toggles.Select(t => t.Id));

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "a.json")), CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        result.Success.Should().BeTrue();
        registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task The_default_preview_names_the_setting_the_way_round_it_will_become()
    {
        var registry = new FakeRegistry();
        var fix = new WindowsTweakFix(Catalog, registry, () => 26100);
        fix.Select(["DisableDVR"]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "b.json")), CancellationToken.None);
        fix.Select([]);
        fix.SelectDefaults(["DisableDVR"]);

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.Changes.Should().Contain(l => l.StartsWith("Windows varsayılanına dönecek:") && l.Contains("aç"));
    }

    [Fact]
    public async Task A_setting_without_a_known_default_is_not_reset()
    {
        var tweak = Catalog.Toggles.First(t => t.Defaults == null);
        var registry = new FakeRegistry();
        var build = tweak.MinBuild > 0 ? tweak.MinBuild : tweak.MaxBuild > 0 ? tweak.MaxBuild : 26100;
        var fix = new WindowsTweakFix(Catalog, registry, () => build);
        fix.Select([tweak.Id]);
        await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "c.json")), CancellationToken.None);
        registry.Log.Clear();
        fix.Select([]);
        fix.SelectDefaults([tweak.Id]);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "c.json")), CancellationToken.None);

        result.Success.Should().BeTrue();
        registry.Log.Should().BeEmpty();
    }
}

public sealed class TweakBackupStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-backups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static TweakBackup Make(string id, DateTimeOffset? at = null) =>
        new(id, "Ayarlarım", at ?? DateTimeOffset.Now, 26100,
            new TweakSnapshot([new TweakSnapshotValue(RegHive.CurrentUser, "Software\\X", "V", true, RegKind.DWord, "1")], [new TweakKey(RegHive.CurrentUser, "Software\\Y")]));

    [Fact]
    public void A_saved_backup_comes_back_exactly()
    {
        var store = new TweakBackupStore(_dir);
        var backup = Make("yedek-20261121-101500-ab12");

        store.Save(backup).Should().BeNull();

        var found = store.Find(backup.Id)!;
        found.Name.Should().Be("Ayarlarım");
        found.Build.Should().Be(26100);
        found.Snapshot.Values.Should().BeEquivalentTo(backup.Snapshot.Values);
        found.Snapshot.CreatedKeys.Should().BeEquivalentTo(backup.Snapshot.CreatedKeys);
    }

    [Fact]
    public void The_list_is_newest_first_and_empty_without_a_folder()
    {
        var store = new TweakBackupStore(_dir);
        store.List().Backups.Should().BeEmpty();

        store.Save(Make("yedek-a", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        store.Save(Make("yedek-b", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        store.List().Backups.Select(b => b.Id).Should().Equal("yedek-b", "yedek-a");
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_not_hidden()
    {
        var store = new TweakBackupStore(_dir);
        store.Save(Make("yedek-iyi"));
        File.WriteAllText(Path.Combine(_dir, "yedek-bozuk.json"), "{ bozuk");

        var listing = store.List();

        listing.Backups.Should().ContainSingle().Which.Id.Should().Be("yedek-iyi");
        listing.Unreadable.Should().Equal("yedek-bozuk.json");
    }

    [Fact]
    public void A_file_whose_name_does_not_match_its_id_is_not_trusted()
    {
        var store = new TweakBackupStore(_dir);
        store.Save(Make("yedek-gercek"));
        File.Copy(Path.Combine(_dir, "yedek-gercek.json"), Path.Combine(_dir, "yedek-baska.json"));

        store.List().Unreadable.Should().Equal("yedek-baska.json");
        store.Find("yedek-baska").Should().BeNull();
    }

    [Theory]
    [InlineData("..\\kacis")]
    [InlineData("../kacis")]
    [InlineData("a/b")]
    [InlineData("C:\\Windows\\x")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ad soyad")]
    [InlineData("yedek.json")]
    public void An_id_that_could_point_outside_the_folder_is_refused_everywhere(string? id)
    {
        var store = new TweakBackupStore(_dir);

        TweakBackupStore.IsValidId(id).Should().BeFalse();
        store.Find(id!).Should().BeNull();
        store.Delete(id!).Should().NotBeNull();
        store.Save(Make(id ?? "x") with { Id = id! }).Should().NotBeNull();
    }

    [Fact]
    public void Delete_removes_only_that_file_inside_the_folder()
    {
        var store = new TweakBackupStore(_dir);
        store.Save(Make("yedek-bir"));
        store.Save(Make("yedek-iki"));
        var outside = Path.Combine(Path.GetDirectoryName(_dir)!, "yedek-bir.json");
        File.WriteAllText(outside, "dokunma");

        store.Delete("yedek-bir").Should().BeNull();

        store.List().Backups.Select(b => b.Id).Should().Equal("yedek-iki");
        File.Exists(outside).Should().BeTrue("a file outside the folder is never touched");
        File.Delete(outside);
    }

    [Fact]
    public void Deleting_something_that_is_not_there_is_not_an_error()
    {
        new TweakBackupStore(_dir).Delete("yedek-yok").Should().BeNull();
    }

    [Fact]
    public void Saving_where_nothing_can_be_written_says_why()
    {
        File.WriteAllText(_dir, "ben bir dosyayım");

        new TweakBackupStore(Path.Combine(_dir, "alt")).Save(Make("yedek-x")).Should().NotBeNullOrEmpty();
        File.Delete(_dir);
    }

    [Fact]
    public void New_ids_are_valid_and_different_each_time()
    {
        var a = TweakBackupStore.NewId(DateTimeOffset.Now);
        var b = TweakBackupStore.NewId(DateTimeOffset.Now);

        TweakBackupStore.IsValidId(a).Should().BeTrue();
        a.Should().NotBe(b);
    }
}

public sealed class TweakBackupRestoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-restore-" + Guid.NewGuid().ToString("N"));

    private readonly FakeRegistry _registry = new();

    private static readonly TweakCatalog Catalog = TweakCatalog.Load();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private TweakBackupStore Files => new(Path.Combine(_dir, "yedekler"));

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    private TweakBackupService Service(Func<DateTimeOffset>? now = null) => new(Catalog, _registry, Files, () => 26100, now);

    private async Task ApplyAsync(params string[] ids)
    {
        var fix = new WindowsTweakFix(Catalog, _registry, () => 26100);
        fix.Select(ids);
        (await fix.ApplyAsync(Journal, CancellationToken.None)).Success.Should().BeTrue();
    }

    private TweakRestoreFix Restore(string id)
    {
        var fix = new TweakRestoreFix(Files, _registry);
        fix.Select(id);
        return fix;
    }

    [Fact]
    public void Saving_keeps_the_settings_under_a_name_with_the_date()
    {
        var at = new DateTimeOffset(2026, 11, 21, 14, 5, 0, TimeSpan.Zero);

        var result = Service(() => at).Create();

        result.Success.Should().BeTrue();
        result.Backup!.Name.Should().StartWith("Ayarlarım ");
        result.Message.Should().Contain("kaydedildi");
        Files.List().Backups.Should().ContainSingle().Which.Id.Should().Be(result.Backup.Id);
    }

    [Fact]
    public void A_name_the_person_gave_is_used()
    {
        Service().Create("  Oyun öncesi  ").Backup!.Name.Should().Be("Oyun öncesi");
    }

    [Fact]
    public void Saving_reads_the_registry_and_changes_nothing()
    {
        _registry.Set(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", RegKind.DWord, "1");
        var before = _registry.Dump();

        Service().Create();

        _registry.Dump().Should().Be(before);
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public void A_backup_that_cannot_be_written_says_so_with_its_code()
    {
        File.WriteAllText(Path.Combine(_dir + "-dosya"), "x");
        var service = new TweakBackupService(Catalog, _registry, new TweakBackupStore(Path.Combine(_dir + "-dosya", "alt")), () => 26100);

        var result = service.Create();
        File.Delete(_dir + "-dosya");

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("[FX-4010]");
        result.Backup.Should().BeNull();
    }

    [Fact]
    public async Task Restoring_puts_every_value_back_as_it_was_when_saved_and_Geri_Al_undoes_the_restore()
    {
        _registry.Set(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", RegKind.DWord, "1");
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var saved = Service().Create().Backup!;
        var atSave = _registry.Dump();

        await ApplyAsync("DisableDVR", "DisableAnimations", "DisableTelemetry", "DisableCopilot");
        var afterChanges = _registry.Dump();
        afterChanges.Should().NotBe(atSave);

        var journal = Journal;
        var fix = Restore(saved.Id);
        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("geri yüklendi");
        _registry.Dump().Should().Be(atSave);

        var restoreRecord = (await journal.LoadAsync(CancellationToken.None)).Last();
        restoreRecord.FixId.Should().Be(TweakRestoreFix.FixIdValue);
        await fix.UndoAsync(restoreRecord, CancellationToken.None);
        _registry.Dump().Should().Be(afterChanges);
    }

    [Fact]
    public async Task Preview_counts_what_differs_and_says_nothing_to_do_when_it_does_not()
    {
        var saved = Service().Create().Backup!;

        var same = await Restore(saved.Id).PreviewAsync(CancellationToken.None);
        same.HasChanges.Should().BeFalse();
        same.Changes.Should().ContainSingle().Which.Should().Contain("zaten aynı");

        await ApplyAsync("DisableDVR");
        var different = await Restore(saved.Id).PreviewAsync(CancellationToken.None);

        different.HasChanges.Should().BeTrue();
        different.Changes.Should().Contain(l => l.Contains(saved.Name));
        different.Changes.Should().Contain(l => l.Contains("kayıt değeri"));
    }

    [Fact]
    public async Task Preview_changes_nothing()
    {
        var saved = Service().Create().Backup!;
        await ApplyAsync("DisableDVR");
        var before = _registry.Dump();
        _registry.Log.Clear();

        await Restore(saved.Id).PreviewAsync(CancellationToken.None);

        _registry.Dump().Should().Be(before);
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task A_backup_that_is_gone_is_reported_with_its_code_and_nothing_is_touched()
    {
        var fix = Restore("yedek-yok");

        var preview = await fix.PreviewAsync(CancellationToken.None);
        var result = await fix.ApplyAsync(Journal, CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
        preview.Changes.Single().Should().StartWith("[FX-4012]");
        result.Success.Should().BeFalse();
        result.Summary.Should().StartWith("[FX-4012]");
        _registry.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task No_backup_selected_is_the_same_as_a_missing_one()
    {
        var result = await new TweakRestoreFix(Files, _registry).ApplyAsync(Journal, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_restore_that_fails_half_way_is_put_back_and_its_journal_entry_withdrawn()
    {
        _registry.Set(RegHive.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", RegKind.DWord, "1");
        _registry.Set(RegHive.CurrentUser, "Control Panel\\Desktop", "UserPreferencesMask", RegKind.Binary, "9e,1e,07,80,12,00,00,00");
        var saved = Service().Create().Backup!;
        await ApplyAsync("DisableDVR", "DisableAnimations");
        var afterChanges = _registry.Dump();
        _registry.FailOnWrite = 2;
        var journal = Journal;
        var recordsBefore = (await journal.LoadAsync(CancellationToken.None)).Count;

        var result = await Restore(saved.Id).ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().StartWith("[FX-4011]").And.Contain("geri alındı");
        _registry.Dump().Should().Be(afterChanges);
        (await journal.LoadAsync(CancellationToken.None)).Should().HaveCount(recordsBefore);
    }

    [Fact]
    public void Administrator_rights_are_needed_when_the_backup_holds_machine_values()
    {
        var saved = Service().Create().Backup!;

        Restore(saved.Id).RequiresAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task A_damaged_journal_entry_is_reported_on_undo()
    {
        var broken = new UndoRecord(Guid.NewGuid(), TweakRestoreFix.FixIdValue, DateTimeOffset.UtcNow, "x", new Dictionary<string, string> { ["values"] = "bozuk", ["createdKeys"] = "[]" });

        var act = () => new TweakRestoreFix(Files, _registry).UndoAsync(broken, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
