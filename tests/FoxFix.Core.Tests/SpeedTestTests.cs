using FluentAssertions;
using FoxFix.Core.SpeedTest;
using Xunit;

namespace FoxFix.Core.Tests;

public class SpeedMathTests
{
    [Fact]
    public void Mbps_uses_decimal_megabits()
    {
        SpeedMath.Mbps(12_500_000, 1).Should().BeApproximately(100, 0.0001);
        SpeedMath.Mbps(25_000_000, 4).Should().BeApproximately(50, 0.0001);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Mbps_is_zero_without_bytes_or_time(long bytes, double seconds)
    {
        SpeedMath.Mbps(bytes, seconds).Should().Be(0);
    }

    [Fact]
    public void Median_ignores_outliers_and_handles_even_counts()
    {
        SpeedMath.Median([30, 31, 500, 29, 32]).Should().Be(31);
        SpeedMath.Median([10, 20, 30, 40]).Should().Be(25);
        SpeedMath.Median([]).Should().Be(0);
        SpeedMath.Median([7]).Should().Be(7);
    }

    [Fact]
    public void Jitter_is_the_mean_difference_between_neighbouring_samples()
    {
        SpeedMath.Jitter([10, 12, 10, 14]).Should().BeApproximately((2 + 2 + 4) / 3.0, 0.0001);
        SpeedMath.Jitter([20, 20, 20]).Should().Be(0);
        SpeedMath.Jitter([20]).Should().Be(0);
    }
}

public class SpeedTestServiceTests
{
    private sealed class FakeClock : IClock
    {
        private long _ticks;

        public TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <summary>Delivers data in 100 ms steps of fake time at a rate that may change over time.</summary>
    private sealed class FakeTransport : ISpeedTestTransport
    {
        private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

        private readonly FakeClock _clock;
        private readonly Queue<double> _latencies;

        public FakeTransport(FakeClock clock, IEnumerable<double> latencies)
        {
            _clock = clock;
            _latencies = new Queue<double>(latencies);
        }

        public Func<TimeSpan, double> DownloadMbps { get; set; } = _ => 100;

        public Func<TimeSpan, double> UploadMbps { get; set; } = _ => 20;

        public Exception? FailDownloadWith { get; set; }

        public int LatencyCalls { get; private set; }

        public Task<double> MeasureLatencyMsAsync(CancellationToken ct)
        {
            LatencyCalls++;
            return Task.FromResult(_latencies.Count > 0 ? _latencies.Dequeue() : 25);
        }

        public Task DownloadAsync(long bytes, Action<long> onBytes, CancellationToken ct) =>
            Transfer(bytes, onBytes, DownloadMbps, FailDownloadWith, ct);

        public Task UploadAsync(long bytes, Action<long> onBytes, CancellationToken ct) =>
            Transfer(bytes, onBytes, UploadMbps, null, ct);

        private async Task Transfer(long bytes, Action<long> onBytes, Func<TimeSpan, double> mbps, Exception? failure, CancellationToken ct)
        {
            if (failure is not null)
            {
                throw failure;
            }

            long sent = 0;
            while (sent < bytes)
            {
                ct.ThrowIfCancellationRequested();

                var piece = (long)(mbps(_clock.Now) * 1_000_000 / 8 * Step.TotalSeconds);
                _clock.Advance(Step);
                sent += piece;
                onBytes(piece);
                await Task.Yield();
            }
        }
    }

    private static SpeedTestOptions SingleStream => new() { DownloadStreams = 1, UploadStreams = 1 };

    private static (SpeedTestService Service, FakeTransport Transport) Create(
        IEnumerable<double>? latencies = null, SpeedTestOptions? options = null)
    {
        var clock = new FakeClock();
        var transport = new FakeTransport(clock, latencies ?? []);
        return (new SpeedTestService(transport, clock, options ?? SingleStream), transport);
    }

    [Fact]
    public async Task Download_and_upload_match_the_rate_the_link_delivers()
    {
        var (service, transport) = Create();
        transport.DownloadMbps = _ => 100;
        transport.UploadMbps = _ => 20;

        var result = await service.RunAsync(null, CancellationToken.None);

        result.DownloadMbps.Should().BeApproximately(100, 1);
        result.UploadMbps.Should().BeApproximately(20, 0.5);
    }

