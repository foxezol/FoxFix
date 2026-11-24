using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

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

        StateChanged += (_, _) => UpdateCaption();

        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is MainViewModel vm)
            {
                WireNavigation(vm);
                WireTour(vm);
                WireAccentPicker(vm);
                vm.RepoLinkCopied += ShowShareToast;
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
            if (vm.Onboarding?.Tour.IsOpen == true)
            {
                vm.Onboarding.Tour.Skip();
            }
            else if (vm.Settings?.IsRestartPromptOpen == true)
            {
                vm.Settings.RestartLaterCommand.Execute(null);
            }
            else if (vm.Onboarding?.IsTourPromptOpen == true)
            {
                vm.Onboarding.DeclineTourCommand.Execute(null);
            }
            else if (vm.Onboarding?.IsDpiHintOpen == true)
            {
                vm.Onboarding.CloseDpiHintCommand.Execute(null);
            }
            else if (vm.Notice.IsOpen)
            {
                vm.Notice.CloseCommand.Execute(null);
            }
            else if (vm.Updates?.IsPromptOpen == true)
            {
                vm.Updates.ClosePromptCommand.Execute(null);
            }
            else if (vm.Tweaks?.IsBackupPromptOpen == true)
            {
                vm.Tweaks.CloseBackupPromptCommand.Execute(null);
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

    // ---------------------------------------------------------------- game overlay shortcut box

    /// <summary>The box takes the keys that are pressed in it as the new shortcut (Ctrl+Shift+F10, for example).</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // A modifier on its own is only the start of a shortcut.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.Tab or Key.Escape)
        {
            return;
        }

        if (DataContext is not MainViewModel { Overlay: { } overlay })
        {
            return;
        }

        var name = WpfOverlayHost.ToKeyName(key);
        var modifiers = FoxFix.Core.Overlay.HotkeyModifiers.None;
        modifiers |= Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? FoxFix.Core.Overlay.HotkeyModifiers.Ctrl : 0;
        modifiers |= Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? FoxFix.Core.Overlay.HotkeyModifiers.Alt : 0;
        modifiers |= Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FoxFix.Core.Overlay.HotkeyModifiers.Shift : 0;
        modifiers |= Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? FoxFix.Core.Overlay.HotkeyModifiers.Win : 0;

        overlay.TrySetHotkey(name is null ? "" : FoxFix.Core.Overlay.OverlayHotkey.Format(modifiers, name));
    }

    // ---------------------------------------------------------------- title bar (drawn by the app, not by Windows)

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    /// <summary>The middle button shows "maximise" or "restore"; a maximised window without a Windows frame would spill past the screen, so it gets the frame's width as room.</summary>
    private void UpdateCaption()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        var name = FoxFix.Core.Strings.Get(maximized ? FoxFix.Core.Strings.UiRestoreWindow : FoxFix.Core.Strings.UiMaximize);
        MaximizeButton.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, name);
        WindowFrame.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    private bool _sidebarFolded;

    private void OnSidebarToggle(object sender, RoutedEventArgs e)
    {
        _sidebarFolded = !_sidebarFolded;
        SidebarToggle.ToolTip = _sidebarFolded ? FoxFix.Core.Strings.Get(FoxFix.Core.Strings.MenuPin) : FoxFix.Core.Strings.Get(FoxFix.Core.Strings.MenuCollapse);
        ApplySidebar();
    }

    /// <summary>
    /// Folded: the menu is gone and only the three lines stay; the page takes the room. Pressing the three lines again brings
    /// it back. It opens and closes only by that press, never because the pointer passes by.
    /// </summary>
    private void ApplySidebar()
    {
        var width = SidebarHost.Width;

        SidebarShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Slide(_sidebarFolded ? -width : 0));
        Scroller.BeginAnimation(
            FrameworkElement.MarginProperty,
            new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(_sidebarFolded ? 56 : width, 38, 0, 0), Duration())
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            });
        SidebarHost.IsHitTestVisible = !_sidebarFolded;
    }

    /// <summary>A small "Kopyalandı" shows above the share button, floats upward and fades out.</summary>
    private void ShowShareToast()
    {
        var up = new System.Windows.Media.Animation.DoubleAnimation(0, -28, TimeSpan.FromMilliseconds(1300))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        ShareToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, up);

        var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(120)));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(750)));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1300)));
        ShareToast.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- colour wheel panel

    private int _pickerVersion;

    /// <summary>The wheel's panel opens with a short fade and a small slide, and fades out when it closes.</summary>
    private void WireAccentPicker(MainViewModel vm)
    {
        if (vm.Settings?.Picker is not { } picker)
        {
            return;
        }

        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccentPickerViewModel.IsOpen))
            {
                if (picker.IsOpen)
                {
                    ShowAccentPicker();
                }
                else
                {
                    HideAccentPicker();
                }
            }
        };
    }

    private void ShowAccentPicker()
    {
        _pickerVersion++;
        AccentPickerPanel.BeginAnimation(OpacityProperty, null);
        AccentPickerPanel.Opacity = 1;
        AccentPickerPanel.Visibility = Visibility.Visible;

        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var move = new TranslateTransform(0, -8);
        AccentPickerPanel.RenderTransform = move;
        AccentPickerPanel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        var slide = new System.Windows.Media.Animation.DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        slide.Completed += (_, _) => AccentPickerPanel.RenderTransform = Transform.Identity;
        move.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void HideAccentPicker()
    {
        var version = ++_pickerVersion;
        if (!SystemParameters.ClientAreaAnimation)
        {
            AccentPickerPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            // Opened again while it was fading: it stays.
            if (version != _pickerVersion)
            {
                return;
            }

            AccentPickerPanel.Visibility = Visibility.Collapsed;
            AccentPickerPanel.BeginAnimation(OpacityProperty, null);
            AccentPickerPanel.Opacity = 1;
        };
        AccentPickerPanel.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- tour

    private const double HolePadX = 6;
    private const double HolePadY = 4;

    private readonly RectangleGeometry _tourLayerShape = new();
    private readonly RectangleGeometry _tourHole = new() { RadiusX = 10, RadiusY = 10 };
    private int _tourVersion;
    private bool _tourPlaced;

    private static System.Windows.Media.Animation.IEasingFunction Ease { get; } =
        new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

    private static bool Animated => SystemParameters.ClientAreaAnimation;

    /// <summary>The tour opens with a soft fade and closes with a small pop; the card and the lit element glide from step to step.</summary>
    private void WireTour(MainViewModel vm)
    {
        if (vm.Onboarding is not { } onboarding)
        {
            return;
        }

        TourShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, _tourLayerShape, _tourHole);

        var shownIndex = -1;
        onboarding.Tour.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TourViewModel.IsOpen))
            {
                if (onboarding.Tour.IsOpen)
                {
                    shownIndex = onboarding.Tour.Index;
                    OpenTour();
                }
                else
                {
                    CloseTour();
                }
            }
            else if (e.PropertyName == nameof(TourViewModel.Index) && onboarding.Tour.IsOpen && onboarding.Tour.Index != shownIndex)
            {
                shownIndex = onboarding.Tour.Index;

                // After the new texts and the page are in place.
                Dispatcher.BeginInvoke(() => PositionTour(animate: _tourPlaced), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
    }

    private void OpenTour()
    {
        var version = ++_tourVersion;

        // The pointers are on the menu: a folded menu is opened for the tour.
        if (_sidebarFolded)
        {
            OnSidebarToggle(this, new RoutedEventArgs());
        }

        _tourPlaced = false;
        ResetTourAnimations();
        TourLayer.Opacity = Animated ? 0 : 1;
        TourLayer.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(
            () =>
            {
                if (version != _tourVersion)
                {
                    return;
                }

                PositionTour(animate: false);
                _tourPlaced = true;
                TourNextButton.Focus();

                if (!Animated)
                {
                    return;
                }

                TourLayer.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
                if (TourCard.RenderTransform is ScaleTransform scale)
                {
                    var grow = new System.Windows.Media.Animation.DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.4 },
                    };
                    grow.Completed += (_, _) => ClearScale();
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                }
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The way out: the card swells a little, then shrinks away while everything fades. The overview is underneath.</summary>
    private void CloseTour()
    {
        var version = ++_tourVersion;
        if (!Animated || TourLayer.Visibility != Visibility.Visible)
        {
            TourLayer.Visibility = Visibility.Collapsed;
            ResetTourAnimations();
            return;
        }

        TourRing.BeginAnimation(OpacityProperty, null);
        TourRing.Opacity = 0;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            if (version != _tourVersion)
            {
                return;
            }

            TourLayer.Visibility = Visibility.Collapsed;
            ResetTourAnimations();
        };
        TourLayer.BeginAnimation(OpacityProperty, fade);

        if (TourCard.RenderTransform is ScaleTransform scale)
        {
            var pop = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.05, TimeSpan.FromMilliseconds(110), Ease));
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0.78, TimeSpan.FromMilliseconds(300), Ease));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }

    private void ResetTourAnimations()
    {
        TourLayer.BeginAnimation(OpacityProperty, null);
        TourLayer.Opacity = 1;
        TourRing.BeginAnimation(OpacityProperty, null);
        TourRing.Opacity = 0;
        TourCard.BeginAnimation(FrameworkElement.MarginProperty, null);
        ClearScale();

        if (TourBody.RenderTransform is TranslateTransform move)
        {
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.Y = 0;
        }

        TourBody.BeginAnimation(OpacityProperty, null);
        TourBody.Opacity = 1;
    }

    private void ClearScale()
    {
        if (TourCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
    }

    /// <summary>A slow, gentle breathing of the ring around the lit element, so the eye finds it.</summary>
    private void StartRingPulse()
    {
        if (!Animated)
        {
            return;
        }

        var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(1100))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
        };
        TourRing.BeginAnimation(OpacityProperty, pulse);
    }

    private void OnTourLayerSizeChanged(object sender, SizeChangedEventArgs e) => PositionTour(animate: false);

    /// <summary>Dims everything except the element the step points at, rings it, and puts the card beside it.</summary>
    private void PositionTour(bool animate)
    {
        if (DataContext is not MainViewModel { Onboarding.Tour: { IsOpen: true } tour } || TourLayer.ActualWidth <= 0)
        {
            return;
        }

        animate &= Animated;
        var layer = new Rect(0, 0, TourLayer.ActualWidth, TourLayer.ActualHeight);
        Rect? hole = null;

        if (tour.TargetName is { } name && FindName(name) is FrameworkElement target)
        {
            target.BringIntoView();
            target.UpdateLayout();

            if (target.IsVisible && target.ActualWidth > 0)
            {
                var origin = target.TransformToVisual(TourLayer).Transform(new Point(0, 0));
                hole = new Rect(Math.Round(origin.X - HolePadX), Math.Round(origin.Y - HolePadY), Math.Round(target.ActualWidth + 2 * HolePadX), Math.Round(target.ActualHeight + 2 * HolePadY));
            }
        }

        _tourLayerShape.Rect = layer;

        // Without a target the lit area closes to a point in the middle: the whole window is dimmed.
        var lit = hole ?? new Rect(layer.Width / 2, layer.Height / 2, 0, 0);
        var holeFrom = _tourHole.Rect;
        var ringFrom = new Thickness(holeFrom.X, holeFrom.Y, 0, 0);

        // A pointer that is still gliding starts from where it is drawn now; a new place is set after the old animation is dropped.
        _tourHole.BeginAnimation(RectangleGeometry.RectProperty, null);
        TourRing.BeginAnimation(FrameworkElement.MarginProperty, null);
        TourRing.BeginAnimation(FrameworkElement.WidthProperty, null);
        TourRing.BeginAnimation(FrameworkElement.HeightProperty, null);

        var duration = TimeSpan.FromMilliseconds(320);
        _tourHole.Rect = lit;
        TourRing.Margin = new Thickness(lit.X, lit.Y, 0, 0);
        TourRing.Width = lit.Width;
        TourRing.Height = lit.Height;

        if (animate && !holeFrom.IsEmpty)
        {
            _tourHole.BeginAnimation(RectangleGeometry.RectProperty, new System.Windows.Media.Animation.RectAnimation(holeFrom, lit, duration) { EasingFunction = Ease });
            if (hole is not null)
            {
                TourRing.BeginAnimation(FrameworkElement.MarginProperty, new System.Windows.Media.Animation.ThicknessAnimation(ringFrom, new Thickness(lit.X, lit.Y, 0, 0), duration) { EasingFunction = Ease });
                TourRing.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(holeFrom.Width, lit.Width, duration) { EasingFunction = Ease });
                TourRing.BeginAnimation(FrameworkElement.HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(holeFrom.Height, lit.Height, duration) { EasingFunction = Ease });
            }
        }

        SetRingVisible(hole is not null, animate);

        // The margin is how the card is placed, so it is cleared before measuring (a measured margin would count as card size).
        var cardFrom = TourCard.Margin;
        TourCard.BeginAnimation(FrameworkElement.MarginProperty, null);
        TourCard.Margin = new Thickness(0);
        TourCard.Measure(new Size(layer.Width, layer.Height));
        var card = TourCard.DesiredSize;
        double left, top;

        if (hole is { } box)
        {
            // Beside the lit element when there is room, otherwise below or above it.
            left = box.Right + 18;
            top = box.Top;
            if (left + card.Width > layer.Width - 12)
            {
                left = Math.Max(12, Math.Min(box.Left, layer.Width - card.Width - 12));
                top = box.Bottom + 14;
                if (top + card.Height > layer.Height - 12)
                {
                    top = Math.Max(12, box.Top - card.Height - 14);
                }
            }
        }
        else
        {
            left = (layer.Width - card.Width) / 2;
            top = (layer.Height - card.Height) / 2;
        }

        left = Math.Clamp(left, 12, Math.Max(12, layer.Width - card.Width - 12));
        top = Math.Clamp(top, 12, Math.Max(12, layer.Height - card.Height - 12));

        // Whole pixels at rest keep the text sharp; the glide is only while it moves.
        var cardTo = new Thickness(Math.Round(left), Math.Round(top), 0, 0);
        TourCard.Margin = cardTo;

        if (animate)
        {
            TourCard.BeginAnimation(FrameworkElement.MarginProperty, new System.Windows.Media.Animation.ThicknessAnimation(cardFrom, cardTo, duration) { EasingFunction = Ease });
            AnimateTourText();
        }
    }

    private void SetRingVisible(bool visible, bool animate)
    {
        TourRing.BeginAnimation(OpacityProperty, null);
        if (!visible)
        {
            TourRing.Opacity = 0;
            return;
        }

        if (animate && TourRing.Opacity < 1)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(TourRing.Opacity, 1, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => StartRingPulse();
            TourRing.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            TourRing.Opacity = 1;
            StartRingPulse();
        }
    }

    /// <summary>The words of a new step rise a few pixels while they fade in.</summary>
    private void AnimateTourText()
    {
        if (TourBody.RenderTransform is not TranslateTransform move)
        {
            return;
        }

        TourBody.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = Ease });
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

        // The window and task bar icon is the boxed fox in the accent colour, for as long as the program runs.
        void UpdateIcon(bool _)
        {
            if (Application.Current.Resources[ThemeManager.IconKey] is System.Windows.Media.ImageSource logo)
            {
                Icon = logo;
            }
        }

        UpdateIcon(theme.IsLight);
        theme.Changed += UpdateIcon;
        Closed += (_, _) => theme.Changed -= UpdateIcon;
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
