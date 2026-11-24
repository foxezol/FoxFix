using System.IO;
using System.Text;
using FoxFix.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core.Updates;

namespace FoxFix.App;

/// <summary>A failed update check leaves a line here. The start-up check says nothing on screen, so this is where it can be read.</summary>
public static class UpdateLog
{
    public static string FilePath => Path.Combine(CrashLog.Folder, "guncelleme.txt");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(CrashLog.Folder);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done if even the log cannot be written.
        }
    }
}

/// <summary>
/// "Is there a newer FoxFix?" Two ways in: the quiet check when FoxFix opens (it shows something only when there is an
/// update) and the button on the Ayarlar page (it always answers). FoxFix never downloads or installs anything itself:
/// the pop-up opens the release page, and the person decides.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    public const string ReleasesUrl = MainViewModel.RepoUrl + "/releases/latest";

    private const int NotesLimit = 700;

    private readonly UpdateChecker _checker;
    private readonly Version _current;
    private readonly Action<string> _openLink;
    private readonly Action<string>? _log;
    private readonly SelfUpdater? _updater;
    private readonly Func<Task>? _stopFoxDpi;
    private readonly Func<string, bool>? _startFinisher;
    private readonly Action? _exitApp;

    /// <param name="updater">Null (as in tests) when the app cannot install updates itself: the pop-up then only opens the release page.</param>
    /// <param name="stopFoxDpi">Ends FoxDPI and unloads its driver, since FoxFix is about to close.</param>
    /// <param name="startFinisher">Starts the prepared command detached; true when it started.</param>
    /// <param name="exitApp">Closes FoxFix a moment later, so the last message can still be read.</param>
    public UpdateViewModel(
        UpdateChecker checker,
        Version current,
        Action<string> openLink,
        Action<string>? log = null,
        SelfUpdater? updater = null,
        Func<Task>? stopFoxDpi = null,
        Func<string, bool>? startFinisher = null,
        Action? exitApp = null)
    {
        _checker = checker;
        _current = ReleaseVersion.Normalize(current);
        _openLink = openLink;
        _log = log;
        _updater = updater;
        _stopFoxDpi = stopFoxDpi;
        _startFinisher = startFinisher;
        _exitApp = exitApp;
    }

    public string CurrentVersionText => Strings.Format(Strings.InstalledVersion, UpdateChecker.FormatVersion(_current));

    /// <summary>What the Ayarlar page says about the last check. Empty until a check was asked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = "";

    public bool HasStatus => StatusText.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isPromptOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(PromptTitle), nameof(PromptNotes), nameof(HasNotes), nameof(CanInstall))]
    // The button asks the command whether it may run only when told that this changed: without this line it stays greyed out
    // after the release arrives (the unit tests ask the command directly, which hid it).
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ReleaseInfo? _latest;

    public bool HasUpdate => Latest is not null;

    /// <summary>True when the app can fetch, check and install this release itself (see <see cref="SelfUpdater"/>).</summary>
    public bool CanInstall => Latest is not null && _updater?.CanUpdate(Latest) == true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(ClosePromptCommand), nameof(OpenReleaseCommand), nameof(CheckNowCommand))]
    private bool _isInstalling;

    /// <summary>0 to 100: how much of the package has arrived.</summary>
    [ObservableProperty]
    private double _installPercent;

    [ObservableProperty]
    private string _installText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallError))]
    private string _installError = "";

    public bool HasInstallError => InstallError.Length > 0;

    /// <summary>
    /// "Şimdi güncelle": downloads the package and checks it (FoxFix keeps running meanwhile), then stops FoxDPI, starts the
    /// detached command and closes. The command replaces the program once FoxFix has ended and opens the new one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartInstall))]
    private async Task InstallAsync()
    {
        if (Latest is not { } release || _updater is null)
        {
            return;
        }

        IsInstalling = true;
        InstallError = "";
        InstallPercent = 0;
        InstallText = Strings.Get(Strings.Downloading);

        try
        {
            var progress = new InlineProgress(fraction =>
            {
                InstallPercent = fraction * 100;
                InstallText = Strings.Format(Strings.DownloadingPercent, Math.Round(fraction * 100));
            });

            var prepared = await _updater.PrepareAsync(release, progress, CancellationToken.None);
            if (!prepared.Success)
            {
                _log?.Invoke(prepared.Message);
                InstallError = prepared.Message;
                return;
            }

            InstallPercent = 100;
            InstallText = Strings.Get(Strings.Preparing);
            if (_stopFoxDpi is not null)
            {
                await _stopFoxDpi();
            }

            if (_startFinisher?.Invoke(prepared.Command!) != true)
            {
                InstallError = Strings.Get(Strings.UpdateStartFailed);
                _log?.Invoke(InstallError);
                return;
            }

            InstallText = prepared.Message;
            _exitApp?.Invoke();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanStartInstall() => !IsInstalling && CanInstall;

    private bool NotInstalling() => !IsInstalling;

    private sealed class InlineProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }
    public string PromptTitle => Latest is null ? "" : Strings.Format(Strings.UpdatePromptTitle, UpdateChecker.FormatVersion(Latest.Version));

    public string PromptNotes => Latest is null ? "" : Shorten(Latest.Notes);

    public bool HasNotes => PromptNotes.Length > 0;

    /// <summary>The check from the button: always says what it found.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNowAsync() => RunAsync(silent: false);

    private bool CanCheck() => !IsChecking && !IsInstalling;

    /// <summary>The check when FoxFix opens: nothing on screen unless there is an update; a failure goes to the log.</summary>
    public Task RunStartupCheckAsync() => RunAsync(silent: true);

    private async Task RunAsync(bool silent)
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        if (!silent)
        {
            StatusText = Strings.Get(Strings.Checking);
        }

        try
        {
            var result = await _checker.CheckAsync(_current, CancellationToken.None);

            switch (result.Status)
            {
                case UpdateStatus.Available:
                    Offer(result.Latest!, result.Message);
                    break;
                case UpdateStatus.UpToDate:
                    Latest = null;
                    StatusText = silent ? "" : result.Message;
                    break;
                default:
                    _log?.Invoke(result.Message);
                    if (!silent)
                    {
                        StatusText = result.Message;
                    }

                    break;
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Remembers the newer release and opens the pop-up that tells about it.</summary>
    public void Offer(ReleaseInfo release, string message)
    {
        Latest = release;
        StatusText = message;
        IsPromptOpen = true;
    }

    [RelayCommand(CanExecute = nameof(NotInstalling))]
    private void ClosePrompt() => IsPromptOpen = false;

    /// <summary>Opens the page of the new release (not a file) and closes the pop-up.</summary>
    [RelayCommand(CanExecute = nameof(NotInstalling))]
    private void OpenRelease()
    {
        IsPromptOpen = false;
        _openLink(SafeReleaseUrl(Latest?.Url));
    }

    /// <summary>
    /// The address comes from GitHub's answer, so it is opened only when it really is a page of this project's own
    /// repository; anything else gives the repository's release page.
    /// </summary>
    public static string SafeReleaseUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/foxezol/FoxFix/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        return ReleasesUrl;
    }

    private static string Shorten(string notes)
    {
        var text = notes.Replace("\r\n", "\n").Trim();
        return text.Length <= NotesLimit ? text : text[..NotesLimit].TrimEnd() + "…";
    }
}
