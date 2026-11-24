using FluentAssertions;
using FoxFix.Core.Devices;
using FoxFix.Core.Tweaks;
using Xunit;

namespace FoxFix.Core.Tests;

internal sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<(RegHive, string, string), RegistryValueState> _values = new();
    private readonly Dictionary<(RegHive, string), List<string>> _keys = new();

    public void Set(RegHive hive, string key, string name, string text) => _values[(hive, key, name)] = new RegistryValueState(true, RegKind.String, text);

    public void SetNumber(RegHive hive, string key, string name, int number) => _values[(hive, key, name)] = new RegistryValueState(true, RegKind.DWord, number.ToString());

    public void AddSubKey(RegHive hive, string key, string child)
    {
        if (!_keys.TryGetValue((hive, key), out var list))
        {
            _keys[(hive, key)] = list = [];
        }

        list.Add(child);
    }

    public RegistryValueState Read(RegHive hive, string key, string name) =>
        _values.GetValueOrDefault((hive, key, name), RegistryValueState.Missing);

    public IReadOnlyList<string> SubKeyNames(RegHive hive, string key) =>
        _keys.TryGetValue((hive, key), out var list) ? list : [];
}

internal sealed class FakeDeviceSource : IDeviceSource
{
    public List<DeviceInfo> Devices { get; } = [];

    public Exception? Throws { get; set; }

    public IReadOnlyList<DeviceInfo> List(DeviceKind kind) =>
        Throws is not null ? throw Throws : Devices.Where(d => d.Kind == kind).ToList();
}

public class AudioEndpointSourceTests
{
    private const string Capture = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";

    [Theory]
    [InlineData(1, DeviceState.Active)]
    [InlineData(2, DeviceState.Disabled)]
    [InlineData(4, DeviceState.NotPresent)]
    [InlineData(8, DeviceState.Unplugged)]
    [InlineData(3, DeviceState.Unknown)]
    [InlineData(0x10000001, DeviceState.Active)]
    [InlineData(0x10000004, DeviceState.NotPresent)]
    [InlineData(0x20000002, DeviceState.Disabled)]
    public void The_endpoint_state_number_becomes_a_state(int number, DeviceState expected)
    {
        AudioEndpointSource.MapState(new RegistryValueState(true, RegKind.DWord, number.ToString())).Should().Be(expected);
    }

    [Fact]
    public void A_missing_or_odd_state_is_unknown_not_a_guess()
    {
        AudioEndpointSource.MapState(RegistryValueState.Missing).Should().Be(DeviceState.Unknown);
        AudioEndpointSource.MapState(new RegistryValueState(true, RegKind.String, "1")).Should().Be(DeviceState.Unknown);
    }

    [Fact]
    public void Devices_that_are_disabled_or_unplugged_are_listed_with_their_names()
    {
        var registry = new FakeRegistryReader();
        registry.AddSubKey(RegHive.LocalMachine, Capture, "{a}");
        registry.AddSubKey(RegHive.LocalMachine, Capture, "{b}");
        registry.AddSubKey(RegHive.LocalMachine, Capture, "{c}");
        registry.SetNumber(RegHive.LocalMachine, Capture + @"\{a}", "DeviceState", 1);
        registry.SetNumber(RegHive.LocalMachine, Capture + @"\{b}", "DeviceState", 2);
        registry.SetNumber(RegHive.LocalMachine, Capture + @"\{c}", "DeviceState", 8);
        registry.Set(RegHive.LocalMachine, Capture + @"\{a}\Properties", "{a45c254e-df1c-4efd-8020-67d146a850e0},2", "Microphone");
        registry.Set(RegHive.LocalMachine, Capture + @"\{a}\Properties", "{b3f8fa53-0004-438e-9003-51a46e139bfc},6", "USB Audio");
        registry.Set(RegHive.LocalMachine, Capture + @"\{b}\Properties", "{a45c254e-df1c-4efd-8020-67d146a850e0},2", "Line In");

        var devices = new AudioEndpointSource(registry).List(DeviceKind.Microphone);

        devices.Select(d => (d.Name, d.State)).Should().Equal(
            ("Microphone (USB Audio)", DeviceState.Active),
            ("Line In", DeviceState.Disabled),
            ("{c}", DeviceState.Unplugged));
    }

