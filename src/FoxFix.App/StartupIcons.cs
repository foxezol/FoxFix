using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FoxFix.Core.Dpi;
using FoxFix.Core.Startup;

namespace FoxFix.App;

/// <summary>Reads the icon Windows itself shows for a startup program's file. Returns null when there is none.</summary>
public static class StartupIcons
{
    public static ImageSource? Load(StartupEntry entry)
    {
        var path = ServicePathParser.GetPath(Environment.ExpandEnvironmentVariables(entry.Command));
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null)
        {
            return null;
        }

        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }
}
