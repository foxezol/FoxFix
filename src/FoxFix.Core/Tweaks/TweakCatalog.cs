using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoxFix.Core.Tweaks;

/// <param name="MinBuild">The lowest Windows build number it applies to; 0 for no limit.</param>
/// <param name="MaxBuild">The highest Windows build number it applies to; 0 for no limit.</param>
/// <param name="Defaults">What Windows has by default (Win11Debloat's own undo file), or null when that is not a plain value change.</param>
/// <param name="UndoTitle">The setting worded the other way round ("... aç"), for the line about going back to the default.</param>
public sealed record WindowsTweak(
    string Id,
    string? CategoryId,
    string Title,
    string Description,
    int MinBuild,
    int MaxBuild,
    IReadOnlyList<RegistryEntry> Entries,
    IReadOnlyList<RegistryEntry>? Defaults = null,
    string? UndoTitle = null)
{
    public bool AppliesTo(int build) => (MinBuild == 0 || build >= MinBuild) && (MaxBuild == 0 || build <= MaxBuild);

    public bool NeedsAdmin => Entries.Any(e => e.Hive == RegHive.LocalMachine);
}

public sealed record TweakCategory(string Id, string Title);

/// <summary>One choice of a group: what it is called and the settings that make it so.</summary>
public sealed record TweakOption(string Label, IReadOnlyList<string> TweakIds);

/// <summary>Settings of which only one can hold at a time (taskbar buttons, where Explorer opens...).</summary>
public sealed record TweakGroup(string Id, string CategoryId, string Title, string Description, IReadOnlyList<TweakOption> Options);

/// <summary>
/// The Windows settings FoxFix can change. They come from Win11Debloat (MIT, https://github.com/Raphire/Win11Debloat): plain
/// registry values, with its Turkish texts. Katalog.json is made by tools\win11debloat-katalog.ps1 and carried inside the program.
/// </summary>
public sealed class TweakCatalog
{
    public const string ResourceName = "Tweaks.Katalog.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, WindowsTweak> _byId;

    private TweakCatalog(IReadOnlyList<TweakCategory> categories, IReadOnlyList<WindowsTweak> tweaks, IReadOnlyList<TweakGroup> groups)
    {
        Categories = categories;
        Tweaks = tweaks;
        Groups = groups;
        _byId = tweaks.ToDictionary(t => t.Id);
        InGroup = groups.SelectMany(g => g.Options).SelectMany(o => o.TweakIds).ToHashSet();
        Toggles = tweaks.Where(t => !InGroup.Contains(t.Id)).ToList();
    }

    public IReadOnlyList<TweakCategory> Categories { get; }

    public IReadOnlyList<WindowsTweak> Tweaks { get; }

    public IReadOnlyList<TweakGroup> Groups { get; }

    /// <summary>The ids that are choices inside a group.</summary>
    public IReadOnlySet<string> InGroup { get; }

    /// <summary>The settings that stand alone: one tick box each.</summary>
    public IReadOnlyList<WindowsTweak> Toggles { get; }

    public WindowsTweak? Find(string id) => _byId.GetValueOrDefault(id);

    public static TweakCatalog Load()
    {
        using var stream = typeof(TweakCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Eksik kaynak: {ResourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static TweakCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<CatalogDto>(json, Options) ?? throw new JsonException("Katalog boş.");

        var categories = (dto.Categories ?? []).Select(c => new TweakCategory(c.Id, c.Title)).ToList();
        var tweaks = (dto.Tweaks ?? []).Select(t => new WindowsTweak(
            t.Id, t.Category, t.Title, t.Description ?? "", t.MinBuild, t.MaxBuild,
            (t.Entries ?? []).Select(ToEntry).ToList(),
            t.Defaults?.Select(ToEntry).ToList(), t.UndoTitle)).ToList();
        var groups = (dto.Groups ?? []).Select(g => new TweakGroup(
            g.Id, g.Category, g.Title, g.Description ?? "",
            (g.Options ?? []).Select(o => new TweakOption(o.Label, o.TweakIds ?? [])).ToList())).ToList();

        return new TweakCatalog(categories, tweaks, groups);
    }

    private static RegistryEntry ToEntry(EntryDto e) => new(e.Hive, e.Key, e.Name ?? "", e.Kind, e.Data ?? "");

    private sealed record CatalogDto(List<CategoryDto>? Categories, List<TweakDto>? Tweaks, List<GroupDto>? Groups);

    private sealed record CategoryDto(string Id, string Title);

    private sealed record TweakDto(string Id, string? Category, string Title, string? Description, int MinBuild, int MaxBuild, List<EntryDto>? Entries, List<EntryDto>? Defaults, string? UndoTitle);

    private sealed record EntryDto(RegHive Hive, string Key, string? Name, RegKind Kind, string? Data);

    private sealed record GroupDto(string Id, string Category, string Title, string? Description, List<OptionDto>? Options);

    private sealed record OptionDto(string Label, List<string>? TweakIds);
}
