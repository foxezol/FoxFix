using FluentAssertions;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Fixes;
using FoxFix.Core.Reporting;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class ProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-progress-" + Guid.NewGuid().ToString("N"));

    public ProgressTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Target : ICleanupTarget
    {
        private readonly long[] _steps;

        public Target(string id, params long[] steps)
        {
            Id = id;
            _steps = steps;
        }

        public string Id { get; }

        public string Title => "Hedef " + Id;

        public string Description => "";

        public bool NeedsAdmin => false;

        public long MeasureBytes(CancellationToken ct) => _steps.Sum();

        public CleanupOutcome Clean(CancellationToken ct) => new(_steps.Sum(), 0);

        public CleanupOutcome Clean(IProgress<long>? freedBytes, CancellationToken ct)
        {
            foreach (var step in _steps)
            {
                freedBytes?.Report(step);
            }

            return new CleanupOutcome(_steps.Sum(), 0);
        }
    }

    private sealed class Collect : IProgress<FixProgress>
    {
        public List<FixProgress> Reports { get; } = [];

        public void Report(FixProgress value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }

    private UndoJournal NewJournal() => new(Path.Combine(_dir, "undo.json"));

    [Fact]
    public async Task Disk_cleanup_reports_how_far_it_is_and_ends_at_one()
    {
        var fix = new DiskCleanupFix([new Target("a", 100, 100), new Target("b", 300)]);
        fix.Select(["a", "b"]);
        await fix.PreviewAsync(CancellationToken.None);
        var progress = new Collect();

        var result = await fix.ApplyAsync(NewJournal(), progress, CancellationToken.None);

        result.Success.Should().BeTrue();
        progress.Reports.Should().NotBeEmpty();
        progress.Reports.Select(r => r.Fraction).Should().BeInAscendingOrder();
        progress.Reports[^1].Fraction.Should().Be(1.0);
        progress.Reports[^1].Text.Should().Contain("/").And.Contain("silindi");
    }

    [Fact]
    public async Task Without_a_preview_there_is_no_total_so_nothing_is_reported_but_the_cleanup_still_works()
    {
        var fix = new DiskCleanupFix([new Target("a", 100)]);
        fix.Select(["a"]);
        var progress = new Collect();

        var result = await fix.ApplyAsync(NewJournal(), progress, CancellationToken.None);

        result.Success.Should().BeTrue();
        progress.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task The_old_apply_without_progress_still_works()
    {
        var fix = new DiskCleanupFix([new Target("a", 10)]);
        fix.Select(["a"]);

        (await fix.ApplyAsync(NewJournal(), CancellationToken.None)).Success.Should().BeTrue();
    }

    [Fact]
    public void A_real_folder_reports_every_deleted_file()
    {
        var root = Path.Combine(_dir, "a", "b", "temp");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllBytes(Path.Combine(root, "one.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "sub", "two.bin"), new byte[500]);
        var deltas = new List<long>();

        var outcome = new FolderTarget("t", "t", "", false, root).Clean(new Progress(deltas.Add), CancellationToken.None);

        outcome.FreedBytes.Should().Be(1500);
        deltas.Sum().Should().Be(1500);
        Directory.EnumerateFileSystemEntries(root).Should().BeEmpty();
    }

    private sealed class Progress(Action<long> handler) : IProgress<long>
    {
        public void Report(long value) => handler(value);
    }

    [Theory]
    [InlineData(true, "DNS: 1.1.1.1 (elle girilmiş)")]
    [InlineData(false, "DNS: 1.1.1.1 (modemden)")]
    [InlineData(null, "DNS: 1.1.1.1")]
    public void The_report_says_whether_the_DNS_was_typed_in_or_comes_from_the_modem(bool? isStatic, string expected)
    {
        var input = new ReportInput(
            DateTimeOffset.Now, "0.1", "Windows 11", "kullanici", "bilgisayar",
            [new ReportAdapter("Ethernet 3", "Realtek", true, null, ["192.168.1.109"], ["192.168.1.1"], ["1.1.1.1"], isStatic)],
            null, [], []);

        DiagnosticReportBuilder.Build(input, maskPersonalData: true).Should().Contain(expected);
    }
}
