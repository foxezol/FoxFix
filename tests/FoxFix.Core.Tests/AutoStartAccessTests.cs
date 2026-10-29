using System.Xml.Linq;
using FluentAssertions;
using FoxFix.Core.Commands;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class AutoStartAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-auto-" + Guid.NewGuid().ToString("N"));

    public AutoStartAccessTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeSchtasks : ICommandRunner
    {
        public bool Exists { get; set; }

        public bool FailCreate { get; set; }

        public List<string> Calls { get; } = [];

        public string? CreatedXml { get; private set; }

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add(string.Join(' ', arguments.Select(a => a.Contains(".xml") ? "<xml>" : a)));
            fileName.Should().EndWith("schtasks.exe");

            switch (arguments[0])
            {
                case "/Query":
                    return Task.FromResult(new CommandResult(Exists ? 0 : 1, "", Exists ? "" : "hata: görev yok"));
                case "/Create":
                    if (FailCreate)
                    {
                        return Task.FromResult(new CommandResult(1, "", "Erişim engellendi.\r\nikinci satır"));
                    }

                    CreatedXml = File.ReadAllText(arguments[4]);
                    Exists = true;
                    return Task.FromResult(new CommandResult(0, "", ""));
                default:
                    Exists = false;
                    return Task.FromResult(new CommandResult(0, "", ""));
            }
        }
    }

    private static FoxDpiAutoStartFix NewFix(FakeSchtasks runner, string? exe = @"C:\Araçlar\FoxFix\FoxFix.exe") =>
        new(new FoxDpiAutoStart(runner), () => exe, () => @"PC\kullanici");

    private UndoJournal NewJournal() => new(Path.Combine(_dir, "undo.json"));

    // ---------- the scheduled task ----------

    [Fact]
    public void The_task_runs_FoxDPI_only_at_the_users_sign_in_with_the_highest_rights()
    {
        var xml = XDocument.Parse(FoxDpiAutoStart.BuildTaskXml(@"C:\Program Files\FoxFix & Co\FoxFix.exe", @"PC\kullanici"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        xml.Descendants(ns + "LogonTrigger").Should().ContainSingle();
        xml.Descendants(ns + "RunLevel").Single().Value.Should().Be("HighestAvailable");
        xml.Descendants(ns + "Command").Single().Value.Should().Be(@"C:\Program Files\FoxFix & Co\FoxFix.exe", "the & is escaped in the file and comes back as it was");
        xml.Descendants(ns + "Arguments").Single().Value.Should().Be("--sadece-foxdpi");
        xml.Descendants(ns + "UserId").Select(e => e.Value).Should().OnlyContain(v => v == @"PC\kullanici");
        xml.Descendants(ns + "ExecutionTimeLimit").Single().Value.Should().Be("PT0S", "FoxDPI must not be stopped after a few days");
        xml.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value.Should().Be("false");
    }

    [Fact]
    public async Task Previewing_does_not_touch_the_system_and_names_the_task_and_the_path()
    {
        var runner = new FakeSchtasks();
        var fix = NewFix(runner);
        fix.Configure(true);

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeTrue();
        preview.Changes.Should().Contain(l => l.Contains(FoxDpiAutoStart.TaskName));
        preview.Changes.Should().Contain(l => l.Contains(@"C:\Araçlar\FoxFix\FoxFix.exe"));
        runner.Calls.Should().OnlyContain(c => c.StartsWith("/Query"));
    }

    [Fact]
    public async Task Turning_it_on_writes_the_journal_first_and_creates_the_task_and_removes_the_temp_file()
    {
        var runner = new FakeSchtasks();
        var fix = NewFix(runner);
        var journal = NewJournal();
        fix.Configure(true);

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        runner.Exists.Should().BeTrue();
        runner.CreatedXml.Should().Contain("HighestAvailable");
        runner.Calls.Should().Contain($"/Create /TN {FoxDpiAutoStart.TaskName} /XML <xml> /F");
        (await journal.LoadAsync(CancellationToken.None)).Should().ContainSingle(r => r.FixId == FoxDpiAutoStartFix.FixIdValue);
        Directory.EnumerateFiles(Path.GetTempPath(), "foxfix-foxdpi-*.xml").Should().BeEmpty();
    }

    [Fact]
    public async Task When_it_is_already_on_there_is_nothing_to_change()
    {
        var runner = new FakeSchtasks { Exists = true };
        var fix = NewFix(runner);
        fix.Configure(true);

        (await fix.PreviewAsync(CancellationToken.None)).HasChanges.Should().BeFalse();
        (await fix.ApplyAsync(NewJournal(), CancellationToken.None)).Success.Should().BeTrue();
        runner.Calls.Should().NotContain(c => c.StartsWith("/Create"));
    }

    [Fact]
    public async Task A_task_that_cannot_be_created_leaves_no_journal_entry_and_says_why()
    {
        var runner = new FakeSchtasks { FailCreate = true };
        var fix = NewFix(runner);
        var journal = NewJournal();
        fix.Configure(true);

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("Erişim engellendi.").And.NotContain("ikinci satır");
        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_it_off_deletes_the_task_and_undo_puts_it_back()
    {
        var runner = new FakeSchtasks { Exists = true };
        var fix = NewFix(runner);
        var journal = NewJournal();
        fix.Configure(false);

        var result = await fix.ApplyAsync(journal, CancellationToken.None);

        result.Success.Should().BeTrue();
        runner.Exists.Should().BeFalse();
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        runner.Exists.Should().BeTrue("it was on before, so undo turns it on again");
        runner.CreatedXml.Should().Contain(@"C:\Araçlar\FoxFix\FoxFix.exe");
    }

    [Fact]
    public async Task Undoing_a_first_time_enable_removes_the_task_again()
    {
        var runner = new FakeSchtasks();
        var fix = NewFix(runner);
        var journal = NewJournal();
        fix.Configure(true);
        await fix.ApplyAsync(journal, CancellationToken.None);
        var record = (await journal.LoadAsync(CancellationToken.None)).Single();

        await fix.UndoAsync(record, CancellationToken.None);

        runner.Exists.Should().BeFalse();
    }

    // ---------- access test ----------

    private sealed class FakeProbe : IHttpsProbe
    {
        public Dictionary<string, HttpsProbeOutcome> Outcomes { get; } = [];

        public async Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct)
        {
            await Task.Yield();
            return Outcomes.GetValueOrDefault(url.Host, HttpsProbeOutcome.Ok);
        }
    }

    private sealed class Collect : IProgress<AccessResult>
    {
        public List<AccessResult> Reports { get; } = [];

        public void Report(AccessResult value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }

    [Fact]
    public async Task The_access_test_reports_every_site_in_the_order_given_with_a_plain_word_for_each_outcome()
    {
        var probe = new FakeProbe();
        probe.Outcomes["discord.com"] = HttpsProbeOutcome.TlsFailed;
        probe.Outcomes["roblox.com"] = HttpsProbeOutcome.Timeout;
        var progress = new Collect();

        var results = await new AccessTester(probe).RunAsync(
            [new Uri("https://discord.com/"), new Uri("https://example.org/"), new Uri("https://roblox.com/")], progress, CancellationToken.None);

        results.Select(r => r.Host).Should().Equal("discord.com", "example.org", "roblox.com");
        results.Select(r => r.Opened).Should().Equal(false, true, false);
        results.Select(r => r.Text).Should().Equal("güvenli bağlantı kesildi", "açıldı", "zaman aşımı");
        progress.Reports.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(HttpsProbeOutcome.CertificateError, "sertifika hatası")]
    [InlineData(HttpsProbeOutcome.DnsFailed, "adres bulunamadı")]
    [InlineData(HttpsProbeOutcome.Other, "bağlanılamadı")]
    public void Every_outcome_has_its_own_plain_word(HttpsProbeOutcome outcome, string expected)
    {
        new AccessResult("x.org", outcome, TimeSpan.Zero).Text.Should().Be(expected);
    }
}

public sealed class AppPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-paths-" + Guid.NewGuid().ToString("N"));

    public AppPathsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Without_the_marker_FoxFix_uses_the_normal_folders()
    {
        AppPaths.PortableRootFor(_dir).Should().BeNull();
        AppPaths.PortableRootFor(null).Should().BeNull();
    }

    [Fact]
    public void With_the_marker_next_to_the_exe_everything_goes_into_a_data_folder_beside_it()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableMarker), "");

        AppPaths.PortableRootFor(_dir).Should().Be(Path.Combine(_dir, "Veri"));
    }
}
