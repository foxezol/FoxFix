using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.Settings;

namespace FoxFix.App;

/// <param name="TargetName">The x:Name of the element the step points at in the main window; null for a step in the middle of the window.</param>
public sealed record TourStep(string TitleKey, string TextKey, string? TargetName);

/// <summary>
/// The first-use tour. It works like Driver.js (a dimmed window with one element lit and a small card beside it, with Back, Next and
/// Skip) but is built natively for WPF: Driver.js lives on web pages and cannot point at WPF controls. The view reads
/// <see cref="Current"/> and <see cref="IsOpen"/> and does the drawing; nothing here touches the system.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    /// <summary>The order of the tour. Every target exists in the main window (checked by a test).</summary>
    public static IReadOnlyList<TourStep> AllSteps { get; } =
    [
        new(Strings.TourWelcomeTitle, Strings.TourWelcomeText, null),
        new(Strings.TourFixTitle, Strings.TourFixText, "TourFixButton"),
        new(Strings.TourConnectionTitle, Strings.TourConnectionText, "NavConnection"),
        new(Strings.TourTweaksTitle, Strings.TourTweaksText, "NavTweaks"),
        new(Strings.TourDpiTitle, Strings.TourDpiText, "NavDnsDpi"),
        new(Strings.TourAccessTitle, Strings.TourAccessText, "NavAccess"),
        new(Strings.TourDevicesTitle, Strings.TourDevicesText, "NavDevices"),
        new(Strings.TourReportTitle, Strings.TourReportText, "NavReport"),
        new(Strings.TourSettingsTitle, Strings.TourSettingsText, "NavSettings"),
        new(Strings.TourDoneTitle, Strings.TourDoneText, null),
    ];

    private readonly Action<AppPage>? _goTo;
    private readonly Func<bool> _devicesEnabled;

    public TourViewModel(Action<AppPage>? goTo = null, Func<bool>? devicesEnabled = null)
    {
        _goTo = goTo;
        _devicesEnabled = devicesEnabled ?? (() => true);
        Steps = AllSteps;
    }

    /// <summary>The steps of this run: the same as <see cref="AllSteps"/> without the "Ses ve kamera" step while that page is switched off.</summary>
    public IReadOnlyList<TourStep> Steps { get; private set; }

    /// <summary>Raised once when the tour ends: Completed after the last step, Skipped for anything else.</summary>
    public event Action<TourState>? Ended;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(Title), nameof(Text), nameof(TargetName), nameof(StepText), nameof(CanGoBack), nameof(IsLast), nameof(HasTarget))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _index;

    public TourStep Current => Steps[Index];

    public string Title => Strings.Get(Current.TitleKey);

    public string Text => Strings.Get(Current.TextKey);

    public string? TargetName => Current.TargetName;

    public bool HasTarget => Current.TargetName is not null;

    public string StepText => Strings.Format(Strings.TourStepOf, Index + 1, Steps.Count);

    public bool CanGoBack => Index > 0;

    public bool IsLast => Index == Steps.Count - 1;

    /// <summary>Starts (or restarts) from the first step. The overview is shown, since the first pointers are on it.</summary>
    [RelayCommand]
    public void Start()
    {
        Steps = AllSteps.Where(step => step.TargetName != "NavDevices" || _devicesEnabled()).ToList();
        Index = 0;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(IsLast));
        _goTo?.Invoke(AppPage.Home);
        IsOpen = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (!IsOpen)
        {
            return;
        }

        if (IsLast)
        {
            End(TourState.Completed);
            return;
        }

        Index++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (IsOpen && Index > 0)
        {
            Index--;
        }
    }

    /// <summary>"Atla" and Esc: the tour is closed and counted as skipped.</summary>
    [RelayCommand]
    public void Skip()
    {
        if (IsOpen)
        {
            End(TourState.Skipped);
        }
    }

    private void End(TourState state)
    {
        IsOpen = false;
        Ended?.Invoke(state);
    }
}

/// <summary>
/// What happens around the first start: the question "tour?" after the language was chosen, the tour itself, and the short FoxDPI
/// explanation that replaces the old pop-up (it shows once, on the DNS ve DPI page, only for a person who skipped the tour).
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;

    public OnboardingViewModel(SettingsViewModel settings, Action<AppPage>? goTo = null)
    {
        _settings = settings;
        Tour = new TourViewModel(goTo, () => settings.ExperimentalDevices);
        Tour.Ended += OnTourEnded;
    }

    public TourViewModel Tour { get; }

    [ObservableProperty]
    private bool _isTourPromptOpen;

    [ObservableProperty]
    private bool _isDpiHintOpen;

    /// <summary>Asks once, at the first start. Nothing is asked again after an answer, whichever it was.</summary>
    public void OfferTourIfFirstRun()
    {
        if (_settings.Tour == TourState.NotOffered)
        {
            IsTourPromptOpen = true;
        }
    }

    [RelayCommand]
    private void StartTourFromPrompt()
    {
        IsTourPromptOpen = false;
        Tour.Start();
    }

    /// <summary>"Şimdi değil" and Esc on the question: counts as skipped, so the DPI explanation can show later.</summary>
    [RelayCommand]
    private void DeclineTour()
    {
        IsTourPromptOpen = false;
        _settings.SetTour(TourState.Skipped);
    }

    /// <summary>The Ayarlar button: always possible, whatever happened before.</summary>
    [RelayCommand]
    private void StartTourFromSettings()
    {
        IsTourPromptOpen = false;
        Tour.Start();
    }

    [RelayCommand]
    private void CloseDpiHint()
    {
        IsDpiHintOpen = false;
        _settings.MarkDpiHintSeen();
    }

    /// <summary>The first time the DNS ve DPI page opens for somebody who skipped the tour, the explanation shows.</summary>
    public void OnPageEntered(AppPage page)
    {
        if (page == AppPage.DnsDpi && _settings.Tour == TourState.Skipped && !_settings.DpiHintSeen && !Tour.IsOpen)
        {
            IsDpiHintOpen = true;
        }
    }

    private void OnTourEnded(TourState state)
    {
        _settings.SetTour(state);

        // The tour already told how FoxDPI is used, so the page does not say it again.
        if (state == TourState.Completed)
        {
            _settings.MarkDpiHintSeen();
        }
    }
}
