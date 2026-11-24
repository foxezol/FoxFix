using System.Globalization;
using System.Runtime.Versioning;
using FoxFix.Core.Tweaks;
using Microsoft.Win32;

namespace FoxFix.Core.Devices;

public enum DeviceKind
{
    Speaker,
    Microphone,
    Camera,
}

/// <summary>What Windows says about one device. "Unknown" is never turned into a guess.</summary>
public enum DeviceState
{
    Active,
    Disabled,
    Unplugged,
    NotPresent,
    Problem,
    Unknown,
}

public sealed record DeviceInfo(string Id, string Name, DeviceKind Kind, DeviceState State);

/// <summary>Whether Windows' privacy settings let programs use the microphone or the camera. Only ever read, never changed.</summary>
public enum PrivacyAccess
{
    NotApplicable,
    Allowed,
    DeniedByUser,
    DeniedForDesktopApps,
    DeniedByPolicy,
    Unknown,
}

public enum DeviceVerdict
{
    Ok,
    NoDevice,
    Disabled,
    PrivacyBlocked,
    Unknown,
}

/// <param name="SettingsUri">The Windows Settings page that fits the finding (ms-settings:...), or null when there is nothing to open.</param>
/// <param name="Error">Why the check could not be made, when it could not (the verdict is then Unknown).</param>
public sealed record DeviceCheckResult(
    DeviceKind Kind,
    IReadOnlyList<DeviceInfo> Devices,
    PrivacyAccess Privacy,
    DeviceVerdict Verdict,
    string Message,
    string? SettingsUri,
    string? Error);

/// <summary>Lists the devices of one kind that Windows knows. Throws when Windows cannot be asked.</summary>
public interface IDeviceSource
{
    IReadOnlyList<DeviceInfo> List(DeviceKind kind);
}

/// <summary>Read-only look at the registry, and at the names of the keys below one.</summary>
public interface IRegistryReader
{
    RegistryValueState Read(RegHive hive, string key, string name);

    IReadOnlyList<string> SubKeyNames(RegHive hive, string key);
}

public static class DeviceSettingsLinks
{
    public const string Sound = "ms-settings:sound";
    public const string MicrophonePrivacy = "ms-settings:privacy-microphone";
    public const string CameraPrivacy = "ms-settings:privacy-webcam";
    public const string Camera = "ms-settings:camera";

    public static string For(DeviceKind kind, bool privacy) => (kind, privacy) switch
    {
        (DeviceKind.Microphone, true) => MicrophonePrivacy,
        (DeviceKind.Camera, true) => CameraPrivacy,
        (DeviceKind.Camera, false) => Camera,
        _ => Sound,
    };
}

[SupportedOSPlatform("windows")]
public sealed class SystemRegistryReader : IRegistryReader
{
    public RegistryValueState Read(RegHive hive, string key, string name)
    {
        using var root = Open(hive);
        using var subKey = root.OpenSubKey(key);
        var value = subKey?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (subKey is null || value is null)
        {
            return RegistryValueState.Missing;
        }

        return subKey.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => new RegistryValueState(true, RegKind.DWord, ((int)value).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String => new RegistryValueState(true, RegKind.String, (string)value),
            _ => new RegistryValueState(true, RegKind.Other, ""),
        };
    }

    public IReadOnlyList<string> SubKeyNames(RegHive hive, string key)
    {
        using var root = Open(hive);
        using var subKey = root.OpenSubKey(key);
        return subKey?.GetSubKeyNames() ?? [];
    }

    private static RegistryKey Open(RegHive hive) =>
        RegistryKey.OpenBaseKey(hive == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
}

/// <summary>
/// Sound devices as Windows keeps them in its endpoint list (MMDevices): this also shows devices that are disabled or unplugged,
/// which Windows' own "enabled devices only" lists leave out. Names are the description plus the adapter name when there is one.
/// </summary>
public sealed class AudioEndpointSource : IDeviceSource
{
    private const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";
    private const string DescriptionProperty = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string AdapterProperty = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    private readonly IRegistryReader _registry;

