namespace FoxFix.App;

/// <summary>Turns "how far" and "how long so far" into the line under a progress bar: "%42 · yaklaşık 20 saniye kaldı".</summary>
public static class ProgressEstimate
{
    /// <summary>An estimate from the first seconds would jump around, so it waits for some data.</summary>
    private static readonly TimeSpan MinimumElapsed = TimeSpan.FromSeconds(2);

    private const double MinimumFraction = 0.03;

    public static string Format(double fraction, TimeSpan elapsed)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var text = $"%{(int)Math.Floor(fraction * 100)}";

        if (fraction >= MinimumFraction && fraction < 1 && elapsed >= MinimumElapsed)
        {
            var remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - fraction) / fraction);
            text += $" · yaklaşık {Duration(remaining)} kaldı";
        }

        return text;
    }

    /// <summary>Rounded to what a person can use: "birkaç saniye", "20 saniye", "2 dakika 30 saniye".</summary>
    public static string Duration(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(span.TotalSeconds / 5) * 5;

        if (seconds <= 5)
        {
            return "birkaç saniye";
        }

        if (seconds < 60)
        {
            return $"{seconds} saniye";
        }

        var minutes = seconds / 60;
        var rest = seconds % 60;
        return rest == 0 ? $"{minutes} dakika" : $"{minutes} dakika {rest} saniye";
    }
}

/// <summary>Reports on the thread that created it (the UI thread), or right away when there is none (tests).</summary>
public sealed class UiProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public UiProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value)
    {
        if (_context is null)
        {
            _handler(value);
        }
        else
        {
            _context.Post(_ => _handler(value), null);
        }
    }
}
