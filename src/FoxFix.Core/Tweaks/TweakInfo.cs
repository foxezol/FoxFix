namespace FoxFix.Core.Tweaks;

/// <summary>What a setting mostly affects.</summary>
public enum TweakArea
{
    Speed,
    Privacy,
    Look,
    Comfort,
    Battery,
}

public enum ImpactLevel
{
    Low,
    Medium,
    High,
}

/// <param name="Percent">A rough guess of how much it matters in its area, 1 to 100. It is an estimate, not a measurement.</param>
/// <param name="Recommended">Safe and worthwhile for most people.</param>
public sealed record TweakInfo(TweakArea Area, int Percent, bool Recommended)
{
    public ImpactLevel Level => Percent >= 25 ? ImpactLevel.High : Percent >= 10 ? ImpactLevel.Medium : ImpactLevel.Low;

    public string AreaName => Area switch
    {
        TweakArea.Speed => "Hız",
        TweakArea.Privacy => "Gizlilik",
        TweakArea.Look => "Görünüm",
        TweakArea.Comfort => "Kullanım",
        _ => "Pil",
    };
}

/// <summary>
/// What each stand-alone setting is mostly about, how much it is thought to matter, and whether FoxFix recommends it. The
/// percentages are rough estimates (they differ from computer to computer and none was measured); the page says so. The
/// recommended ones are those that are safe, easy to understand and useful for most people; anything that switches a feature
/// off that many people use (notifications, location, Storage Sense...) is not.
/// </summary>
public static class TweakInfos
{
    private static readonly Dictionary<string, TweakInfo> All = new()
    {
        ["DisableTelemetry"] = new(TweakArea.Privacy, 60, true),
        ["DisableSuggestions"] = new(TweakArea.Comfort, 40, true),
        ["DisableNotifications"] = new(TweakArea.Comfort, 20, false),
        ["DisableLocationServices"] = new(TweakArea.Privacy, 35, false),
        ["DisableFindMyDevice"] = new(TweakArea.Privacy, 15, false),
        ["DisableLockscreenTips"] = new(TweakArea.Look, 10, true),
        ["DisableEdgeAds"] = new(TweakArea.Privacy, 25, true),
        ["DisableCopilot"] = new(TweakArea.Speed, 8, true),
        ["DisableRecall"] = new(TweakArea.Privacy, 50, true),
        ["DisableClickToDo"] = new(TweakArea.Privacy, 15, true),
        ["DisableAISvcAutoStart"] = new(TweakArea.Speed, 12, true),
        ["DisableDVR"] = new(TweakArea.Speed, 15, true),
        ["DisableStartRecommended"] = new(TweakArea.Look, 10, false),
        ["DisableStartPhoneLink"] = new(TweakArea.Look, 5, false),
        ["DisableBing"] = new(TweakArea.Speed, 20, true),
        ["DisableSettings365Ads"] = new(TweakArea.Comfort, 5, true),
        ["DisableSettingsHome"] = new(TweakArea.Look, 5, false),
        ["DisableEdgeAI"] = new(TweakArea.Privacy, 10, false),
        ["DisableChromeAI"] = new(TweakArea.Privacy, 10, false),
        ["DisablePaintAI"] = new(TweakArea.Privacy, 5, false),
        ["DisableNotepadAI"] = new(TweakArea.Privacy, 5, false),
        ["EnableDarkMode"] = new(TweakArea.Look, 30, false),
        ["RevertContextMenu"] = new(TweakArea.Comfort, 30, false),
        ["DisableMouseAcceleration"] = new(TweakArea.Comfort, 25, false),
        ["DisableStickyKeys"] = new(TweakArea.Comfort, 10, true),
        ["DisableWindowSnapping"] = new(TweakArea.Comfort, 5, false),
        ["DisableSnapAssist"] = new(TweakArea.Comfort, 5, false),
        ["DisableSnapLayouts"] = new(TweakArea.Comfort, 5, false),
        ["TaskbarAlignLeft"] = new(TweakArea.Look, 10, false),
        ["HideTaskview"] = new(TweakArea.Look, 5, false),
        ["HideChat"] = new(TweakArea.Look, 5, true),
        ["DisableStorageSense"] = new(TweakArea.Comfort, 10, false),
        ["DisableFastStartup"] = new(TweakArea.Comfort, 15, false),
        ["DisableBitlockerAutoEncryption"] = new(TweakArea.Speed, 8, false),
        ["DisableModernStandbyNetworking"] = new(TweakArea.Battery, 15, false),
        ["EnableEndTask"] = new(TweakArea.Comfort, 20, true),
        ["EnableLastActiveClick"] = new(TweakArea.Comfort, 10, false),
        ["ShowKnownFileExt"] = new(TweakArea.Comfort, 30, true),
        ["ShowHiddenFolders"] = new(TweakArea.Comfort, 15, false),
        ["HideHome"] = new(TweakArea.Look, 5, false),
        ["HideGallery"] = new(TweakArea.Look, 5, false),
        ["HideOnedrive"] = new(TweakArea.Look, 5, false),
        ["DisableTransparency"] = new(TweakArea.Speed, 8, false),
        ["DisableAnimations"] = new(TweakArea.Speed, 12, false),
        ["DisableUpdateASAP"] = new(TweakArea.Comfort, 10, false),
        ["PreventUpdateAutoReboot"] = new(TweakArea.Comfort, 20, true),
        ["DisableDriverUpdates"] = new(TweakArea.Comfort, 15, false),
        ["DisableDeviceAutoAppDownload"] = new(TweakArea.Privacy, 10, true),
        ["AddFoldersToThisPC"] = new(TweakArea.Comfort, 5, false),
        ["DisableBraveBloat"] = new(TweakArea.Privacy, 5, false),
    };

    public static IReadOnlyCollection<string> Ids => All.Keys;

    public static TweakInfo? For(string id) => All.GetValueOrDefault(id);

    public static bool IsRecommended(string id) => All.TryGetValue(id, out var info) && info.Recommended;
}