    [Fact]
    public void Speakers_are_read_from_the_render_branch_and_not_mixed_with_microphones()
    {
        var registry = new FakeRegistryReader();
        registry.AddSubKey(RegHive.LocalMachine, Capture, "{mic}");

        new AudioEndpointSource(registry).List(DeviceKind.Speaker).Should().BeEmpty();
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class CameraStateTests
{
    [Theory]
    [InlineData(0u, DeviceState.Active)]
    [InlineData(22u, DeviceState.Disabled)]
    [InlineData(10u, DeviceState.Problem)]
    public void The_device_manager_error_code_becomes_a_state(uint code, DeviceState expected)
    {
        WmiCameraSource.MapErrorCode(code).Should().Be(expected);
    }

    [Fact]
    public void No_code_is_unknown()
    {
        WmiCameraSource.MapErrorCode(null).Should().Be(DeviceState.Unknown);
    }
}

public class PrivacyReaderTests
{
    private const string Consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string Policy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

    [Fact]
    public void Speakers_have_no_privacy_switch()
    {
        new PrivacyReader(new FakeRegistryReader()).Read(DeviceKind.Speaker).Should().Be(PrivacyAccess.NotApplicable);
    }

    [Fact]
    public void All_switches_on_means_allowed()
    {
        var registry = new FakeRegistryReader();
        registry.Set(RegHive.LocalMachine, Consent + @"\microphone", "Value", "Allow");
        registry.Set(RegHive.CurrentUser, Consent + @"\microphone", "Value", "Allow");
        registry.Set(RegHive.CurrentUser, Consent + @"\microphone\NonPackaged", "Value", "Allow");

        new PrivacyReader(registry).Read(DeviceKind.Microphone).Should().Be(PrivacyAccess.Allowed);
    }

    [Fact]
    public void The_user_switch_off_is_told_apart_from_the_desktop_app_switch_off()
    {
        var user = new FakeRegistryReader();
        user.Set(RegHive.CurrentUser, Consent + @"\webcam", "Value", "Deny");
        new PrivacyReader(user).Read(DeviceKind.Camera).Should().Be(PrivacyAccess.DeniedByUser);

        var desktop = new FakeRegistryReader();
        desktop.Set(RegHive.CurrentUser, Consent + @"\webcam", "Value", "Allow");
        desktop.Set(RegHive.CurrentUser, Consent + @"\webcam\NonPackaged", "Value", "Deny");
        new PrivacyReader(desktop).Read(DeviceKind.Camera).Should().Be(PrivacyAccess.DeniedForDesktopApps);
    }

    [Fact]
    public void A_machine_wide_deny_counts_as_denied()
    {
        var registry = new FakeRegistryReader();
        registry.Set(RegHive.LocalMachine, Consent + @"\microphone", "Value", "Deny");

        new PrivacyReader(registry).Read(DeviceKind.Microphone).Should().Be(PrivacyAccess.DeniedByUser);
    }

    [Fact]
    public void A_group_policy_that_forces_deny_wins()
    {
        var registry = new FakeRegistryReader();
        registry.SetNumber(RegHive.LocalMachine, Policy, "LetAppsAccessCamera", 2);
        registry.Set(RegHive.CurrentUser, Consent + @"\webcam", "Value", "Allow");

        new PrivacyReader(registry).Read(DeviceKind.Camera).Should().Be(PrivacyAccess.DeniedByPolicy);
    }

    [Fact]
    public void When_nothing_can_be_read_it_is_unknown_and_not_called_allowed()
    {
        new PrivacyReader(new FakeRegistryReader()).Read(DeviceKind.Microphone).Should().Be(PrivacyAccess.Unknown);
    }
}

public class DeviceCheckerTests
{
    private const string Consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private static (DeviceChecker Checker, FakeDeviceSource Devices, FakeRegistryReader Registry) Create()
    {
        var devices = new FakeDeviceSource();
        var registry = new FakeRegistryReader();
        registry.Set(RegHive.CurrentUser, Consent + @"\microphone", "Value", "Allow");
        registry.Set(RegHive.CurrentUser, Consent + @"\webcam", "Value", "Allow");
        return (new DeviceChecker(devices, new PrivacyReader(registry)), devices, registry);
    }

    private static DeviceInfo Dev(DeviceKind kind, DeviceState state, string name = "Device") => new(name + state, name, kind, state);

    [Fact]
    public void No_device_at_all_is_told_plainly()
    {
        var (checker, _, _) = Create();

        var result = checker.Check(DeviceKind.Camera);

        result.Verdict.Should().Be(DeviceVerdict.NoDevice);
        result.SettingsUri.Should().Be(DeviceSettingsLinks.Camera);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void A_device_that_is_only_disabled_is_not_called_missing()
    {
        var (checker, devices, _) = Create();
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Disabled));

        var result = checker.Check(DeviceKind.Microphone);

        result.Verdict.Should().Be(DeviceVerdict.Disabled);
        result.SettingsUri.Should().Be(DeviceSettingsLinks.Sound);
    }

    [Fact]
    public void An_unplugged_device_counts_as_no_usable_device()
    {
        var (checker, devices, _) = Create();
        devices.Devices.Add(Dev(DeviceKind.Speaker, DeviceState.Unplugged));

        checker.Check(DeviceKind.Speaker).Verdict.Should().Be(DeviceVerdict.NoDevice);
    }

    [Fact]
    public void A_working_device_with_the_privacy_switch_off_points_at_the_privacy_page()
    {
        var (checker, devices, registry) = Create();
        devices.Devices.Add(Dev(DeviceKind.Camera, DeviceState.Active));
        registry.Set(RegHive.CurrentUser, Consent + @"\webcam", "Value", "Deny");

        var result = checker.Check(DeviceKind.Camera);

        result.Verdict.Should().Be(DeviceVerdict.PrivacyBlocked);
        result.Privacy.Should().Be(PrivacyAccess.DeniedByUser);
        result.SettingsUri.Should().Be(DeviceSettingsLinks.CameraPrivacy);
    }

    [Fact]
    public void A_policy_block_offers_no_settings_page_because_the_page_cannot_undo_it()
    {
        var (checker, devices, registry) = Create();
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Active));
        registry.SetNumber(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMicrophone", 2);

        var result = checker.Check(DeviceKind.Microphone);

        result.Verdict.Should().Be(DeviceVerdict.PrivacyBlocked);
        result.SettingsUri.Should().BeNull();
    }