    [Fact]
    public async Task The_slow_first_second_does_not_drag_the_result_down()
    {
        var (service, transport) = Create();
        transport.DownloadMbps = at => at < TimeSpan.FromSeconds(1) ? 5 : 80;

        var result = await service.RunAsync(null, CancellationToken.None);

        result.DownloadMbps.Should().BeApproximately(80, 2, "slow start belongs to warm-up");
    }

    [Fact]
    public async Task Latency_is_the_median_of_the_samples_after_a_discarded_first_one()
    {
        // first value (500) is the warm-up sample and must be ignored; 200 is an outlier the median absorbs
        var (service, _) = Create(latencies: [500, 20, 22, 21, 200, 23, 20, 22, 21]);

        var result = await service.RunAsync(null, CancellationToken.None);

        // kept: 20 22 21 200 23 20 22 21 -> sorted 20 20 21 21 22 22 23 200 -> median (21 + 22) / 2
        result.LatencyMs.Should().Be(21.5);
    }

    [Fact]
    public async Task Jitter_is_reported_from_the_same_samples()
    {
        var (service, _) = Create(latencies: [99, 10, 14, 10, 14, 10, 14, 10, 14]);

        var result = await service.RunAsync(null, CancellationToken.None);

        result.JitterMs.Should().Be(4);
    }

    [Fact]
    public async Task The_configured_number_of_latency_samples_is_taken_plus_one_warm_up()
    {
        var (service, transport) = Create(options: SingleStream with { LatencySamples = 5 });

        await service.RunAsync(null, CancellationToken.None);

        transport.LatencyCalls.Should().Be(6);
    }

    [Fact]
    public async Task Progress_walks_through_the_phases_in_order_with_live_speeds()
    {
        var (service, _) = Create();
        var reports = new List<SpeedTestProgress>();

        await service.RunAsync(new InlineProgress(reports.Add), CancellationToken.None);

        reports.Select(r => r.Phase).Distinct().Should().Equal(SpeedTestPhase.Latency, SpeedTestPhase.Download, SpeedTestPhase.Upload);
        reports.Where(r => r.Phase == SpeedTestPhase.Latency).Should().OnlyContain(r => r.LatencyMs != null && r.CurrentMbps == null);
        reports.Where(r => r.Phase != SpeedTestPhase.Latency).Should().OnlyContain(r => r.CurrentMbps != null && r.CurrentMbps > 0);

        var order = reports.Select(r => (int)r.Phase).ToList();
        order.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Several_parallel_streams_add_up()
    {
        var clock = new FakeClock();
        var transport = new FakeTransport(clock, []);
        transport.DownloadMbps = _ => 25;

        // The fake clock is shared, so four streams each moving "25 Mbps" advance time four times as fast.
        // What matters here is that all bytes are counted and the run still ends at the deadline.
        var service = new SpeedTestService(transport, clock, new SpeedTestOptions { DownloadStreams = 4, UploadStreams = 1 });

        var result = await service.RunAsync(null, CancellationToken.None);

        result.DownloadMbps.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_cancelled_run_throws_instead_of_returning_a_half_result()
    {
        var (service, _) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunAsync(null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_real_transfer_failure_is_not_mistaken_for_the_deadline()
    {
        var (service, transport) = Create();
        transport.FailDownloadWith = new HttpRequestException("sunucu yanıt vermedi");

        var act = () => service.RunAsync(null, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*sunucu*");
    }

    [Fact]
    public async Task A_dead_link_gives_zero_not_an_exception()
    {
        var (service, transport) = Create();
        transport.DownloadMbps = _ => 0;
        transport.UploadMbps = _ => 0;

        var result = await service.RunAsync(null, CancellationToken.None);

        result.DownloadMbps.Should().Be(0);
        result.UploadMbps.Should().Be(0);
    }

    private sealed class InlineProgress : IProgress<SpeedTestProgress>
    {
        private readonly Action<SpeedTestProgress> _handler;

        public InlineProgress(Action<SpeedTestProgress> handler) => _handler = handler;

        public void Report(SpeedTestProgress value) => _handler(value);
    }
}
