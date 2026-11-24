using FluentAssertions;
using FoxFix.Core.Overlay;
using FoxFix.Core.Settings;
using Xunit;

namespace FoxFix.Core.Tests;

public class PresentCounterTests
{
    private sealed class Clock
    {
        public TimeSpan Now { get; set; }
    }

    private static (PresentCounter Counter, Clock Clock) Create()
    {
        var clock = new Clock();
        return (new PresentCounter(() => clock.Now), clock);
    }

    private static void Draw(PresentCounter counter, Clock clock, int pid, int frames, double fps)
    {
        for (var i = 0; i < frames; i++)
        {
            counter.OnPresent(pid);
            clock.Now += TimeSpan.FromSeconds(1 / fps);
        }
    }

    [Fact]
    public void A_steady_program_gives_its_frame_rate_and_the_time_between_frames()
    {
        var (counter, clock) = Create();

        Draw(counter, clock, pid: 100, frames: 60, fps: 120);

        var stats = counter.Snapshot(0, _ => false)!;
        stats.ProcessId.Should().Be(100);
        stats.Fps.Should().BeApproximately(120, 1);
        stats.FrameMs.Should().BeApproximately(8.33, 0.1);
    }

    [Fact]
    public void Only_the_last_second_counts()
    {
        var (counter, clock) = Create();
        Draw(counter, clock, 100, frames: 100, fps: 30);
        Draw(counter, clock, 100, frames: 200, fps: 144);

        counter.Snapshot(0, _ => false)!.Fps.Should().BeApproximately(144, 3, "the slow start is more than a second ago");
    }

    [Fact]
    public void A_program_that_stops_drawing_drops_out_within_a_second()
    {
        var (counter, clock) = Create();
        Draw(counter, clock, 100, frames: 30, fps: 60);

        clock.Now += TimeSpan.FromSeconds(1.5);

        counter.Snapshot(0, _ => false).Should().BeNull();
    }

    [Fact]
    public void The_program_in_front_wins_even_when_another_draws_faster()
    {
        var (counter, clock) = Create();
        Draw(counter, clock, 100, frames: 30, fps: 60);
        clock.Now = TimeSpan.Zero;
        Draw(counter, clock, 200, frames: 100, fps: 200);

        counter.Snapshot(foregroundProcessId: 100, _ => false)!.ProcessId.Should().Be(100);
        counter.Snapshot(foregroundProcessId: 0, _ => false)!.ProcessId.Should().Be(200, "with none in front the busiest one is shown");
    }

    [Fact]
    public void The_desktop_itself_is_left_out()
    {
        var (counter, clock) = Create();
        Draw(counter, clock, 4, frames: 60, fps: 60);

        counter.Snapshot(0, pid => pid == 4).Should().BeNull();
    }

    [Fact]
    public void One_single_frame_is_not_a_rate()
    {
        var (counter, _) = Create();
        counter.OnPresent(100);

        counter.Snapshot(0, _ => false).Should().BeNull();
    }
}

public class LoadMathTests
{
    [Fact]
    public void The_processor_is_as_busy_as_the_time_it_was_not_idle()
    {
        var before = new CpuTimes(Idle: 1000, Kernel: 2000, User: 500);
        var now = new CpuTimes(Idle: 1750, Kernel: 3000, User: 1000);

        // Total 1500, idle 750: half busy.
        CpuTimes.Percent(before, now).Should().BeApproximately(50, 0.01);
    }

    [Fact]
    public void No_time_passed_or_a_counter_that_went_back_is_not_a_reading()
    {
        var a = new CpuTimes(10, 20, 5);

        CpuTimes.Percent(a, a).Should().BeNull();
        CpuTimes.Percent(a, new CpuTimes(5, 10, 2)).Should().BeNull();
    }

