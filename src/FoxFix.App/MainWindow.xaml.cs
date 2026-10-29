using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FoxFix.App;

public partial class MainWindow : Window
{
    private readonly ThemeManager? _theme;

    public MainWindow()
    {
        InitializeComponent();

        // Starts see-through whatever the Windows animation setting says, so an unfinished window can never show as a black
        // rectangle. The app shows it only when it is ready and then reveals it (see FadeInAsync).
        Opacity = 0;

        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is MainViewModel vm)
            {
                WireNavigation(vm);
            }
        };

        // Esc goes back to the home screen, unless the user is typing somewhere.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not MainViewModel vm)
            {
                return;
            }

            // The top-most pop-up goes first; with none open Esc goes back to the overview (unless the user is typing).
            if (vm.Notice.IsOpen)
            {
                vm.Notice.CloseCommand.Execute(null);
            }
            else if (vm.Info.IsOpen)
            {
                vm.Info.CloseCommand.Execute(null);
            }
            else if (vm.RepairPicker.IsOpen)
            {
                vm.RepairPicker.CloseCommand.Execute(null);
            }
            else if (vm.Fix.IsPanelVisible)
            {
                vm.Fix.CancelCommand.Execute(null);
            }
            else if (vm.ShowBack && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
            {
                vm.GoHomeCommand.Execute(null);
            }
            else
            {
                return;
            }

            e.Handled = true;
        };
    }

    private bool _sidebarFolded;

    private void OnSidebarToggle(object sender, RoutedEventArgs e)
    {
        _sidebarFolded = !_sidebarFolded;
        SidebarToggle.ToolTip = _sidebarFolded ? "Menüyü sabitle" : "Menüyü daralt";
        ApplySidebar();
    }

    /// <summary>
    /// The mouse leaves one element a moment before it enters the next, so the decision waits until both events are through;
    /// otherwise the menu would blink when the pointer travels from the three lines onto the menu.
    /// </summary>
    private void OnSidebarMouseChanged(object sender, MouseEventArgs e) =>
        Dispatcher.BeginInvoke(ApplySidebar, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>
    /// Folded: the menu is gone and only the three lines stay; the page takes the room. Pointing at the three lines slides the
    /// menu in over the page (the page does not move) and leaving slides it out. Pressing the lines again unfolds it for good.
    /// </summary>
    private void ApplySidebar()
    {
        var shown = !_sidebarFolded || Sidebar.IsMouseOver || SidebarToggle.IsMouseOver;

        SidebarShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Slide(shown ? 0 : -SidebarHost.Width));
        Scroller.BeginAnimation(
            FrameworkElement.MarginProperty,
            new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(_sidebarFolded ? 56 : SidebarHost.Width, 0, 0, 0), Duration())
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            });
        SidebarHost.IsHitTestVisible = shown;
    }

    private static TimeSpan Duration() => SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(170) : TimeSpan.Zero;

    private static System.Windows.Media.Animation.DoubleAnimation Slide(double to) =>
        new(to, Duration())
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
    public MainWindow(ThemeManager theme)
        : this()
    {
        _theme = theme;

        WindowTheme.Follow(this, theme);
    }

    /// <summary>Fades the window in; the splash fades out at the same time so the two meet in the middle.</summary>
    public Task FadeInAsync()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            done.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }

    /// <summary>A pop-up fades in while its card grows a little: it shows where the change came from.</summary>
    private void OnDialogVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not System.Windows.Controls.Grid overlay || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        overlay.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });

        if (overlay.Children.Count > 1 && overlay.Children[1] is FrameworkElement { RenderTransform: System.Windows.Media.ScaleTransform scale })
        {
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        }
    }

    /// <summary>Each page slides up a few pixels while it fades in.</summary>
    private void AnimatePageIn()
    {
        if (!SystemParameters.ClientAreaAnimation || PageHost.RenderTransform is not System.Windows.Media.TranslateTransform move)
        {
            return;
        }

        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        PageHost.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        move.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void OnInfoBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Info.CloseCommand.Execute(null);
        }
    }

    private void OnNoticeBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Notice.CloseCommand.Execute(null);
        }
    }

    private void OnPickerBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.RepairPicker.CloseCommand.Execute(null);
        }
    }

    /// <summary>A new page, or a change preview that just opened, always starts at the top so it cannot be missed.</summary>
    private void WireNavigation(MainViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Page))
            {
                Scroller.ScrollToTop();
                AnimatePageIn();
            }
        };

        vm.Fix.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FixFlowViewModel.IsPanelVisible) && vm.Fix.IsPanelVisible)
            {
                Scroller.ScrollToTop();
            }
        };
    }
}
