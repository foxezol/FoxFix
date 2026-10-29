using System.Windows;
using System.Windows.Media.Animation;

namespace FoxFix.App;

/// <summary>The first thing on screen. It stays until the main window is ready and then fades out as that fades in.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>The same screen for something else that takes a moment (FoxDPI starting): its own logo, name and a line under it.</summary>
    public SplashWindow(string title, string logoPath, string subtitle)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        Logo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/" + logoPath));
        SubtitleText.Text = subtitle;
        SubtitleText.Visibility = Visibility.Visible;
    }

    /// <summary>Fills the bar up to this fraction (0 to 1). It only moves forward.</summary>
    public void Report(double fraction, TimeSpan? duration = null)
    {
        var target = Math.Clamp(Math.Max(fraction, Bar.Value), 0, 1);

        if (!SystemParameters.ClientAreaAnimation)
        {
            Bar.Value = target;
            return;
        }

        Bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(target, duration ?? TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public Task FadeOutAsync()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => done.TrySetResult();
        BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }
}