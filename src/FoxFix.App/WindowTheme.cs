using System.Windows;
using System.Windows.Interop;

namespace FoxFix.App;

/// <summary>The title bar is drawn by Windows, so every window has to be told about the theme separately.</summary>
public static class WindowTheme
{
    /// <summary>Gives the window a dark or light title bar now, and again whenever the theme changes.</summary>
    public static void Follow(Window window, ThemeManager theme)
    {
        void Update(bool light)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                TitleBar.SetDark(handle, dark: !light);
            }
        }

        window.SourceInitialized += (_, _) => Update(theme.IsLight);
        theme.Changed += Update;
        window.Closed += (_, _) => theme.Changed -= Update;
    }
}