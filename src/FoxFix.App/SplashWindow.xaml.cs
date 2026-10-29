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