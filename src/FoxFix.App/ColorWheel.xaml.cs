using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FoxFix.App;

/// <summary>Colour wheel: the angle is the hue, the distance from the middle is the saturation. The brightness comes from a slider outside.</summary>
public partial class ColorWheel : UserControl
{
    // Drawn large once, so the edge stays sharp on any screen.
    private const int PixelSize = 320;

    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
        nameof(Hue), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorWheel)d).UpdateThumb()));

    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(
        nameof(Saturation), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorWheel)d).UpdateThumb()));

    /// <summary>0 to 100: only colours the pointer's fill.</summary>
    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
        nameof(Brightness), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(100.0, (d, _) => ((ColorWheel)d).UpdateThumb()));

    private static WriteableBitmap? _shared;
    private bool _dragging;

    public ColorWheel()
    {
        InitializeComponent();
        WheelImage.Source = SharedWheel;
        SizeChanged += (_, _) => UpdateThumb();
        UpdateThumb();
    }

    /// <summary>The wheel's picture; the "custom colour" circle in the settings uses the same one.</summary>
    public static ImageSource SharedWheel => _shared ??= BuildWheel();

    public double Hue
    {
        get => (double)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => (double)GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    private static WriteableBitmap BuildWheel()
    {
        var bitmap = new WriteableBitmap(PixelSize, PixelSize, 96, 96, PixelFormats.Pbgra32, null);
        var pixels = new byte[PixelSize * PixelSize * 4];
        var centre = PixelSize / 2.0;

        for (var y = 0; y < PixelSize; y++)
        {
            for (var x = 0; x < PixelSize; x++)
            {
                double dx = (x + 0.5 - centre) / centre, dy = (y + 0.5 - centre) / centre;
                var distance = Math.Sqrt(dx * dx + dy * dy);

                // A soft edge, one pixel wide.
                var coverage = Math.Clamp((1 - distance) * centre + 0.5, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var rgb = ColorMath.FromHsv(hue, Math.Min(1, distance), 1);
                var i = (y * PixelSize + x) * 4;
                pixels[i] = (byte)(rgb.B * coverage);
                pixels[i + 1] = (byte)(rgb.G * coverage);
                pixels[i + 2] = (byte)(rgb.R * coverage);
                pixels[i + 3] = (byte)(255 * coverage);
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, PixelSize, PixelSize), pixels, PixelSize * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateThumb()
    {
        if (Thumb is null)
        {
            return;
        }

        double width = ActualWidth > 0 ? ActualWidth : Width, height = ActualHeight > 0 ? ActualHeight : Height;
        var radius = Math.Min(width, height) / 2;
        var angle = Hue * Math.PI / 180;
        var x = width / 2 + Math.Cos(angle) * Saturation * radius;
        var y = height / 2 + Math.Sin(angle) * Saturation * radius;

        Canvas.SetLeft(Thumb, x - Thumb.Width / 2);
        Canvas.SetTop(Thumb, y - Thumb.Height / 2);
        Canvas.SetLeft(ThumbOuter, x - ThumbOuter.Width / 2);
        Canvas.SetTop(ThumbOuter, y - ThumbOuter.Height / 2);
        Thumb.Fill = new SolidColorBrush(ColorMath.FromHsv(Hue, Saturation, Brightness / 100));
    }

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        Surface.CaptureMouse();
        Pick(e.GetPosition(Surface));
    }

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            Pick(e.GetPosition(Surface));
        }
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Surface.ReleaseMouseCapture();
        Pick(e.GetPosition(Surface));
    }

    /// <summary>Outside the wheel the saturation stops at 1: dragging past the edge keeps the pointer on it.</summary>
    private void Pick(Point point)
    {
        double width = ActualWidth, height = ActualHeight;
        double dx = point.X - width / 2, dy = point.Y - height / 2;
        var radius = Math.Min(width, height) / 2;
        var saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / radius);

        if (saturation > 0.001)
        {
            SetCurrentValue(HueProperty, (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360);
        }

        SetCurrentValue(SaturationProperty, saturation);
    }
}
