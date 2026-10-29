using FluentAssertions;
using FoxFix.App;
using FoxFix.Core.SpeedTest;
using Xunit;

namespace FoxFix.App.Tests;

public class SpeedTestViewModelTests
{
    private sealed class FakeSpeedTest : ISpeedTest
    {
        public SpeedTestResult Result { get; set; } = new(24.5, 3.2, 87.34, 21.9);

        public List<SpeedTestProgress> Steps { get; } = [];

        public Exception? Failure { get; set; }

        public bool WaitForCancel { get; set; }

        public TaskCompletionSource Started { get; } = new();

        public async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress>? progress, CancellationToken ct)
        {
            Started.TrySetResult();
            foreach (var step in Steps)
            {
                progress?.Report(step);
            }

            if (WaitForCancel)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Failure is null ? Result : throw Failure;
        }
    }

    [Theory]
    [InlineData(87.34, "87,3")]
    [InlineData(5, "5")]
    [InlineData(0, "0")]
    [InlineData(100, "100")]
    [InlineData(940.6, "941")]
    [InlineData(1240.2, "1240")]
    public void Numbers_use_a_decimal_comma_and_few_digits(double value, string expected)
    {
        SpeedTestViewModel.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void Before_a_run_nothing_is_measured_and_start_is_available()
    {
        var vm = new SpeedTestViewModel(new FakeSpeedTest());

        vm.DownloadText.Should().Be("–");
        vm.HasResult.Should().BeFalse();
        vm.Status.Should().Be("Henüz ölçülmedi.");
        vm.StartCommand.CanExecute(null).Should().BeTrue();
        vm.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_finished_run_shows_every_number_with_its_unit()
    {
        var vm = new SpeedTestViewModel(new FakeSpeedTest());

        await vm.StartCommand.ExecuteAsync(null);

        vm.DownloadText.Should().Be("87,3 Mb/sn");
        vm.UploadText.Should().Be("21,9 Mb/sn");
        vm.LatencyText.Should().Be("24,5 ms");
        vm.JitterText.Should().Be("3,2 ms");
        vm.HasResult.Should().BeTrue();
        vm.IsRunning.Should().BeFalse();
        vm.Status.Should().Contain("yaklaşık");
    }

    [Fact]
    public async Task Live_values_are_shown_while_running_and_replaced_by_the_final_result()
    {
        // xUnit installs its own context, which would queue the progress calls like a UI thread does.
        // Without a context the view model runs them inline, which makes the order deterministic here.
        SynchronizationContext.SetSynchronizationContext(null);

        var test = new FakeSpeedTest();
        test.Steps.Add(new SpeedTestProgress(SpeedTestPhase.Latency, null, 30));
        test.Steps.Add(new SpeedTestProgress(SpeedTestPhase.Download, 55.5, null));
        var vm = new SpeedTestViewModel(test);
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SpeedTestViewModel.DownloadText))
            {
                seen.Add(vm.DownloadText);
            }
        };

        await vm.StartCommand.ExecuteAsync(null);

        seen.Should().Contain("55,5 Mb/sn", "the live value appeared");
        vm.DownloadText.Should().Be("87,3 Mb/sn", "and the final result replaced it");
    }

    [Fact]
    public async Task Progress_that_arrives_after_the_run_ended_never_overwrites_the_final_numbers()
    {
        var queue = new List<SendOrPostCallback>();
        SynchronizationContext.SetSynchronizationContext(new QueueingContext(queue));

        var test = new FakeSpeedTest();
        test.Steps.Add(new SpeedTestProgress(SpeedTestPhase.Download, 5, null));
        var vm = new SpeedTestViewModel(test);

        await vm.StartCommand.ExecuteAsync(null);
        foreach (var late in queue.ToList())
        {
            late(null);
        }

        vm.DownloadText.Should().Be("87,3 Mb/sn");
        vm.Status.Should().Contain("yaklaşık");
    }

    private sealed class QueueingContext : SynchronizationContext
    {
        private readonly List<SendOrPostCallback> _queue;

        public QueueingContext(List<SendOrPostCallback> queue) => _queue = queue;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add(_ => d(state));
    }

    [Fact]
    public async Task Start_is_blocked_while_a_run_is_in_progress_and_stop_is_offered()
    {
        var test = new FakeSpeedTest { WaitForCancel = true };
        var vm = new SpeedTestViewModel(test);

        var run = vm.StartCommand.ExecuteAsync(null);
        await test.Started.Task;

        vm.IsRunning.Should().BeTrue();
        vm.StartCommand.CanExecute(null).Should().BeFalse();
        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task Stopping_says_so_and_leaves_no_half_result()
    {
        var test = new FakeSpeedTest { WaitForCancel = true };
        var vm = new SpeedTestViewModel(test);

        var run = vm.StartCommand.ExecuteAsync(null);
        await test.Started.Task;
        vm.CancelCommand.Execute(null);
        await run;

        vm.Status.Should().Be("Ölçüm durduruldu.");
        vm.HasResult.Should().BeFalse();
        vm.IsRunning.Should().BeFalse();
        vm.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_failure_is_reported_in_words_not_thrown()
    {
        var vm = new SpeedTestViewModel(new FakeSpeedTest { Failure = new HttpRequestException("sunucuya ulaşılamadı") });

        await vm.StartCommand.ExecuteAsync(null);

        vm.Status.Should().Be("Hız ölçülemedi: sunucuya ulaşılamadı");
        vm.HasResult.Should().BeFalse();
        vm.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task A_second_run_clears_the_previous_numbers_first()
    {
        var test = new FakeSpeedTest();
        var vm = new SpeedTestViewModel(test);
        await vm.StartCommand.ExecuteAsync(null);

        test.Failure = new InvalidOperationException("kesildi");
        await vm.StartCommand.ExecuteAsync(null);

        vm.DownloadText.Should().Be("–");
        vm.HasResult.Should().BeFalse();
    }
}
