using FluentAssertions;
using FoxFix.Core.Diagnostics;
using Xunit;

namespace FoxFix.Core.Tests;

public class DiagnosticsEngineTests
{
    private sealed class StubCheck : ICheck
    {
        private readonly Func<CheckResult> _result;

        public StubCheck(string id, Func<CheckResult> result)
        {
            Id = id;
            _result = result;
        }

        public string Id { get; }

        public string Title => Id;

        public int Runs { get; private set; }

        public Task<CheckResult> RunAsync(CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(_result());
        }
    }

    private static CheckResult Result(CheckStatus status) => new(status, status.ToString());

    [Fact]
    public async Task Stops_at_the_first_failed_layer()
    {
        var first = new StubCheck("a", () => Result(CheckStatus.Passed));
        var broken = new StubCheck("b", () => Result(CheckStatus.Failed));
        var never = new StubCheck("c", () => Result(CheckStatus.Passed));
        var engine = new DiagnosticsEngine([first, broken, never]);

        var report = await engine.RunAsync(null, CancellationToken.None);

        report.Reports.Select(r => r.CheckId).Should().Equal("a", "b");
        report.FirstFailure!.CheckId.Should().Be("b");
        report.Healthy.Should().BeFalse();
        never.Runs.Should().Be(0);
    }

    [Fact]
    public async Task Warnings_and_skips_do_not_stop_the_run()
    {
        var engine = new DiagnosticsEngine(
        [
            new StubCheck("a", () => Result(CheckStatus.Warning)),
            new StubCheck("b", () => Result(CheckStatus.Skipped)),
            new StubCheck("c", () => Result(CheckStatus.Passed)),
        ]);

        var report = await engine.RunAsync(null, CancellationToken.None);

        report.Reports.Should().HaveCount(3);
        report.Healthy.Should().BeTrue();
    }

    [Fact]
    public async Task A_crashing_check_becomes_an_error_and_the_run_continues()
    {
        var engine = new DiagnosticsEngine(
        [
            new StubCheck("a", () => throw new InvalidOperationException("boom")),
            new StubCheck("b", () => Result(CheckStatus.Passed)),
        ]);

        var report = await engine.RunAsync(null, CancellationToken.None);

        report.Reports.Should().HaveCount(2);
        report.Reports[0].Result.Status.Should().Be(CheckStatus.Error);
        report.Reports[0].Result.Summary.Should().Contain("boom");
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var engine = new DiagnosticsEngine([new StubCheck("a", () => Result(CheckStatus.Passed))]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => engine.RunAsync(null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Progress_is_reported_per_check()
    {
        var seen = new List<string>();
        var progress = new SyncProgress<CheckReport>(r => seen.Add(r.CheckId));
        var engine = new DiagnosticsEngine(
        [
            new StubCheck("a", () => Result(CheckStatus.Passed)),
            new StubCheck("b", () => Result(CheckStatus.Passed)),
        ]);

        await engine.RunAsync(progress, CancellationToken.None);

        seen.Should().Equal("a", "b");
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }
}
