using FluentAssertions;
using FoxFix.App;
using FoxFix.Core;
using FoxFix.Core.Devices;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.App.Tests;

public class DevicesViewModelTests
{
    private const string Consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private sealed class Devices : IDeviceSource
    {
        public List<DeviceInfo> All { get; } = [];

        public Exception? Throws { get; set; }

        public IReadOnlyList<DeviceInfo> List(DeviceKind kind) => Throws is not null ? throw Throws : All.Where(d => d.Kind == kind).ToList();
    }

    private sealed class Registry : IRegistryReader
    {
        public Dictionary<string, string> Values { get; } = new();

        public RegistryValueState Read(RegHive hive, string key, string name) =>
            Values.TryGetValue($"{hive}|{key}|{name}", out var v) ? new RegistryValueState(true, RegKind.String, v) : RegistryValueState.Missing;

        public IReadOnlyList<string> SubKeyNames(RegHive hive, string key) => [];
    }

    private sealed class Tone : IToneTester
    {
        public int Played { get; private set; }

        public int Stopped { get; private set; }

        public Exception? Throws { get; set; }

        public TaskCompletionSource Finish { get; } = new();

        public async Task PlayAsync(CancellationToken ct)
        {
            Played++;
            if (Throws is not null)
            {
                throw Throws;
            }

            await Finish.Task;
        }

        public void Stop()
        {
            Stopped++;
            Finish.TrySetResult();
        }
    }

    private readonly Devices _devices = new();
    private readonly Registry _registry = new();
    private readonly Tone _tone = new();
    private readonly List<string> _opened = [];

    private DevicesViewModel Create()
    {
        _registry.Values[$"CurrentUser|{Consent}\\microphone|Value"] = "Allow";
        _registry.Values[$"CurrentUser|{Consent}\\webcam|Value"] = "Allow";

        return new DevicesViewModel(
            new DeviceChecker(_devices, new PrivacyReader(_registry)), _tone, _opened.Add);
    }

    private static DeviceInfo Dev(DeviceKind kind, DeviceState state, string name = "Device") => new(name, name, kind, state);