    public AudioEndpointSource(IRegistryReader registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<DeviceInfo> List(DeviceKind kind)
    {
        var branch = kind == DeviceKind.Microphone ? "Capture" : "Render";
        var key = $@"{Root}\{branch}";
        var devices = new List<DeviceInfo>();

        foreach (var id in _registry.SubKeyNames(RegHive.LocalMachine, key))
        {
            var state = _registry.Read(RegHive.LocalMachine, $@"{key}\{id}", "DeviceState");
            var properties = $@"{key}\{id}\Properties";
            var description = Text(_registry.Read(RegHive.LocalMachine, properties, DescriptionProperty));
            var adapter = Text(_registry.Read(RegHive.LocalMachine, properties, AdapterProperty));

            var name = (description, adapter) switch
            {
                (null, null) => id,
                (null, _) => adapter!,
                (_, null) => description!,
                _ => $"{description} ({adapter})",
            };

            devices.Add(new DeviceInfo(id, name, kind, MapState(state)));
        }

        return devices;
    }

    /// <summary>DEVICE_STATE_ACTIVE 1, DISABLED 2, NOTPRESENT 4, UNPLUGGED 8 (low four bits).</summary>
    public static DeviceState MapState(RegistryValueState state)
    {
        if (!state.Exists || state.Kind != RegKind.DWord || !int.TryParse(state.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return DeviceState.Unknown;
        }

        // Windows adds internal high bits to the number (0x10000001 is still "active"); only the low four bits are the state.
        return (value & 0xF) switch
        {
            1 => DeviceState.Active,
            2 => DeviceState.Disabled,
            4 => DeviceState.NotPresent,
            8 => DeviceState.Unplugged,
            _ => DeviceState.Unknown,
        };
    }

    private static string? Text(RegistryValueState value) =>
        value.Exists && value.Kind == RegKind.String && !string.IsNullOrWhiteSpace(value.Data) ? value.Data.Trim() : null;
}

/// <summary>Cameras (and the "Image" class that older webcam drivers use) as Windows' device manager sees them.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiCameraSource : IDeviceSource
{
    public IReadOnlyList<DeviceInfo> List(DeviceKind kind)
    {
        if (kind != DeviceKind.Camera)
        {
            return [];
        }

        var devices = new List<DeviceInfo>();
        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
        using var results = searcher.Get();

        foreach (var item in results)
        {
            using (item)
            {
                var name = item["Name"] as string;
                var id = item["PNPDeviceID"] as string ?? name;
                if (name is null || id is null)
                {
                    continue;
                }

                devices.Add(new DeviceInfo(id, name, DeviceKind.Camera, MapErrorCode(item["ConfigManagerErrorCode"])));
            }
        }

        return devices;
    }

    /// <summary>0 works, 22 is "disabled by the user"; anything else is a problem Windows reports.</summary>
    public static DeviceState MapErrorCode(object? code)
    {
        if (code is null)
        {
            return DeviceState.Unknown;
        }

        var number = Convert.ToUInt32(code, CultureInfo.InvariantCulture);
        return number switch
        {
            0 => DeviceState.Active,
            22 => DeviceState.Disabled,
            _ => DeviceState.Problem,
        };
    }
}

[SupportedOSPlatform("windows")]
public sealed class SystemDeviceSource : IDeviceSource
{
    private readonly AudioEndpointSource _audio = new(new SystemRegistryReader());
    private readonly WmiCameraSource _camera = new();

    public IReadOnlyList<DeviceInfo> List(DeviceKind kind) => kind == DeviceKind.Camera ? _camera.List(kind) : _audio.List(kind);
}

/// <summary>Reads Windows' privacy switches for the microphone and the camera. It never writes them.</summary>
public sealed class PrivacyReader
{
    private const string Consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string Policies = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

    private readonly IRegistryReader _registry;

    public PrivacyReader(IRegistryReader registry)
    {
        _registry = registry;
    }

    public PrivacyAccess Read(DeviceKind kind)
    {
        if (kind == DeviceKind.Speaker)
        {
            return PrivacyAccess.NotApplicable;
        }

        var capability = kind == DeviceKind.Microphone ? "microphone" : "webcam";
        var policyName = kind == DeviceKind.Microphone ? "LetAppsAccessMicrophone" : "LetAppsAccessCamera";

        // 2 = "force deny" in the group policy.
        var policy = _registry.Read(RegHive.LocalMachine, Policies, policyName);
        if (policy.Exists && policy.Kind == RegKind.DWord && policy.Data == "2")
        {
            return PrivacyAccess.DeniedByPolicy;
        }

        var machine = ConsentValue(RegHive.LocalMachine, capability);
        var user = ConsentValue(RegHive.CurrentUser, capability);
        var desktop = ConsentValue(RegHive.CurrentUser, capability + @"\NonPackaged");

        if (machine == "Deny" || user == "Deny")
        {
            return PrivacyAccess.DeniedByUser;
        }

        if (desktop == "Deny")
        {
            return PrivacyAccess.DeniedForDesktopApps;
        }

        // Nothing readable at all: say so instead of calling it allowed.
        return machine is null && user is null && desktop is null ? PrivacyAccess.Unknown : PrivacyAccess.Allowed;
    }

    private string? ConsentValue(RegHive hive, string capability)
    {
        var value = _registry.Read(hive, $@"{Consent}\{capability}", "Value");
        return value.Exists && value.Kind == RegKind.String ? value.Data : null;
    }
}

/// <summary>Puts what Windows says into one plain finding per device kind. Looking only: nothing is changed.</summary>
public sealed class DeviceChecker
{
    private readonly IDeviceSource _devices;
    private readonly PrivacyReader _privacy;

    public DeviceChecker(IDeviceSource devices, PrivacyReader privacy)
    {
        _devices = devices;
        _privacy = privacy;
    }

    public static string KindName(DeviceKind kind) => Strings.Get(kind switch
    {
        DeviceKind.Microphone => Strings.KindMicrophone,
        DeviceKind.Camera => Strings.KindCamera,
        _ => Strings.KindSpeaker,
    });

    public DeviceCheckResult Check(DeviceKind kind)
    {
        var name = KindName(kind);

        IReadOnlyList<DeviceInfo> devices;
        try
        {
            devices = _devices.List(kind);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Security.SecurityException
            or IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return Unknown(kind, ex.Message);
        }

        PrivacyAccess privacy;
        try
        {
            privacy = _privacy.Read(kind);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            privacy = PrivacyAccess.Unknown;
        }

        var active = devices.Any(d => d.State == DeviceState.Active);

        if (!active)
        {
            var verdict = devices.Any(d => d.State == DeviceState.Disabled) ? DeviceVerdict.Disabled : DeviceVerdict.NoDevice;
            var key = verdict == DeviceVerdict.Disabled ? Strings.DevVerdictDisabled : Strings.DevVerdictNoDevice;
            return new DeviceCheckResult(kind, devices, privacy, verdict, Strings.VerdictFor(key, name), DeviceSettingsLinks.For(kind, privacy: false), null);
        }

        if (privacy is PrivacyAccess.DeniedByUser or PrivacyAccess.DeniedForDesktopApps or PrivacyAccess.DeniedByPolicy)
        {
            // A group policy cannot be undone from the settings page, so only the other two get a link.
            var link = privacy == PrivacyAccess.DeniedByPolicy ? null : DeviceSettingsLinks.For(kind, privacy: true);
            return new DeviceCheckResult(kind, devices, privacy, DeviceVerdict.PrivacyBlocked, Strings.VerdictFor(Strings.DevVerdictPrivacy, name), link, null);
        }

        return new DeviceCheckResult(kind, devices, privacy, DeviceVerdict.Ok, Strings.VerdictFor(Strings.DevVerdictOk, name), null, null);
    }

    private static DeviceCheckResult Unknown(DeviceKind kind, string error) =>
        new(kind, [], PrivacyAccess.Unknown, DeviceVerdict.Unknown, Strings.VerdictFor(Strings.DevVerdictUnknown, KindName(kind)), null, error);
}
