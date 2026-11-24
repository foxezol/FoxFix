using FluentAssertions;
using FoxFix.App;
using FoxFix.Core;
using FoxFix.Core.Overlay;
using FoxFix.Core.Settings;
using Xunit;

namespace FoxFix.App.Tests;

public sealed class OverlayViewModelTests : IDisposable
{
    private sealed class FakeHost : IOverlayHost
    {
        public List<string> Log { get; } = [];

        public OverlaySettings? Shown { get; private set; }

        public OverlayReading? Drawn { get; private set; }

        public Action? Pressed { get; private set; }

        public HashSet<string> Taken { get; } = [];

        public Func<Task>? Tick { get; private set; }

        public bool TimerRunning { get; private set; }

        public void Show(OverlaySettings settings)
        {
            Shown = settings;
            Log.Add("show");
        }

        public void Hide()
        {
            Shown = null;
            Log.Add("hide");
        }

        public void Update(OverlayReading reading, OverlaySettings settings) => Drawn = reading;

        public bool RegisterHotkey(HotkeyModifiers modifiers, string key, Action pressed)
        {
            var text = OverlayHotkey.Format(modifiers, key);
            if (Taken.Contains(text))
            {
                return false;
            }

            Pressed = pressed;
            Log.Add("register " + text);
            return true;
        }

        public void UnregisterHotkey()
        {
            Pressed = null;
            Log.Add("unregister");
        }

        public void StartTimer(Func<Task> tick)
        {
            Tick = tick;
            TimerRunning = true;
        }

        public void StopTimer() => TimerRunning = false;

        public Action<double, double>? Placed { get; private set; }

        public bool Placing { get; private set; }

        public void BeginPlacing(OverlaySettings settings, Action<double, double> placed)
        {
            Placing = true;
            Placed = placed;
            Log.Add("place");
        }

        public void EndPlacing(bool keepShown)
        {
            Placing = false;
            Log.Add(keepShown ? "endplace-keep" : "endplace");
        }
    }

    private sealed class Feed : IPresentFeed
    {
        public bool Disposed { get; private set; }

        public void Start(Action<int> onPresent)
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Source(double? value) : IMetricSource
    {
        public double? Read() => value;
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "foxfix-overlay-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host = new();
    private readonly List<Feed> _feeds = [];

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_dir, "ayarlar.json");

    private (OverlayViewModel Vm, SettingsViewModel Settings) Create(OverlaySettings? initial = null)
    {
        var settings = new SettingsViewModel(new AppSettingsStore(FilePath), new AppSettings { Overlay = initial ?? new OverlaySettings() }, _ => { });
        var vm = new OverlayViewModel(settings, _host, () =>
        {
            var feed = new Feed();
            _feeds.Add(feed);
            return new OverlayService(feed, new Source(25), new Source(60));
        });
        return (vm, settings);
    }

    [Fact]
    public void It_starts_off_and_takes_nothing_from_windows()
    {
        var (vm, _) = Create();
        vm.Start();

        vm.Enabled.Should().BeFalse();
        vm.IsVisible.Should().BeFalse();
        _host.Log.Should().BeEmpty("the shortcut is not taken while the overlay is off");
    }

    [Fact]
    public void Switching_it_on_takes_the_shortcut_and_switching_it_off_gives_it_back()
    {
        var (vm, _) = Create();

        vm.Enabled = true;
        vm.Enabled = false;

        _host.Log.Should().Equal("register Ctrl+Shift+F10", "unregister");
    }

