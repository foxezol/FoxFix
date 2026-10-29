using FluentAssertions;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using FoxFix.Core.Startup;
using Xunit;

namespace FoxFix.Core.Tests;

public class StartupApprovalTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    public void The_lowest_bit_of_the_first_byte_means_disabled(byte first, bool enabled)
    {
        StartupApproval.IsEnabled([first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]).Should().Be(enabled);
    }

    [Fact]
    public void No_value_at_all_means_the_program_starts()
    {
        StartupApproval.IsEnabled(null).Should().BeTrue();
        StartupApproval.IsEnabled([]).Should().BeTrue();
    }

    [Fact]
    public void The_values_FoxFix_writes_are_twelve_bytes_like_Task_Manager_writes_them()
    {
        var enabled = StartupApproval.EnabledValue();
        var when = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var disabled = StartupApproval.DisabledValue(when);

        enabled.Should().HaveCount(12);
        enabled[0].Should().Be(2);
        disabled.Should().HaveCount(12);
        disabled[0].Should().Be(3);
        BitConverter.ToInt64(disabled, 4).Should().Be(when.ToFileTimeUtc());
        StartupApproval.IsEnabled(enabled).Should().BeTrue();
        StartupApproval.IsEnabled(disabled).Should().BeFalse();
    }
}

public class StartupFilterTests
{
    private const string Windows = @"C:\Windows";

    [Theory]
    [InlineData("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe")]
    [InlineData("AnythingAtAll", @"C:\Windows\System32\something.exe")]
    [InlineData("Defender", @"""C:\Program Files\Windows Defender\MSASCuiL.exe""")]
    [InlineData("Shell", @"%SystemRoot%\explorer.exe /x")]
    public void Windows_and_security_entries_are_never_offered(string name, string command)
    {
        StartupFilter.IsProtected(name, command, Windows).Should().BeTrue();
    }

    [Theory]
    [InlineData("Discord", @"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe")]
    [InlineData("Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent")]
    [InlineData("WindowsLookalike", @"C:\Program Files\WindowsApps\x\app.exe")]
    [InlineData("Tool", @"C:\Windows2\tool.exe")]
    public void Ordinary_programs_are_offered_even_when_the_name_sounds_like_windows(string name, string command)
    {
        StartupFilter.IsProtected(name, command, Windows).Should().BeFalse();
    }
}

