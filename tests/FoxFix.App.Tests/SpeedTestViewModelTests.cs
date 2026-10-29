using System.Windows.Media.Imaging;
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

        vm.Status.Should().Be("[FX-6001] Hız ölçülemedi: sunucuya ulaşılamadı");
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

    private sealed class FakeCardClipboard : ISpeedCardClipboard
    {
        public List<SpeedCard> Copied { get; } = [];

        public Exception? Failure { get; set; }

        public void Copy(SpeedCard card)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Copied.Add(card);
        }
    }

    [Fact]
    public async Task Sharing_is_possible_only_after_a_result_and_copies_the_numbers_with_a_short_confirmation()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var clipboard = new FakeCardClipboard();
        var vm = new SpeedTestViewModel(new FakeSpeedTest(), clipboard);
        vm.ShareCommand.CanExecute(null).Should().BeFalse("there is nothing to share before a test");

        await vm.StartCommand.ExecuteAsync(null);

        vm.ShareCommand.CanExecute(null).Should().BeTrue();
        vm.ShareCommand.Execute(null);

        vm.ShareStatus.Should().Be("PNG kopyalandı");
        vm.HasShareStatus.Should().BeTrue();
        var card = clipboard.Copied.Should().ContainSingle().Subject;
        card.Download.Should().Contain("87,3");
        card.Latency.Should().Contain("24,5");

        await vm.StartCommand.ExecuteAsync(null);
        vm.ShareStatus.Should().BeEmpty("a new test starts without the old confirmation");
    }

    [Fact]
    public async Task A_busy_clipboard_is_reported_instead_of_crashing()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var vm = new SpeedTestViewModel(new FakeSpeedTest(), new FakeCardClipboard { Failure = new System.Runtime.InteropServices.COMException("pano meşgul") });
        await vm.StartCommand.ExecuteAsync(null);

        vm.ShareCommand.Execute(null);

        vm.ShareStatus.Should().StartWith("[FX-1004] Kopyalanamadı").And.Contain("pano meşgul");
    }

    [Fact]
    public void Without_a_clipboard_service_the_share_button_stays_off()
    {
        new SpeedTestViewModel(new FakeSpeedTest()).ShareCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void The_card_is_drawn_opaque_at_double_resolution_and_names_the_project()
    {
        BitmapSource? image = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                image = WpfSpeedCardClipboard.Render(new SpeedCard("24,5 Mb/sn", "3,2 Mb/sn", "87,3 ms", "21,9 ms", new DateTime(2026, 10, 29, 20, 15, 0)));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();

        image.Should().NotBeNull();
        image!.PixelWidth.Should().Be(1440);
        image.PixelHeight.Should().BeInRange(500, 1000);
        image.Format.Should().Be(System.Windows.Media.PixelFormats.Bgr32);
        WpfSpeedCardClipboard.RepoText.Should().Be("github.com/foxezol/FoxFix");

        if (Environment.GetEnvironmentVariable("FOXFIX_CARD_PNG") is { Length: > 0 } path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path);
            encoder.Save(file);
        }
    }
}