    [Fact]
    public void Several_devices_are_all_listed_and_one_active_one_is_enough()
    {
        var (checker, devices, _) = Create();
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Disabled, "A"));
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Active, "B"));
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Unplugged, "C"));

        var result = checker.Check(DeviceKind.Microphone);

        result.Verdict.Should().Be(DeviceVerdict.Ok);
        result.Devices.Should().HaveCount(3);
    }

    [Fact]
    public void Speakers_need_no_privacy_permission()
    {
        var (checker, devices, _) = Create();
        devices.Devices.Add(Dev(DeviceKind.Speaker, DeviceState.Active));

        var result = checker.Check(DeviceKind.Speaker);

        result.Verdict.Should().Be(DeviceVerdict.Ok);
        result.Privacy.Should().Be(PrivacyAccess.NotApplicable);
    }

    [Fact]
    public void When_windows_cannot_be_asked_the_answer_is_unknown_with_the_reason_and_never_a_diagnosis()
    {
        var (checker, devices, _) = Create();
        devices.Throws = new InvalidOperationException("WMI is unavailable");

        var result = checker.Check(DeviceKind.Camera);

        result.Verdict.Should().Be(DeviceVerdict.Unknown);
        result.Error.Should().Be("WMI is unavailable");
        result.Devices.Should().BeEmpty();
        result.SettingsUri.Should().BeNull();
    }

    [Fact]
    public void Unreadable_privacy_does_not_turn_a_working_device_into_a_problem()
    {
        var devices = new FakeDeviceSource();
        devices.Devices.Add(Dev(DeviceKind.Microphone, DeviceState.Active));
        var checker = new DeviceChecker(devices, new PrivacyReader(new FakeRegistryReader()));

        var result = checker.Check(DeviceKind.Microphone);

        result.Verdict.Should().Be(DeviceVerdict.Ok);
        result.Privacy.Should().Be(PrivacyAccess.Unknown);
    }

    [Fact]
    public void The_finding_texts_exist_in_both_languages_and_take_the_kind_name()
    {
        // The language is a global switch, so the test reads the two texts directly instead of changing it.
        foreach (var key in new[] { Strings.DevVerdictOk, Strings.DevVerdictNoDevice, Strings.DevVerdictDisabled, Strings.DevVerdictPrivacy, Strings.DevVerdictUnknown })
        {
            Strings.GetIn(key, Strings.English).Should().Contain("{0}");
            Strings.GetIn(key, Strings.Turkish).Should().Contain("{0}");
        }

        DeviceChecker.KindName(DeviceKind.Camera).Should().NotBeNullOrWhiteSpace();
    }
}