    [Fact]
    public void The_shortcut_shows_and_hides_the_overlay_and_measuring_runs_only_while_it_is_shown()
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true });
        vm.Start();

        _host.Pressed!();
        vm.IsVisible.Should().BeTrue();
        _host.Shown.Should().NotBeNull();
        _host.TimerRunning.Should().BeTrue();

        _host.Pressed!();
        vm.IsVisible.Should().BeFalse();
        _host.TimerRunning.Should().BeFalse("nothing is measured while it is hidden");
        _feeds.Should().ContainSingle().Which.Disposed.Should().BeTrue("the listening session ends with it");
    }

    [Fact]
    public void Showing_it_while_it_is_switched_off_says_so_and_does_nothing()
    {
        var (vm, _) = Create();

        vm.ToggleVisibleCommand.Execute(null);

        vm.IsVisible.Should().BeFalse();
        vm.Message.Should().Be(Strings.Get(Strings.OverlayTurnOnFirst));
        _host.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_beat_draws_the_numbers_for_what_is_chosen()
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true, ShowFps = false, ShowFrameTime = false, ShowCpu = false });
        vm.Start();
        _host.Pressed!();

        await _host.Tick!();

        _host.Drawn.Should().NotBeNull();
        _host.Drawn!.CpuPercent.Should().BeNull("what is not shown is not read");
        _host.Drawn.GpuPercent.Should().Be(60);
    }

    [Fact]
    public void A_choice_is_saved_and_an_overlay_on_screen_follows_it_at_once()
    {
        var (vm, settings) = Create(new OverlaySettings { Enabled = true });
        vm.Start();
        _host.Pressed!();

        vm.IsNoBackground = true;
        vm.IsBottomRight = true;
        vm.ShowGpu = false;

        _host.Shown!.Background.Should().Be(OverlayBackground.None);
        _host.Shown.Corner.Should().Be(OverlayCorner.BottomRight);
        _host.Shown.ShowGpu.Should().BeFalse();
        settings.Overlay.Background.Should().Be(OverlayBackground.None);
        new AppSettingsStore(FilePath).Load().Overlay.Corner.Should().Be(OverlayCorner.BottomRight);
    }

    [Fact]
    public void The_size_is_a_percentage_from_the_slider_and_is_saved()
    {
        var (vm, settings) = Create(new OverlaySettings { Enabled = true, Scale = 1.5 });

        vm.ScalePercent.Should().Be(150);
        vm.ScalePercent = 80;

        vm.ScaleText.Should().Be(Strings.Format(Strings.OverlayScaleValue, 80));
        settings.Overlay.Scale.Should().BeApproximately(0.8, 0.0001);
        new AppSettingsStore(FilePath).Load().Overlay.Scale.Should().BeApproximately(0.8, 0.0001);
    }

    [Fact]
    public void Placing_it_freely_shows_the_strip_for_dragging_and_keeps_where_it_was_dropped()
    {
        var (vm, settings) = Create();

        vm.PlaceCommand.Execute(null);

        vm.IsPlacing.Should().BeTrue();
        vm.IsFree.Should().BeTrue("placing it makes the free place the choice");
        _host.Placing.Should().BeTrue();

        _host.Placed!(310, 220);
        vm.PlaceCommand.Execute(null);

        vm.IsPlacing.Should().BeFalse();
        _host.Placing.Should().BeFalse();
        settings.Overlay.Corner.Should().Be(OverlayCorner.Free);
        settings.Overlay.FreeLeft.Should().Be(310);
        settings.Overlay.FreeTop.Should().Be(220);
        new AppSettingsStore(FilePath).Load().Overlay.FreeTop.Should().Be(220);
    }

    [Fact]
    public void Choosing_a_fixed_corner_ends_the_dragging()
    {
        var (vm, _) = Create();
        vm.PlaceCommand.Execute(null);

        vm.IsTopRight = true;

        vm.IsPlacing.Should().BeFalse();
        vm.Corner.Should().Be(OverlayCorner.TopRight);
    }

    [Fact]
    public void Showing_the_overlay_or_closing_the_app_ends_the_dragging()
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true });
        vm.Start();
        vm.PlaceCommand.Execute(null);

        vm.ToggleVisibleCommand.Execute(null);

        vm.IsPlacing.Should().BeFalse();
        vm.IsVisible.Should().BeTrue();

        vm.PlaceCommand.Execute(null);
        vm.Shutdown();
        vm.IsPlacing.Should().BeFalse();
    }

    [Fact]
    public void A_radio_button_being_unticked_changes_nothing()
    {
        var (vm, _) = Create();

        vm.IsThemed = false;

        vm.Background.Should().Be(OverlayBackground.Translucent);
    }

    [Fact]
    public void A_valid_free_shortcut_replaces_the_old_one()
    {
        var (vm, settings) = Create(new OverlaySettings { Enabled = true });
        vm.Start();

        vm.TrySetHotkey("Ctrl+Alt+G").Should().BeTrue();

        vm.Hotkey.Should().Be("Ctrl+Alt+G");
        settings.Overlay.Hotkey.Should().Be("Ctrl+Alt+G");
        _host.Log.Should().Contain("register Ctrl+Alt+G");
    }

    [Fact]
    public void A_shortcut_another_program_has_is_refused_and_the_old_one_stays_in_use()
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true });
        vm.Start();
        _host.Taken.Add("Ctrl+Alt+G");

        vm.TrySetHotkey("Ctrl+Alt+G").Should().BeFalse();

        vm.Hotkey.Should().Be("Ctrl+Shift+F10");
        vm.Message.Should().Contain("Ctrl+Alt+G");
        _host.Log.Last().Should().Be("register Ctrl+Shift+F10", "the working one is taken again");
    }

    [Theory]
    [InlineData("F10")]
    [InlineData("Ctrl")]
    [InlineData("")]
    public void A_shortcut_that_is_not_valid_is_refused(string text)
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true });
        vm.Start();

        vm.TrySetHotkey(text).Should().BeFalse();

        vm.Message.Should().Be(Strings.Get(Strings.OverlayHotkeyInvalid));
        vm.Hotkey.Should().Be("Ctrl+Shift+F10");
    }

    [Fact]
    public void A_shortcut_taken_by_another_program_at_start_is_said_not_hidden()
    {
        _host.Taken.Add("Ctrl+Shift+F10");
        var (vm, _) = Create(new OverlaySettings { Enabled = true });

        vm.Start();

        vm.Message.Should().Contain("Ctrl+Shift+F10");
    }

    [Fact]
    public void Closing_the_app_hides_the_overlay_and_gives_the_shortcut_back()
    {
        var (vm, _) = Create(new OverlaySettings { Enabled = true });
        vm.Start();
        _host.Pressed!();

        vm.Shutdown();

        vm.IsVisible.Should().BeFalse();
        _host.Log.TakeLast(2).Should().Equal("hide", "unregister");
    }

    [Fact]
    public void Overlay_keys_map_between_text_and_wpf_keys()
    {
        WpfOverlayHost.ToWpfKey("F10").Should().Be(System.Windows.Input.Key.F10);
        WpfOverlayHost.ToWpfKey("G").Should().Be(System.Windows.Input.Key.G);
        WpfOverlayHost.ToWpfKey("1").Should().Be(System.Windows.Input.Key.D1);
        WpfOverlayHost.ToKeyName(System.Windows.Input.Key.D1).Should().Be("1");
        WpfOverlayHost.ToKeyName(System.Windows.Input.Key.F12).Should().Be("F12");
        WpfOverlayHost.ToKeyName(System.Windows.Input.Key.Space).Should().BeNull();
    }
}
