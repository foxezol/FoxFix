using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FoxFix.App;

/// <summary>The numbers that go onto the picture a person can share.</summary>
public sealed record SpeedCard(string Download, string Upload, string Latency, string Jitter, DateTime At);

/// <summary>Puts the speed card on the clipboard as a picture.</summary>
public interface ISpeedCardClipboard
{
    void Copy(SpeedCard card);
}

/// <summary>Draws the speed card (dark, with the logo and the project's address at the bottom) and copies it as a PNG-style bitmap.</summary>
public sealed class WpfSpeedCardClipboard : ISpeedCardClipboard
{
    public const string RepoText = "github.com/foxezol/FoxFix";

    private const int Width = 720;
    private const int Scale = 2;
    private const int Attempts = 5;

    private static readonly Color Background = Color.FromRgb(0x0F, 0x11, 0x17);
    private static readonly Color Panel = Color.FromRgb(0x17, 0x1A, 0x23);
    private static readonly Color Accent = Color.FromRgb(0x9E, 0x20, 0xE6);
    private static readonly Color AccentText = Color.FromRgb(0xC5, 0x8A, 0xF9);
    private static readonly Color Muted = Color.FromRgb(0x9A, 0xA1, 0xB2);

    public void Copy(SpeedCard card)
    {
        var image = Render(card);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return;
            }
            catch (COMException) when (attempt < Attempts)
            {
                // Another program holds the clipboard for a moment.
                Thread.Sleep(80);
            }
        }
    }

    public static BitmapSource Render(SpeedCard card)
    {
        var root = new Border { Width = Width, Background = Brush(Background), Padding = new Thickness(40, 36, 40, 28) };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.ClearType);

        var stack = new StackPanel();
        root.Child = stack;

        // Header: logo, name, date
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = new Image { Source = LoadLogo(), Height = 52, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        header.Children.Add(logo);

        var names = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text("FoxFix", 26, FontWeights.SemiBold, Colors.White, display: true));
        names.Children.Add(Text("Hız testi sonucu", 14, FontWeights.Normal, Muted));
        Grid.SetColumn(names, 1);
        header.Children.Add(names);

        var date = Text(card.At.ToString("d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")), 13, FontWeights.Normal, Muted);
        date.HorizontalAlignment = HorizontalAlignment.Right;
        date.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(date, 1);
        header.Children.Add(date);
        stack.Children.Add(header);

        // The four numbers, two by two
        var grid = new Grid { Margin = new Thickness(0, 28, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        AddStat(grid, 0, 0, "İndirme", card.Download);
        AddStat(grid, 0, 1, "Yükleme", card.Upload);
        AddStat(grid, 1, 0, "Ping", card.Latency);
        AddStat(grid, 1, 1, "Titreşim", card.Jitter);
        stack.Children.Add(grid);

        // The advertisement: where the program lives
        var footer = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        footer.Children.Add(new Border { Height = 1, Background = Brush(Color.FromRgb(0x2A, 0x2F, 0x3D)) });
        var ad = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        ad.Children.Add(Text(RepoText, 14, FontWeights.SemiBold, AccentText));
        footer.Children.Add(ad);
        stack.Children.Add(footer);

        root.Measure(new Size(Width, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * Scale), (int)Math.Ceiling(root.ActualHeight * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(root);

        // Opaque, so no program pastes it with a black or transparent background.
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    /// <summary>Named by assembly, so it also resolves when another program hosts FoxFix's code (the tests). A missing logo only leaves a gap.</summary>
    private static BitmapImage? LoadLogo()
    {
        try
        {
            return new BitmapImage(new Uri("pack://application:,,,/FoxFix;component/Assets/FoxLogo.png"));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private static void AddStat(Grid grid, int row, int column, string label, string value)
    {
        var tile = new Border
        {
            Background = Brush(Panel),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(column == 0 ? 0 : 8, row == 0 ? 0 : 16, column == 0 ? 8 : 0, 0),
        };
        var content = new StackPanel();
        content.Children.Add(Text(value, 32, FontWeights.SemiBold, Colors.White, display: true));
        content.Children.Add(Text(label, 14, FontWeights.Normal, Muted));
        tile.Child = content;
        Grid.SetRow(tile, row);
        Grid.SetColumn(tile, column);
        grid.Children.Add(tile);
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Color color, bool display = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brush(color),
        FontFamily = new FontFamily(display ? "Segoe UI Variable Display, Segoe UI" : "Segoe UI Variable Text, Segoe UI"),
    };

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}