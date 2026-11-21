using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoxFix.Core.Safety;

namespace FoxFix.Core.Tweaks;

public sealed record TweakKey(RegHive Hive, string Key);

public sealed record TweakSnapshotValue(RegHive Hive, string Key, string Name, bool Exists, RegKind Kind, string Data);

/// <summary>
/// How a set of registry values was before something changed them: each value (or that it was not there) and the keys that did not exist
/// yet. Putting it back writes the old values, deletes the values that were not there and removes the keys that were created, deepest
/// first and only when nothing else is in them. Used by the undo journal and by the saved Windows settings.
/// </summary>
public sealed record TweakSnapshot(IReadOnlyList<TweakSnapshotValue> Values, IReadOnlyList<TweakKey> CreatedKeys)
{
    // Names, not numbers: a saved snapshot stays readable (and valid) even if the enums are ever reordered.
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Null when a value that would be overwritten has a type FoxFix cannot write back exactly.</summary>
    public static TweakSnapshot? Take(IRegistryStore store, IEnumerable<RegistryEntry> entries)
    {
        var values = new List<TweakSnapshotValue>();
        var created = new List<TweakKey>();
        var seen = new HashSet<(RegHive, string, string)>();

        foreach (var entry in entries)
        {
            var state = store.Read(entry.Hive, entry.Key, entry.Name);
            if (state.Exists && state.Kind == RegKind.Other)
            {
                return null;
            }

            if (seen.Add((entry.Hive, entry.Key, entry.Name)))
            {
                values.Add(new TweakSnapshotValue(entry.Hive, entry.Key, entry.Name, state.Exists, state.Kind, state.Data));
            }

            // Keys that are not there yet are created by a write and have to be removed again by "Geri Al".
            if (entry.Kind != RegKind.Delete)
            {
                foreach (var key in MissingKeys(store, entry))
                {
                    if (!created.Contains(key))
                    {
                        created.Add(key);
                    }
                }
            }
        }

        return new TweakSnapshot(values, created);
    }

    private static IEnumerable<TweakKey> MissingKeys(IRegistryStore store, RegistryEntry entry)
    {
        var parts = entry.Key.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('\\', parts.Take(i));
            if (!store.KeyExists(entry.Hive, path))
            {
                yield return new TweakKey(entry.Hive, path);
            }
        }
    }

    /// <summary>Puts every value back as it was and removes the keys that were created. False when something could not be put back.</summary>
    public bool TryRestore(IRegistryStore store)
    {
        var ok = true;

        foreach (var v in Enumerable.Reverse(Values))
        {
            try
            {
                if (v.Exists)
                {
                    store.Write(v.Hive, v.Key, v.Name, v.Kind, v.Data);
                }
                else
                {
                    store.DeleteValue(v.Hive, v.Key, v.Name);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
            {
                ok = false;
            }
        }

        foreach (var key in Enumerable.Reverse(CreatedKeys))
        {
            try
            {
                store.DeleteKeyIfEmpty(key.Hive, key.Key);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>How many of the values are not as they are in this snapshot right now.</summary>
    public int CountDifferences(IRegistryStore store) =>
        Values.Count(v =>
        {
            var now = store.Read(v.Hive, v.Key, v.Name);
            return v.Exists
                ? !now.Exists || now.Kind != v.Kind || !string.Equals(now.Data, v.Data, StringComparison.Ordinal)
                : now.Exists;
        });

    public bool NeedsAdmin => Values.Any(v => v.Hive == RegHive.LocalMachine);

    public Dictionary<string, string> ToData(string id) => new()
    {
        ["tweak"] = id,
        ["values"] = JsonSerializer.Serialize(Values, Json),
        ["createdKeys"] = JsonSerializer.Serialize(CreatedKeys, Json),
    };

    public static TweakSnapshot FromData(UndoRecord record)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<TweakSnapshotValue>>(Field(record, "values"), Json);
            var keys = JsonSerializer.Deserialize<List<TweakKey>>(Field(record, "createdKeys"), Json);
            return new TweakSnapshot(values ?? throw new JsonException(), keys ?? throw new JsonException());
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, "values"));
        }
    }

    private static string Field(UndoRecord record, string key) =>
        record.Data.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(Strings.Format(Strings.UndoRecordBroken, key));
}
