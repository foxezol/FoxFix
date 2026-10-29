using FluentAssertions;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.Core.Tests;

public sealed class UndoJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-tests-" + Guid.NewGuid().ToString("N"));

    private string JournalPath => Path.Combine(_dir, "undo.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static UndoRecord Record(string fixId = "dns") => new(
        Guid.NewGuid(),
        fixId,
        DateTimeOffset.UtcNow,
        "DNS eski haline getirilecek",
        new Dictionary<string, string> { ["adapter"] = "Wi-Fi", ["oldDns"] = "dhcp" });

    [Fact]
    public async Task Missing_file_means_empty_journal()
    {
        var journal = new UndoJournal(JournalPath);

        (await journal.LoadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Appended_records_survive_a_reload()
    {
        var record = Record();
        await new UndoJournal(JournalPath).AppendAsync(record, CancellationToken.None);

        var loaded = await new UndoJournal(JournalPath).LoadAsync(CancellationToken.None);

        loaded.Should().ContainSingle();
        loaded[0].Id.Should().Be(record.Id);
        loaded[0].Data["oldDns"].Should().Be("dhcp");
    }

    [Fact]
    public async Task Remove_deletes_only_the_given_record()
    {
        var journal = new UndoJournal(JournalPath);
        var keep = Record("proxy");
        var drop = Record("dns");
        await journal.AppendAsync(keep, CancellationToken.None);
        await journal.AppendAsync(drop, CancellationToken.None);

        await journal.RemoveAsync(drop.Id, CancellationToken.None);

        (await journal.LoadAsync(CancellationToken.None)).Select(r => r.Id).Should().Equal(keep.Id);
    }

    [Fact]
    public async Task Corrupt_file_is_kept_aside_and_journal_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(JournalPath, "{ not json");
        var journal = new UndoJournal(JournalPath);

        var loaded = await journal.LoadAsync(CancellationToken.None);

        loaded.Should().BeEmpty();
        Directory.GetFiles(_dir, "undo.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_appends_do_not_lose_records()
    {
        var journal = new UndoJournal(JournalPath);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => journal.AppendAsync(Record(), CancellationToken.None)));

        (await journal.LoadAsync(CancellationToken.None)).Should().HaveCount(20);
    }
}
