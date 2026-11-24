using System.Diagnostics;

namespace FoxFix.Core.Overlay;

public enum OverlayBackground
{
    /// <summary>A dark (or light) card with some see-through, so the game shows behind it.</summary>
    Translucent,

    /// <summary>The same solid card the app uses.</summary>
    Themed,

    /// <summary>No background at all: only the numbers, with a thin outline so they stay readable.</summary>
    None,
}

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,

    /// <summary>Wherever the person dragged it (see <see cref="OverlaySettings.FreeLeft"/> and <see cref="OverlaySettings.FreeTop"/>).</summary>
    Free,
}

/// <summary>What the person chose for the game overlay. Every value has a safe default, so a missing or damaged file is harmless.</summary>
public sealed record OverlaySettings
{
    public const string DefaultHotkey = "Ctrl+Shift+F10";

    /// <summary>The overlay and its shortcut exist while this is on. Turned off, nothing is measured and the shortcut is free again.</summary>
    public bool Enabled { get; init; }

    /// <summary>For example "Ctrl+Shift+F10": shows and hides the overlay while <see cref="Enabled"/> is on.</summary>
    public string Hotkey { get; init; } = DefaultHotkey;

    public bool ShowFps { get; init; } = true;

    public bool ShowFrameTime { get; init; } = true;

    public bool ShowGpu { get; init; } = true;

    public bool ShowCpu { get; init; } = true;

    public OverlayBackground Background { get; init; } = OverlayBackground.Translucent;

    public OverlayCorner Corner { get; init; } = OverlayCorner.TopLeft;

    /// <summary>1 is the normal size; the slider on the page goes from 50 % to 250 %.</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>Where the strip's top left corner is on the screen in free mode; null until it was placed once.</summary>
    public double? FreeLeft { get; init; }

    public double? FreeTop { get; init; }

    public const double MinScale = 0.5;

    public const double MaxScale = 2.5;

    public bool AnythingShown => ShowFps || ShowFrameTime || ShowGpu || ShowCpu;

    /// <summary>The same settings with anything out of range put back to its default (a hand-edited file, for example).</summary>
    public OverlaySettings Sanitized() => this with
    {
        Hotkey = OverlayHotkey.TryParse(Hotkey, out _) ? Hotkey : DefaultHotkey,
        Background = Enum.IsDefined(Background) ? Background : OverlayBackground.Translucent,
        Corner = Enum.IsDefined(Corner) ? Corner : OverlayCorner.TopLeft,
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, MinScale, MaxScale) : 1.0,
        FreeLeft = FreeLeft is { } left && double.IsFinite(left) ? left : null,
        FreeTop = FreeTop is { } top && double.IsFinite(top) ? top : null,
    };
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>"Ctrl+Shift+F10": at least one modifier and exactly one key, so a shortcut never takes over a plain key.</summary>
public static class OverlayHotkey
{
    public static bool TryParse(string? text, out (HotkeyModifiers Modifiers, string Key) hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null || !IsKeyName(part))
                    {
                        return false;
                    }

                    key = part.ToUpperInvariant();
                    break;
            }
        }

        if (key is null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        hotkey = (modifiers, key);
        return true;
    }

    /// <summary>A letter, a digit or F1 to F24.</summary>
    private static bool IsKeyName(string name)
    {
        if (name.Length == 1)
        {
            return char.IsAsciiLetterOrDigit(name[0]);
        }

        return name.Length is 2 or 3
            && (name[0] is 'F' or 'f')
            && int.TryParse(name.AsSpan(1), out var number)
            && number is >= 1 and <= 24;
    }

    /// <summary>The text for a shortcut: modifiers in a fixed order, then the key.</summary>
    public static string Format(HotkeyModifiers modifiers, string key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToUpperInvariant());
        return string.Join('+', parts);
    }
}

/// <summary>One second's numbers. A value that could not be read is null and is shown as "N/A"; nothing is guessed.</summary>
public sealed record OverlayReading(double? Fps, double? FrameMs, double? GpuPercent, double? CpuPercent)
{
    public static OverlayReading Empty { get; } = new(null, null, null, null);
}