    private static async Task GoTo(DevicesViewModel vm, DeviceKind kind)
    {
        while (vm.Current.Kind != kind)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task Entering_the_page_looks_at_all_three_kinds_and_changes_nothing()
    {
        _devices.All.Add(Dev(DeviceKind.Speaker, DeviceState.Active, "Speakers"));
        _devices.All.Add(Dev(DeviceKind.Microphone, DeviceState.Disabled, "Mic"));
        var vm = Create();

        await vm.OnEnteredAsync();

        vm.Steps[0].VerdictKind.Should().Be(StatusKind.Ok);
        vm.Steps[1].VerdictKind.Should().Be(StatusKind.Bad);
        vm.Steps[2].VerdictKind.Should().Be(StatusKind.Warn, "no camera at all is a finding, not a failure to read");
        vm.Steps[1].Devices.Should().ContainSingle().Which.StateText.Should().Be(Strings.Get(Strings.DevStateDisabled));
        _tone.Played.Should().Be(0, "nothing is played or listened to until the person asks");
        _opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Devices_Windows_only_remembers_are_not_listed_but_the_others_are_and_counted()
    {
        _devices.All.Add(Dev(DeviceKind.Microphone, DeviceState.Active, "Now"));
        _devices.All.Add(Dev(DeviceKind.Microphone, DeviceState.NotPresent, "Long gone"));
        _devices.All.Add(Dev(DeviceKind.Microphone, DeviceState.Unplugged, "Pulled out"));
        var vm = Create();

        await vm.OnEnteredAsync();

        var step = vm.Steps[1];
        step.Devices.Select(d => d.Name).Should().Equal("Now", "Pulled out");
        step.DevicesHeader.Should().Be(Strings.Format(Strings.DevDevicesHeaderCount, 2));
    }

    [Fact]
    public async Task A_device_list_that_cannot_be_read_says_could_not_be_checked_with_the_code()
    {
        _devices.Throws = new InvalidOperationException("WMI down");
        var vm = Create();

        await vm.OnEnteredAsync();

        var step = vm.Steps[2];
        step.VerdictText.Should().Contain("[FX-7001]").And.Contain("WMI down");
        step.VerdictKind.Should().Be(StatusKind.Neutral, "not read means not diagnosed");
        step.HasDevices.Should().BeFalse();
        step.SettingsUri.Should().BeNull();
    }

    [Fact]
    public async Task A_privacy_block_offers_the_matching_windows_page_and_opening_it_goes_through_the_opener()
    {
        _devices.All.Add(Dev(DeviceKind.Microphone, DeviceState.Active));
        var vm = Create();
        _registry.Values[$"CurrentUser|{Consent}\\microphone|Value"] = "Deny";

        await vm.OnEnteredAsync();

        var step = vm.Steps[1];
        step.VerdictKind.Should().Be(StatusKind.Bad);
        step.PrivacyText.Should().Be(Strings.Get(Strings.DevPrivacyDeniedUser));
        step.SettingsUri.Should().Be(DeviceSettingsLinks.MicrophonePrivacy);
        step.SettingsLabel.Should().Be(Strings.Get(Strings.DevOpenMicPrivacy));

        vm.OpenSettingsCommand.Execute(step.SettingsUri);

        _opened.Should().Equal(DeviceSettingsLinks.MicrophonePrivacy);
    }

    [Fact]
    public async Task Only_windows_settings_links_can_be_opened()
    {
        var vm = Create();
        await vm.OnEnteredAsync();

        vm.OpenSettingsCommand.Execute("https://example.com");
        vm.OpenSettingsCommand.Execute("calc.exe");
        vm.OpenSettingsCommand.Execute(null);

        _opened.Should().BeEmpty();
    }

    [Fact]
    public async Task A_settings_page_that_cannot_open_is_reported()
    {
        var vm = new DevicesViewModel(
            new DeviceChecker(_devices, new PrivacyReader(_registry)), _tone,
            _ => throw new System.ComponentModel.Win32Exception("no handler"));

        vm.OpenSettingsCommand.Execute(DeviceSettingsLinks.Sound);

        vm.ActionError.Should().Contain("no handler");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_steps_go_back_and_forth_and_the_edges_are_closed()
    {
        var vm = Create();

        vm.CanGoBack.Should().BeFalse();
        vm.PreviousCommand.CanExecute(null).Should().BeFalse();
        vm.StepText.Should().Be(Strings.Format(Strings.DevStepOf, 1, 3));

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        vm.Current.Kind.Should().Be(DeviceKind.Camera);
        vm.CanGoNext.Should().BeFalse();
        vm.NextCommand.CanExecute(null).Should().BeFalse();

        await vm.PreviousCommand.ExecuteAsync(null);

        vm.Current.Kind.Should().Be(DeviceKind.Microphone);
    }

    [Fact]
    public async Task The_test_sound_plays_only_when_asked_and_ends_with_a_question_to_the_person()
    {
        var vm = Create();
        var step = vm.Current;

        var running = vm.StartTestCommand.ExecuteAsync(null);
        step.IsTesting.Should().BeTrue();
        step.TestStatus.Should().Be(Strings.Get(Strings.DevToneRunning));
        _tone.Finish.SetResult();
        await running;

        step.IsTesting.Should().BeFalse();
        step.TestStatus.Should().Be(Strings.Get(Strings.DevToneDone));
        _tone.Played.Should().Be(1);
    }

    [Fact]
    public async Task The_test_sound_can_be_stopped_at_once()
    {
        var vm = Create();
        var running = vm.StartTestCommand.ExecuteAsync(null);

        await vm.StopTestCommand.ExecuteAsync(null);
        await running;

        _tone.Stopped.Should().BeGreaterThan(0);
        vm.Current.IsTesting.Should().BeFalse();
        vm.Current.TestStatus.Should().Be(Strings.Get(Strings.DevStopped));
    }

    [Fact]
    public async Task A_speaker_test_that_cannot_play_says_so_without_crashing()
    {
        _tone.Throws = new InvalidOperationException("no output device");
        var vm = Create();

        await vm.StartTestCommand.ExecuteAsync(null);

        vm.Current.TestStatus.Should().Contain("no output device");
        vm.Current.TestKind.Should().Be(StatusKind.Bad);
        vm.Current.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_speakers_have_a_test_and_microphone_and_camera_are_never_switched_on()
    {
        var vm = Create();
        await GoTo(vm, DeviceKind.Microphone);

        await vm.StartTestCommand.ExecuteAsync(null);

        vm.Current.IsTesting.Should().BeFalse();
        vm.Current.TestStatus.Should().BeEmpty();

        await GoTo(vm, DeviceKind.Camera);
        await vm.StartTestCommand.ExecuteAsync(null);

        vm.Current.IsTesting.Should().BeFalse();
        _tone.Played.Should().Be(0, "the sound test belongs to the speakers only");
    }

    [Fact]
    public async Task Leaving_the_page_stops_the_test_sound()
    {
        var vm = Create();
        var running = vm.StartTestCommand.ExecuteAsync(null);
        await Task.Yield();

        await vm.OnLeftAsync();
        await running;

        vm.Current.IsTesting.Should().BeFalse();
        vm.Current.TestStatus.Should().BeEmpty("a test the person walked away from leaves no result behind");
        _tone.Stopped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Pressing_start_twice_does_not_restart_the_test_sound()
    {
        var vm = Create();
        var first = vm.StartTestCommand.ExecuteAsync(null);
        await Task.Yield();

        await vm.StartTestCommand.ExecuteAsync(null);

        _tone.Played.Should().Be(1);
        _tone.Stopped.Should().Be(0);
        await vm.StopTestCommand.ExecuteAsync(null);
        await first;
    }

    [Fact]
    public void States_and_privacy_have_words_and_a_colour_each()
    {
        foreach (var state in Enum.GetValues<DeviceState>())
        {
            DevicesViewModel.StateText(state).Should().NotBeNullOrWhiteSpace();
        }

        DevicesViewModel.PrivacyLine(PrivacyAccess.NotApplicable).Text.Should().BeEmpty();
        DevicesViewModel.PrivacyLine(PrivacyAccess.Unknown).Kind.Should().Be(StatusKind.Neutral);
        DevicesViewModel.PrivacyLine(PrivacyAccess.DeniedByPolicy).Kind.Should().Be(StatusKind.Bad);
    }

    [Fact]
    public void The_test_tone_is_a_valid_short_wave_file()
    {
        var wave = SoundPlayerToneTester.BuildWave();

        System.Text.Encoding.ASCII.GetString(wave, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wave, 8, 4).Should().Be("WAVE");
        wave.Length.Should().BeInRange(44 + 44100, 44 + 44100 * 4, "a second or two of 16-bit mono audio");
        BitConverter.ToInt16(wave, 44).Should().Be(0, "it starts silent, so it never clicks");
    }
}