public sealed class StartupFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-startup-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeStore _store = new();

    private UndoJournal Journal => new(Path.Combine(_dir, "undo.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private StartupFix CreateFix() => new(_store, () => new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc));

    private static StartupEntry User(string name, bool enabled = true) =>
        new(name, $@"C:\Apps\{name}.exe", StartupSource.CurrentUserRun, enabled);

    /// <summary>The fake store derives "enabled" from its approval values, like the registry does.</summary>
    private void AddDisabled(StartupEntry entry)
    {
        _store.Add(entry);
        _store.Approvals[entry.Id] = Convert.ToBase64String(StartupApproval.DisabledValue(DateTime.UtcNow));
    }

    private static StartupEntry Machine(string name, bool enabled = true) =>
        new(name, $@"C:\Apps\{name}.exe", StartupSource.MachineRun, enabled);

    [Fact]
    public async Task Preview_names_what_will_change_and_promises_nothing_is_deleted_without_changing_anything()
    {
        _store.Add(User("Discord"));
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Discord").Id] = false });

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain("Açılışta başlamayacak: Discord");
        preview.Changes.Should().Contain(l => l.Contains("Hiçbir program silinmez"));
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_says_when_the_user_is_switching_a_program_back_on()
    {
        AddDisabled(User("Steam"));
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Steam").Id] = true });

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain("Açılışta başlayacak: Steam");
    }

    [Fact]
    public async Task With_no_selection_there_is_nothing_to_confirm()
    {
        _store.Add(User("Discord"));

        var preview = await CreateFix().PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Entries_already_in_the_wanted_state_and_unknown_ids_are_ignored()
    {
        _store.Add(User("Discord"));
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool>
        {
            [User("Discord").Id] = true,          // already starts
            ["CurrentUserRun|DoesNotExist"] = false,
            [@"MachineRun|SecurityHealth"] = false,
        });

        var result = await fix.ApplyAsync(Journal, CancellationToken.None);

        result.Summary.Should().Contain("Değiştirilecek bir şey yoktu");
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_saves_the_old_flag_first_then_switches_the_program_off_without_removing_it()
    {
        _store.Add(User("Discord"));
        var previousRaw = Convert.ToBase64String(StartupApproval.EnabledValue());
        _store.Approvals[User("Discord").Id] = previousRaw;
        var journal = Journal;
        IReadOnlyList<UndoRecord>? seenAtWrite = null;
        _store.OnWrite = async () => seenAtWrite = await journal.LoadAsync(CancellationToken.None);
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Discord").Id] = false });

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be("1 programın açılış ayarı değişti.");
        seenAtWrite.Should().ContainSingle(r => r.Data["previous"] == previousRaw);
        StartupApproval.IsEnabled(Convert.FromBase64String(_store.Approvals[User("Discord").Id]!)).Should().BeFalse();
        _store.List().Should().ContainSingle(e => e.Name == "Discord", "the entry is still there, only switched");
    }

    [Fact]
    public async Task Switching_a_program_back_on_writes_the_enabled_value()
    {
        AddDisabled(User("Steam"));
        _store.List().Single().Enabled.Should().BeFalse("precondition");
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Steam").Id] = true });

        await fix.ApplyAsync(Journal, CancellationToken.None);

        _store.List().Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        _store.Add(User("Discord"));
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Discord").Id] = false });
        await fix.ApplyAsync(Journal, CancellationToken.None);
        var writes = _store.Writes.Count;

        var second = await fix.ApplyAsync(Journal, CancellationToken.None);

        second.Success.Should().BeTrue();
        _store.Writes.Should().HaveCount(writes);
    }

    [Fact]
    public void Administrator_rights_are_only_needed_when_a_machine_wide_entry_is_involved()
    {
        _store.Add(User("A"));
        _store.Add(Machine("B"));
        var fix = CreateFix();

        fix.SetDesired(new Dictionary<string, bool> { [User("A").Id] = false });
        fix.RequiresAdmin.Should().BeFalse();

        fix.SetDesired(new Dictionary<string, bool> { [User("A").Id] = false, [Machine("B").Id] = false });
        fix.RequiresAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task A_refused_write_is_reported_leaves_no_record_and_does_not_stop_the_rest()
    {
        _store.Add(Machine("Locked"));
        _store.Add(User("Fine"));
        _store.DenyWritesFor.Add(Machine("Locked").Id);
        var journal = Journal;
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [Machine("Locked").Id] = false, [User("Fine").Id] = false });

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("Locked için açılış ayarı değiştirilemedi");
        (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle(r => r.Data["name"] == "Fine");
        _store.List().Single(e => e.Name == "Fine").Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Undo_puts_the_previous_flag_back()
    {
        _store.Add(User("Discord"));
        var previous = Convert.ToBase64String(StartupApproval.EnabledValue());
        _store.Approvals[User("Discord").Id] = previous;
        var journal = Journal;
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Discord").Id] = false });
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        _store.Approvals[User("Discord").Id].Should().Be(previous);
        _store.List().Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Undo_removes_the_flag_again_when_there_was_none_before()
    {
        _store.Add(User("Discord"));
        var journal = Journal;
        var fix = CreateFix();
        fix.SetDesired(new Dictionary<string, bool> { [User("Discord").Id] = false });
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();
        record.Data["hadValue"].Should().Be("False");

        await fix.UndoAsync(record, CancellationToken.None);

        _store.Approvals.ContainsKey(User("Discord").Id).Should().BeFalse();
    }

    [Fact]
    public async Task A_damaged_undo_record_is_rejected()
    {
        var record = new UndoRecord(Guid.NewGuid(), StartupFix.FixIdValue, DateTimeOffset.UtcNow, "x", new Dictionary<string, string>());

        var act = () => CreateFix().UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bozuk*");
    }

    private sealed class FakeStore : IStartupStore
    {
        private readonly List<StartupEntry> _entries = [];

        public Dictionary<string, string?> Approvals { get; } = [];

        public List<string> Writes { get; } = [];

        public HashSet<string> DenyWritesFor { get; } = [];

        public Func<Task>? OnWrite { get; set; }

        public void Add(StartupEntry entry) => _entries.Add(entry);

        public IReadOnlyList<StartupEntry> List() =>
            _entries
                .Select(e => e with
                {
                    Enabled = !Approvals.TryGetValue(e.Id, out var raw) || StartupApproval.IsEnabled(raw is null ? null : Convert.FromBase64String(raw)),
                })
                .ToList();

        public string? ReadApproval(StartupEntry entry) => Approvals.GetValueOrDefault(entry.Id);

        public void WriteApproval(StartupEntry entry, string? rawBase64)
        {
            OnWrite?.Invoke().GetAwaiter().GetResult();

            if (DenyWritesFor.Contains(entry.Id))
            {
                throw new UnauthorizedAccessException("Erişim engellendi");
            }

            Writes.Add(entry.Id);
            if (rawBase64 is null)
            {
                Approvals.Remove(entry.Id);
            }
            else
            {
                Approvals[entry.Id] = rawBase64;
            }
        }
    }
}