/// <summary>Frames per second and the average time between two frames, for one program.</summary>
public sealed record PresentStats(int ProcessId, double Fps, double FrameMs);

/// <summary>
/// Counts the moments programs put a finished picture on the screen and turns them into frames per second. Only the last second
/// counts, so a program that stops drawing drops out within a second.
/// </summary>
public sealed class PresentCounter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Dictionary<int, Queue<TimeSpan>> _presents = [];
    private readonly Func<TimeSpan> _clock;

    public PresentCounter(Func<TimeSpan>? clock = null)
    {
        var stopwatch = Stopwatch.StartNew();
        _clock = clock ?? (() => stopwatch.Elapsed);
    }

    public void OnPresent(int processId)
    {
        var now = _clock();
        lock (_gate)
        {
            if (!_presents.TryGetValue(processId, out var queue))
            {
                queue = new Queue<TimeSpan>();
                _presents[processId] = queue;
            }

            queue.Enqueue(now);
            Trim(queue, now);
        }
    }

    /// <summary>
    /// The program that is drawing: the one in front when it has been drawing, otherwise the one that drew most.
    /// <paramref name="ignore"/> leaves out the desktop itself and FoxFix.
    /// </summary>
    public PresentStats? Snapshot(int foregroundProcessId, Func<int, bool> ignore)
    {
        var now = _clock();
        lock (_gate)
        {
            PresentStats? best = null;
            PresentStats? front = null;

            foreach (var (pid, queue) in _presents.ToList())
            {
                Trim(queue, now);
                if (queue.Count == 0)
                {
                    _presents.Remove(pid);
                    continue;
                }

                if (queue.Count < 2 || ignore(pid))
                {
                    continue;
                }

                var span = (queue.Last() - queue.First()).TotalSeconds;
                if (span <= 0)
                {
                    continue;
                }

                var stats = new PresentStats(pid, (queue.Count - 1) / span, span / (queue.Count - 1) * 1000);
                if (pid == foregroundProcessId)
                {
                    front = stats;
                }

                if (best is null || stats.Fps > best.Fps)
                {
                    best = stats;
                }
            }

            return front ?? best;
        }
    }

    private static void Trim(Queue<TimeSpan> queue, TimeSpan now)
    {
        while (queue.Count > 0 && now - queue.Peek() > Window)
        {
            queue.Dequeue();
        }
    }
}

/// <summary>What the processor did since the last look. The numbers are the ones Windows keeps, as 100 ns units.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    /// <summary>Kernel time already contains the idle time, so the busy part is what is left of kernel plus user.</summary>
    public static double? Percent(CpuTimes before, CpuTimes now)
    {
        if (now.Kernel < before.Kernel || now.User < before.User || now.Idle < before.Idle)
        {
            return null;
        }

        var total = (now.Kernel - before.Kernel) + (now.User - before.User);
        if (total == 0)
        {
            return null;
        }

        var idle = now.Idle - before.Idle;
        return Math.Clamp((1.0 - (double)idle / total) * 100, 0, 100);
    }
}

public static class GpuLoad
{
    /// <summary>
    /// Windows lists the load per program and per engine ("pid_123_luid_..._phys_0_eng_0_engtype_3D"). The load of one engine is the
    /// sum over all programs; the graphics card's load is the busiest engine, as Task Manager shows it.
    /// </summary>
    public static double? Percent(IEnumerable<(string Name, double Utilization)> instances)
    {
        var engines = new Dictionary<string, double>();

        foreach (var (name, utilization) in instances)
        {
            var at = name.IndexOf("_luid_", StringComparison.Ordinal);
            var engine = at >= 0 ? name[(at + 1)..] : name;
            engines[engine] = engines.GetValueOrDefault(engine) + utilization;
        }

        return engines.Count == 0 ? null : Math.Clamp(engines.Values.Max(), 0, 100);
    }
}
