using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class ProgressBarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-bar-" + Guid.NewGuid().ToString("N"));

    public ProgressBarTests()
    {
        Directory.CreateDirectory(_dir);
        SynchronizationContext.SetSynchronizationContext(null);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Elevated : IElevation
    {
        public bool IsElevated => true;

        public bool RestartElevated(string? arguments = null) => true;
    }

    private sealed class NoReboot : IRebootService
    {
        public Task<bool> ScheduleRestartAsync(int delaySeconds, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> CancelAsync(CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>Reports half way and then done, like a fix that can tell.</summary>
    private sealed class Reporting(string id) : IFix
    {
        public string Id => id;

        public string Title => id;

        public bool IncludedInRepair => false;

        public bool RequiresAdmin => false;

        public Task<FixPreview> PreviewAsync(CancellationToken ct) => Task.FromResult(new FixPreview(["x " + id]));

        public Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct) => Task.FromResult(new FixResult(true, "ok"));

        public Task<FixResult> ApplyAsync(UndoJournal journal, IProgress<FixProgress>? progress, CancellationToken ct)
        {
            progress?.Report(new FixProgress(0.5, "yarısı"));
            return ApplyAsync(journal, ct);
        }

        public Task UndoAsync(UndoRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Silent(string id) : IFix
    {
        public string Id => id;

        public string Title => id;

        public bool IncludedInRepair => false;

        public bool RequiresAdmin => false;

        public Task<FixPreview> PreviewAsync(CancellationToken ct) => Task.FromResult(new FixPreview(["x " + id]));

        public Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct) => Task.FromResult(new FixResult(true, "ok"));

        public Task UndoAsync(UndoRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private FixFlowViewModel Create(params IFix[] fixes) =>
        new(fixes, new UndoJournal(Path.Combine(_dir, "undo.json")), new Elevated(), new NoReboot());

    [Theory]
    [InlineData(0.0, 0, "%0")]
    [InlineData(0.42, 1, "%42")]
    [InlineData(0.5, 10, "%50 · yaklaşık 10 saniye kaldı")]
    [InlineData(0.1, 30, "%10 · yaklaşık 4 dakika 30 saniye kaldı")]
    [InlineData(0.9, 90, "%90 · yaklaşık 10 saniye kaldı")]
    [InlineData(1.0, 60, "%100")]
    [InlineData(0.02, 60, "%2")]
    public void The_line_under_the_bar_says_how_far_and_roughly_how_long_is_left(double fraction, int elapsedSeconds, string expected)
    {
        ProgressEstimate.Format(fraction, TimeSpan.FromSeconds(elapsedSeconds)).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "birkaç saniye")]
    [InlineData(5, "birkaç saniye")]
    [InlineData(18, "20 saniye")]
    [InlineData(60, "1 dakika")]
    [InlineData(61, "1 dakika 5 saniye")]
    public void Time_left_is_rounded_to_five_seconds(int seconds, string expected)
    {
        ProgressEstimate.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public async Task A_single_fix_without_progress_keeps_the_bar_moving_instead_of_showing_a_wrong_percentage()
    {
        var flow = Create(new Silent("a"));
        await flow.StartSingleCommand.ExecuteAsync("a");
        flow.IsIndeterminate.Should().BeTrue();

        flow.IsConfirmed = true;
        SynchronizationContext.SetSynchronizationContext(null); // an awaited call above may have put xUnit's context back
        await flow.ApplyCommand.ExecuteAsync(null);

        flow.HasProgress.Should().BeFalse();
    }

    [Fact]
    public async Task A_single_fix_that_reports_fills_the_bar_with_its_own_numbers()
    {
        var flow = Create(new Reporting("a"));
        var seen = new List<(double Percent, string Text)>();
        flow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.ProgressText) && flow.HasProgress)
            {
                seen.Add((flow.ProgressPercent, flow.ProgressText));
            }
        };

        await flow.StartSingleCommand.ExecuteAsync("a");
        flow.IsConfirmed = true;
        SynchronizationContext.SetSynchronizationContext(null); // an awaited call above may have put xUnit's context back
        await flow.ApplyCommand.ExecuteAsync(null);

        seen.Should().Contain(s => s.Percent == 50 && s.Text.StartsWith("yarısı · %50"));
        flow.IsIndeterminate.Should().BeFalse();
    }

    [Fact]
    public async Task Several_fixes_move_the_bar_a_step_per_finished_fix()
    {
        var flow = Create(new Silent("a"), new Silent("b"), new Silent("c"), new Silent("d"));
        var percents = new List<double>();
        flow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.ProgressPercent))
            {
                percents.Add(flow.ProgressPercent);
            }
        };

        await flow.StartManyAsync(["a", "b", "c", "d"]);
        flow.IsConfirmed = true;
        SynchronizationContext.SetSynchronizationContext(null); // an awaited call above may have put xUnit's context back
        await flow.ApplyCommand.ExecuteAsync(null);

        percents.Should().ContainInOrder(25.0, 50.0, 75.0, 100.0);
        flow.ProgressText.Should().BeEmpty("the line under the bar goes away when the work is done");
    }

    [Fact]
    public async Task Progress_of_one_fix_stays_inside_its_own_share_of_the_bar()
    {
        var flow = Create(new Reporting("a"), new Silent("b"));
        var percents = new List<double>();
        flow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.ProgressPercent))
            {
                percents.Add(flow.ProgressPercent);
            }
        };

        await flow.StartManyAsync(["a", "b"]);
        flow.IsConfirmed = true;
        SynchronizationContext.SetSynchronizationContext(null); // an awaited call above may have put xUnit's context back
        await flow.ApplyCommand.ExecuteAsync(null);

        percents.Should().ContainInOrder(25.0, 50.0, 100.0);
    }
}