    [Fact]
    public void The_graphics_card_is_as_busy_as_its_busiest_engine_summed_over_programs()
    {
        var load = GpuLoad.Percent(
        [
            ("pid_100_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 30),
            ("pid_200_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 25),
            ("pid_100_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 10),
        ]);

        load.Should().Be(55);
    }

    [Fact]
    public void Nothing_listed_is_not_a_reading_and_a_sum_over_one_hundred_stops_at_one_hundred()
    {
        GpuLoad.Percent([]).Should().BeNull();
        GpuLoad.Percent([("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70), ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70)]).Should().Be(100);
    }
}

public class OverlaySettingsTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F10", true)]
    [InlineData("ctrl+alt+g", true)]
    [InlineData("Alt+1", true)]
    [InlineData("Win+Shift+F24", true)]
    [InlineData("F10", false)]
    [InlineData("Ctrl", false)]
    [InlineData("Ctrl+Shift", false)]
    [InlineData("Ctrl+A+B", false)]
    [InlineData("Ctrl+F25", false)]
    [InlineData("Ctrl+?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_shortcut_needs_a_modifier_and_exactly_one_known_key(string? text, bool valid)
    {
        OverlayHotkey.TryParse(text, out _).Should().Be(valid);
    }

    [Fact]
    public void A_shortcut_is_read_into_its_parts_and_written_in_a_fixed_order()
    {
        OverlayHotkey.TryParse("shift+ctrl+f10", out var parsed).Should().BeTrue();

        parsed.Modifiers.Should().Be(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        parsed.Key.Should().Be("F10");
        OverlayHotkey.Format(parsed.Modifiers, parsed.Key).Should().Be("Ctrl+Shift+F10");
    }

    [Fact]
    public void A_damaged_overlay_setting_goes_back_to_its_default()
    {
        var damaged = new OverlaySettings { Hotkey = "oops", Background = (OverlayBackground)99, Corner = (OverlayCorner)42, Scale = 99, FreeLeft = double.NaN, FreeTop = 120 };

        var fixedUp = damaged.Sanitized();

        fixedUp.Hotkey.Should().Be(OverlaySettings.DefaultHotkey);
        fixedUp.Background.Should().Be(OverlayBackground.Translucent);
        fixedUp.Corner.Should().Be(OverlayCorner.TopLeft);
        fixedUp.Scale.Should().Be(OverlaySettings.MaxScale, "a scale outside the slider's range is pulled back into it");
        fixedUp.FreeLeft.Should().BeNull();
        fixedUp.FreeTop.Should().Be(120);
        new OverlaySettings { Scale = 0.01 }.Sanitized().Scale.Should().Be(OverlaySettings.MinScale);
        new OverlaySettings { Scale = double.NaN }.Sanitized().Scale.Should().Be(1.0);
    }

    [Fact]
    public void The_overlay_is_off_by_default_and_the_choices_survive_the_settings_file()
    {
        new AppSettings().Overlay.Enabled.Should().BeFalse();

        var path = Path.Combine(Path.GetTempPath(), "foxfix-overlay-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new AppSettingsStore(path);
            var chosen = new OverlaySettings
            {
                Enabled = true, Hotkey = "Ctrl+Alt+G", ShowGpu = false, Background = OverlayBackground.None, Corner = OverlayCorner.Free, Scale = 1.6, FreeLeft = 300, FreeTop = 450,
            };

            store.Save(new AppSettings { Overlay = chosen }).Should().BeTrue();

            store.Load().Overlay.Should().Be(chosen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_old_settings_file_without_the_overlay_loads_with_its_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "foxfix-old-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"AutoCheckUpdates\": false}");

            var loaded = new AppSettingsStore(path).Load();

            loaded.AutoCheckUpdates.Should().BeFalse();
            loaded.Overlay.Should().Be(new OverlaySettings());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class OverlayServiceTests
{
    private sealed class Source(double? value) : IMetricSource
    {
        public int Reads { get; private set; }

        public double? Read()
        {
            Reads++;
            return value;
        }
    }

    private sealed class Feed : IPresentFeed
    {
        public Action<int>? OnPresent { get; private set; }

        public Exception? Throws { get; init; }

        public bool Disposed { get; private set; }

        public void Start(Action<int> onPresent)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            OnPresent = onPresent;
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Nothing_is_measured_before_it_starts_or_after_it_stops()
    {
        var cpu = new Source(40);
        var gpu = new Source(60);
        var service = new OverlayService(new Feed(), cpu, gpu);

        service.Sample(new OverlaySettings()).Should().Be(OverlayReading.Empty);
        cpu.Reads.Should().Be(0);
        gpu.Reads.Should().Be(0);
    }

    [Fact]
    public void Only_what_is_shown_is_read()
    {
        var cpu = new Source(40);
        var gpu = new Source(60);
        var service = new OverlayService(new Feed(), cpu, gpu);
        service.Start();
        var gpuReadsAfterStart = gpu.Reads;

        var reading = service.Sample(new OverlaySettings { ShowGpu = false, ShowFps = false, ShowFrameTime = false });

        reading.CpuPercent.Should().Be(40);
        reading.GpuPercent.Should().BeNull();
        gpu.Reads.Should().Be(gpuReadsAfterStart, "the graphics card is not asked while it is not shown");
    }

    [Fact]
    public void Frames_come_from_what_the_feed_reports()
    {
        var clock = TimeSpan.Zero;
        var feed = new Feed();
        var service = new OverlayService(feed, new Source(null), new Source(null), new PresentCounter(() => clock));
        service.Start();

        for (var i = 0; i < 30; i++)
        {
            feed.OnPresent!(777);
            clock += TimeSpan.FromMilliseconds(10);
        }

        var reading = service.Sample(new OverlaySettings());
        reading.Fps.Should().BeApproximately(100, 2);
        reading.FrameMs.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public void A_feed_that_cannot_start_is_reported_and_the_other_numbers_still_work()
    {
        var service = new OverlayService(new Feed { Throws = new UnauthorizedAccessException("needs administrator") }, new Source(33), new Source(44));

        service.Start();

        service.FeedError.Should().Contain("administrator");
        var reading = service.Sample(new OverlaySettings());
        reading.Fps.Should().BeNull();
        reading.CpuPercent.Should().Be(33);
        reading.GpuPercent.Should().Be(44);
    }

    [Fact]
    public void Stopping_closes_the_feed_and_starting_twice_does_nothing_more()
    {
        var feed = new Feed();
        var service = new OverlayService(feed, new Source(1), new Source(1));

        service.Start();
        service.Start();
        service.Stop();

        feed.Disposed.Should().BeTrue();
        service.IsRunning.Should().BeFalse();
    }
}
