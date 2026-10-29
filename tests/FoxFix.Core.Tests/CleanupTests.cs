using System.Diagnostics;
using FluentAssertions;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(512 * 1024, "512 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(1536L * 1024, "1,5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1,5 GB")]
    public void Sizes_read_naturally_with_a_decimal_comma(long bytes, string expected)
    {
        ByteSize.Format(bytes).Should().Be(expected);
    }

    [Fact]
    public void A_negative_number_never_shows_a_minus_sign()
    {
        ByteSize.Format(-5).Should().Be("0 B");
    }
}

public sealed class SafeFolderCleanerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "foxfix-clean-tests-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_base, "root");

    public SafeFolderCleanerTests()
    {
        Directory.CreateDirectory(Root);
    }

    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        // A junction must be removed on its own, otherwise a recursive delete is refused.
        foreach (var junction in _junctions.Where(Directory.Exists))
        {
            Directory.Delete(junction, recursive: false);
        }

        if (!Directory.Exists(_base))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_base, recursive: true);
    }

    private static void Write(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private void MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "the test needs a directory junction");
        _junctions.Add(link);
    }

    [Theory]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:", false)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"C:\Windows\Temp", true)]
    [InlineData(@"C:\Windows\Temp\", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Temp", true)]
    [InlineData(@"relative\path\here", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Only_deep_absolute_paths_are_accepted_as_a_root(string path, bool safe)
    {
        SafeFolderCleaner.IsSafeRoot(path).Should().Be(safe);
    }

    [Fact]
    public void Measure_adds_up_every_file_including_nested_ones()
    {
        Write(Path.Combine(Root, "a.tmp"), 1000);
        Write(Path.Combine(Root, "sub", "b.tmp"), 2000);
        Write(Path.Combine(Root, "sub", "deeper", "c.tmp"), 3000);

        SafeFolderCleaner.Measure(Root, CancellationToken.None).Should().Be(6000);
    }

    [Fact]
    public void Deleting_empties_the_folder_but_keeps_the_folder_itself()
    {
        Write(Path.Combine(Root, "a.tmp"), 1000);
        Write(Path.Combine(Root, "sub", "deeper", "c.tmp"), 3000);

        var outcome = SafeFolderCleaner.DeleteContents(Root, CancellationToken.None);

        outcome.FreedBytes.Should().Be(4000);
        outcome.SkippedFiles.Should().Be(0);
        Directory.Exists(Root).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(Root).Should().BeEmpty();
    }

    [Fact]
    public void Read_only_files_are_deleted_too()
    {
        var file = Path.Combine(Root, "locked-by-attribute.tmp");
        Write(file, 500);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var outcome = SafeFolderCleaner.DeleteContents(Root, CancellationToken.None);

        outcome.FreedBytes.Should().Be(500);
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void A_file_in_use_is_skipped_and_counted_while_everything_else_goes()
    {
        var inUse = Path.Combine(Root, "sub", "in-use.tmp");
        Write(inUse, 700);
        Write(Path.Combine(Root, "gone.tmp"), 300);
        Write(Path.Combine(Root, "sub", "also-gone.tmp"), 200);

        using (new FileStream(inUse, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = SafeFolderCleaner.DeleteContents(Root, CancellationToken.None);

            outcome.FreedBytes.Should().Be(500);
            outcome.SkippedFiles.Should().Be(1);
        }

        File.Exists(inUse).Should().BeTrue();
        File.Exists(Path.Combine(Root, "gone.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(Root, "sub", "also-gone.tmp")).Should().BeFalse();
    }

    [Fact]
    public void A_junction_inside_the_folder_is_never_followed()
    {
        var outside = Path.Combine(_base, "outside");
        Write(Path.Combine(outside, "precious.txt"), 4096);
        Write(Path.Combine(Root, "plain.tmp"), 100);
        MakeJunction(Path.Combine(Root, "link-to-outside"), outside);

        var measured = SafeFolderCleaner.Measure(Root, CancellationToken.None);
        var outcome = SafeFolderCleaner.DeleteContents(Root, CancellationToken.None);

        measured.Should().Be(100, "the junction target is not counted");
        outcome.FreedBytes.Should().Be(100);
        File.Exists(Path.Combine(outside, "precious.txt")).Should().BeTrue("deleting through a link would destroy it");
    }

    [Fact]
    public void A_root_that_is_itself_a_junction_is_refused()
    {
        var outside = Path.Combine(_base, "outside-target");
        Write(Path.Combine(outside, "precious.txt"), 2048);
        var linkRoot = Path.Combine(_base, "linked-root");
        MakeJunction(linkRoot, outside);

        SafeFolderCleaner.Measure(linkRoot, CancellationToken.None).Should().Be(0);
        SafeFolderCleaner.DeleteContents(linkRoot, CancellationToken.None).Should().Be(new CleanupOutcome(0, 0));
        File.Exists(Path.Combine(outside, "precious.txt")).Should().BeTrue();
    }

    [Fact]
    public void A_missing_folder_is_simply_empty()
    {
        var missing = Path.Combine(_base, "does", "not", "exist");

        SafeFolderCleaner.Measure(missing, CancellationToken.None).Should().Be(0);
        SafeFolderCleaner.DeleteContents(missing, CancellationToken.None).Should().Be(new CleanupOutcome(0, 0));
    }

    [Fact]
    public void A_relative_path_is_refused_before_anything_happens()
    {
        var before = Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Count();

        SafeFolderCleaner.DeleteContents(@"some\relative\folder", CancellationToken.None).Should().Be(new CleanupOutcome(0, 0));

        Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Count().Should().Be(before);
    }

    [Fact]
    public void Cancelling_stops_the_work()
    {
        Write(Path.Combine(Root, "a.tmp"), 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => SafeFolderCleaner.DeleteContents(Root, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(Root, "a.tmp")).Should().BeTrue();
    }

    [Fact]
    public void A_folder_target_with_several_roots_adds_them_up_and_skips_missing_ones()
    {
        var second = Path.Combine(_base, "second", "inner");
        Write(Path.Combine(Root, "a.tmp"), 100);
        Write(Path.Combine(second, "b.tmp"), 200);
        var target = new FolderTarget("t", "Test", "açıklama", false, Root, second, Path.Combine(_base, "missing", "x"));

        target.MeasureBytes(CancellationToken.None).Should().Be(300);
        var outcome = target.Clean(CancellationToken.None);

        outcome.FreedBytes.Should().Be(300);
        Directory.EnumerateFileSystemEntries(Root).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(second).Should().BeEmpty();
    }
}

public sealed class DiskCleanupFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-diskfix-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static FakeTarget Target(string id, long bytes, bool admin = false) => new(id, "Başlık " + id, bytes, admin);

    [Fact]
    public async Task Nothing_selected_means_nothing_to_confirm()
    {
        var fix = new DiskCleanupFix([Target("a", 100)]);

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_shows_each_size_the_total_and_says_it_cannot_be_undone()
    {
        var fix = new DiskCleanupFix([Target("a", 5L * 1024 * 1024), Target("b", 2048), Target("c", 999)]);
        fix.Select(["a", "b"]);

        var preview = await fix.PreviewAsync(CancellationToken.None);

        preview.Changes.Should().Contain("Temizlenecek: Başlık a, yaklaşık 5 MB");
        preview.Changes.Should().Contain("Temizlenecek: Başlık b, yaklaşık 2 KB");
        preview.Changes.Should().Contain(l => l.StartsWith("Toplam yaklaşık"));
        preview.Changes.Should().Contain(l => l.Contains("geri getirilemez"));
        preview.Changes.Should().NotContain(l => l.Contains("Başlık c"));
    }

    [Fact]
    public async Task Preview_deletes_nothing()
    {
        var target = Target("a", 100);
        var fix = new DiskCleanupFix([target]);
        fix.Select(["a"]);

        await fix.PreviewAsync(CancellationToken.None);

        target.CleanCalls.Should().Be(0);
    }

    [Fact]
    public async Task Apply_cleans_only_what_was_selected_and_reports_the_freed_space()
    {
        var a = Target("a", 3L * 1024 * 1024);
        var b = Target("b", 1000);
        var fix = new DiskCleanupFix([a, b]);
        fix.Select(["a"]);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be("3 MB boşaltıldı.");
        a.CleanCalls.Should().Be(1);
        b.CleanCalls.Should().Be(0);
    }

    [Fact]
    public async Task Skipped_files_are_mentioned()
    {
        var target = Target("a", 2048);
        target.Skipped = 3;
        var fix = new DiskCleanupFix([target]);
        fix.Select(["a"]);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        result.Summary.Should().Contain("3 dosya kullanımda olduğu için atlandı");
    }

    [Fact]
    public async Task A_failing_target_is_reported_and_the_others_still_run()
    {
        var bad = Target("bad", 10);
        bad.Throws = true;
        var good = Target("good", 4096);
        var fix = new DiskCleanupFix([bad, good]);
        fix.Select(["bad", "good"]);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("Başlık bad temizlenemedi").And.Contain("4 KB boşaltıldı");
        good.CleanCalls.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_ids_are_ignored()
    {
        var fix = new DiskCleanupFix([Target("a", 1)]);
        fix.Select(["../../Windows", "nope"]);

        var result = await fix.ApplyAsync(new UndoJournal(Path.Combine(_dir, "undo.json")), CancellationToken.None);

        result.Summary.Should().Contain("seçilmedi");
    }

    [Fact]
    public void Administrator_rights_are_only_needed_when_a_selected_target_needs_them()
    {
        var fix = new DiskCleanupFix([Target("user", 1), Target("system", 1, admin: true)]);

        fix.Select(["user"]);
        fix.RequiresAdmin.Should().BeFalse();

        fix.Select(["user", "system"]);
        fix.RequiresAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task It_never_writes_an_undo_record_because_there_is_nothing_to_undo()
    {
        var path = Path.Combine(_dir, "undo.json");
        var fix = new DiskCleanupFix([Target("a", 100)]);
        fix.Select(["a"]);

        await fix.ApplyAsync(new UndoJournal(path), CancellationToken.None);

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Undo_is_refused_honestly()
    {
        var fix = new DiskCleanupFix([]);
        var record = new UndoRecord(Guid.NewGuid(), DiskCleanupFix.FixIdValue, DateTimeOffset.UtcNow, "x", new Dictionary<string, string>());

        var act = () => fix.UndoAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*geri alınamaz*");
    }

    [Fact]
    public void It_is_optional_and_not_part_of_the_repair_run()
    {
        new DiskCleanupFix([]).IncludedInRepair.Should().BeFalse();
    }

    private sealed class FakeTarget : ICleanupTarget
    {
        private readonly long _bytes;

        public FakeTarget(string id, string title, long bytes, bool needsAdmin)
        {
            Id = id;
            Title = title;
            _bytes = bytes;
            NeedsAdmin = needsAdmin;
        }

        public string Id { get; }

        public string Title { get; }

        public string Description => "";

        public bool NeedsAdmin { get; }

        public int Skipped { get; set; }

        public bool Throws { get; set; }

        public int CleanCalls { get; private set; }

        public long MeasureBytes(CancellationToken ct) => _bytes;

        public CleanupOutcome Clean(CancellationToken ct)
        {
            CleanCalls++;
            return Throws ? throw new IOException("erişim engellendi") : new CleanupOutcome(_bytes, Skipped);
        }
    }
}
