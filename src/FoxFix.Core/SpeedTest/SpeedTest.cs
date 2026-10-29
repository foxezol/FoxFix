using System.Diagnostics;

namespace FoxFix.Core.SpeedTest;

public enum SpeedTestPhase
{
    Latency,
    Download,
    Upload,
}

/// <param name="CurrentMbps">Live speed while a transfer phase runs, null during the latency phase.</param>
/// <param name="LatencyMs">Latest latency sample during the latency phase.</param>
public sealed record SpeedTestProgress(SpeedTestPhase Phase, double? CurrentMbps, double? LatencyMs);

public sealed record SpeedTestResult(double LatencyMs, double JitterMs, double DownloadMbps, double UploadMbps);

public sealed record SpeedTestOptions
{
    public int LatencySamples { get; init; } = 8;

    /// <summary>TCP slow start makes the first second unrepresentative, so it is left out of the result.</summary>
    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan DownloadDuration { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan UploadDuration { get; init; } = TimeSpan.FromSeconds(6);

    public int DownloadStreams { get; init; } = 4;

    public int UploadStreams { get; init; } = 3;

    public long DownloadChunkBytes { get; init; } = 25_000_000;

    public long UploadChunkBytes { get; init; } = 10_000_000;
}

public interface IClock
{
    /// <summary>Time since an arbitrary start. Only differences matter.</summary>
    TimeSpan Now { get; }
}

public sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Now => _stopwatch.Elapsed;
}

/// <summary>The network side of the test, so the maths and timing can be tested without a network.</summary>
public interface ISpeedTestTransport
{
    /// <summary>One round-trip sample in milliseconds.</summary>
    Task<double> MeasureLatencyMsAsync(CancellationToken ct);

    /// <summary>Downloads up to <paramref name="bytes"/>, reporting each received piece through <paramref name="onBytes"/>.</summary>
    Task DownloadAsync(long bytes, Action<long> onBytes, CancellationToken ct);

    Task UploadAsync(long bytes, Action<long> onBytes, CancellationToken ct);
}

public interface ISpeedTest
{
    Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress>? progress, CancellationToken ct);
}

public static class SpeedMath
{
    /// <summary>Megabits per second (decimal megabits, as ISPs quote them).</summary>
    public static double Mbps(long bytes, double seconds) => seconds <= 0 ? 0 : bytes * 8.0 / seconds / 1_000_000.0;

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Mean absolute difference between consecutive samples.</summary>
    public static double Jitter(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        return Enumerable.Range(1, values.Count - 1).Average(i => Math.Abs(values[i] - values[i - 1]));
    }
}

/// <summary>Measures latency, download and upload. Each transfer phase is time-boxed and uses several parallel streams.</summary>
public sealed class SpeedTestService : ISpeedTest
{
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250);

    private readonly ISpeedTestTransport _transport;
    private readonly IClock _clock;
    private readonly SpeedTestOptions _options;

    public SpeedTestService(ISpeedTestTransport transport, IClock clock, SpeedTestOptions? options = null)
    {
        _transport = transport;
        _clock = clock;
        _options = options ?? new SpeedTestOptions();
    }

    public async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress>? progress, CancellationToken ct)
    {
        var (latency, jitter) = await MeasureLatencyAsync(progress, ct);

        var download = await MeasureThroughputAsync(
            SpeedTestPhase.Download, _options.DownloadStreams, _options.DownloadDuration,
            _options.DownloadChunkBytes, _transport.DownloadAsync, progress, ct);

        var upload = await MeasureThroughputAsync(
            SpeedTestPhase.Upload, _options.UploadStreams, _options.UploadDuration,
            _options.UploadChunkBytes, _transport.UploadAsync, progress, ct);

        return new SpeedTestResult(latency, jitter, download, upload);
    }

    private async Task<(double Median, double Jitter)> MeasureLatencyAsync(IProgress<SpeedTestProgress>? progress, CancellationToken ct)
    {
        var samples = new List<double>();

        // One extra sample is thrown away: it pays for DNS and route warm-up.
        for (var i = 0; i <= _options.LatencySamples; i++)
        {
            ct.ThrowIfCancellationRequested();

            var sample = await _transport.MeasureLatencyMsAsync(ct);
            if (i == 0)
            {
                continue;
            }

            samples.Add(sample);
            progress?.Report(new SpeedTestProgress(SpeedTestPhase.Latency, null, sample));
        }

        return (SpeedMath.Median(samples), SpeedMath.Jitter(samples));
    }

    private async Task<double> MeasureThroughputAsync(
        SpeedTestPhase phase,
        int streams,
        TimeSpan duration,
        long chunkBytes,
        Func<long, Action<long>, CancellationToken, Task> transfer,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Real-time safety net for stalled connections. The normal stop is the clock deadline below.
        stop.CancelAfter(duration + TimeSpan.FromSeconds(10));

        var start = _clock.Now;
        var deadline = start + duration;
        var gate = new object();
        long total = 0;
        long totalAtWarmup = -1;
        var warmupAt = TimeSpan.Zero;
        var lastReport = start;

        void OnBytes(long count)
        {
            lock (gate)
            {
                total += count;
                var now = _clock.Now;

                if (totalAtWarmup < 0 && now - start >= _options.Warmup)
                {
                    totalAtWarmup = total;
                    warmupAt = now;
                }

                if (now - lastReport >= ReportEvery)
                {
                    lastReport = now;
                    progress?.Report(new SpeedTestProgress(phase, SpeedMath.Mbps(total, (now - start).TotalSeconds), null));
                }

                if (now >= deadline)
                {
                    stop.Cancel();
                }
            }
        }

        async Task Worker()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await transfer(chunkBytes, OnBytes, stop.Token);
                }
            }
            catch (Exception) when (stop.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The deadline cut the transfer short. That is how every phase normally ends.
            }
            catch
            {
                stop.Cancel(); // a real failure: stop the other streams too
                throw;
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, streams)).Select(_ => Worker()));
        ct.ThrowIfCancellationRequested();

        lock (gate)
        {
            var end = _clock.Now;
            return totalAtWarmup >= 0 && end > warmupAt
                ? SpeedMath.Mbps(total - totalAtWarmup, (end - warmupAt).TotalSeconds)
                : SpeedMath.Mbps(total, (end - start).TotalSeconds);
        }
    }
}
