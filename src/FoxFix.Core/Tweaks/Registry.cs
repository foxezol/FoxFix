using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FoxFix.Core.Tweaks;

/// <summary>The two registry roots the Windows settings touch. Nothing else (classes root, other users) is ever written.</summary>
public enum RegHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Number, text, bytes, or "remove this value". <see cref="Other"/> only appears when reading a value of a type FoxFix does not write.</summary>
public enum RegKind
{
    DWord,
    String,
    Binary,
    Delete,
    Other,
}

/// <param name="Data">DWord: the number as text. String: the text. Binary: lower-case hex bytes separated by commas. Empty for Delete.</param>
public sealed record RegistryEntry(RegHive Hive, string Key, string Name, RegKind Kind, string Data);

public sealed record RegistryValueState(bool Exists, RegKind Kind, string Data)
{
    public static RegistryValueState Missing { get; } = new(false, RegKind.Delete, "");
}

/// <summary>The only door to the registry for the Windows settings. A fake stands in for it in tests.</summary>
public interface IRegistryStore
{
    RegistryValueState Read(RegHive hive, string key, string name);

    bool KeyExists(RegHive hive, string key);

    void Write(RegHive hive, string key, string name, RegKind kind, string data);

    /// <summary>Removes a value; no error when it is not there.</summary>
    void DeleteValue(RegHive hive, string key, string name);

    /// <summary>Removes a key only when nothing is left in it. Returns true when it was removed.</summary>
    bool DeleteKeyIfEmpty(RegHive hive, string key);
}

/// <summary>
/// The real registry (64-bit view). NOT exercised by automated tests because it changes the system; the logic around it is
/// tested with a fake. Needs a hand test in a VM snapshot.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemRegistryStore : IRegistryStore
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
            RegistryValueKind.Binary => new RegistryValueState(true, RegKind.Binary, TweakData.BytesToText((byte[])value)),
            _ => new RegistryValueState(true, RegKind.Other, ""),
        };
    }

    public bool KeyExists(RegHive hive, string key)
    {
        using var root = Open(hive);
        using var subKey = root.OpenSubKey(key);
        return subKey is not null;
    }

    public void Write(RegHive hive, string key, string name, RegKind kind, string data)
    {
        using var root = Open(hive);
        using var subKey = root.CreateSubKey(key, writable: true);

        switch (kind)
        {
            case RegKind.DWord:
                subKey.SetValue(name, int.Parse(data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegKind.String:
                subKey.SetValue(name, data, RegistryValueKind.String);
                break;
            case RegKind.Binary:
                subKey.SetValue(name, TweakData.TextToBytes(data), RegistryValueKind.Binary);
                break;
            default:
                throw new InvalidOperationException($"Bu tür yazılamaz: {kind}");
        }
    }

    public void DeleteValue(RegHive hive, string key, string name)
    {
        using var root = Open(hive);
        using var subKey = root.OpenSubKey(key, writable: true);
        subKey?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool DeleteKeyIfEmpty(RegHive hive, string key)
    {
        using var root = Open(hive);
        using (var subKey = root.OpenSubKey(key))
        {
            if (subKey is null || subKey.ValueCount > 0 || subKey.SubKeyCount > 0)
            {
                return false;
            }
        }

        root.DeleteSubKey(key, throwOnMissingSubKey: false);
        return true;
    }

    private static RegistryKey Open(RegHive hive) => RegistryKey.OpenBaseKey(
        hive == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
        RegistryView.Registry64);
}

/// <summary>How values are written as text, in the catalog and in the undo journal.</summary>
public static class TweakData
{
    public static string BytesToText(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    public static byte[] TextToBytes(string text) =>
        text.Length == 0
            ? []
            : text.Split(',', StringSplitOptions.TrimEntries).Select(p => byte.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>True when the value as it is now is exactly what the entry would write (or, for Delete, is not there).</summary>
    public static bool Matches(RegistryEntry entry, RegistryValueState state) =>
        entry.Kind == RegKind.Delete
            ? !state.Exists
            : state.Exists && state.Kind == entry.Kind && string.Equals(state.Data, entry.Data, StringComparison.Ordinal);
}
