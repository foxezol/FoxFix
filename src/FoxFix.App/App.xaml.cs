using System.Windows;
using System.IO;
using FoxFix.Core;
using FoxFix.Core.Commands;
using FoxFix.Core.Diagnostics;
using FoxFix.Core.Diagnostics.Checks;
using FoxFix.Core.Cleanup;
using FoxFix.Core.Dpi;
using FoxFix.Core.Startup;
using FoxFix.Core.Fixes;
using FoxFix.Core.GoodbyeDpi;
using FoxFix.Core.Network;
using FoxFix.Core.Reporting;
using FoxFix.Core.Safety;
using FoxFix.Core.SpeedTest;

namespace FoxFix.App;

public partial class App : Application
{
    private ThemeManager _theme = null!;
    private MainViewModel _main = null!;
    private MainWindow? _mainWindow;
    private FoxDpiHost _foxDpiHost = null!;
    private bool _mainStarted;

    protected override void OnExit(ExitEventArgs e)
    {
        _theme?.Dispose();
        base.OnExit(e);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if !DEBUG
        // Without administrator rights nothing here can work, so the program does not open at all.
        if (ElevationGate.BlockingMessage(new WindowsElevation().IsElevated) is { } blocked)
        {
            MessageBox.Show(blocked, "FoxFix", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(ElevationGate.ExitCode);
            return;
        }
#endif

        // FoxDPI can keep running in the notification area after the main window is closed, so the app ends on purpose.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // No silent crashes: anything unhandled is written to a log file and shown in plain words.
        DispatcherUnhandledException += (_, args) =>
        {
            var logPath = CrashLog.Write(args.Exception);
            MessageBox.Show(
                $"FoxFix beklenmedik bir hata verdi ve kapanacak.\n\n{args.Exception.Message}\n\nAyrıntılar şu dosyada: {logPath}",
                "FoxFix", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);

        // Follows the Windows light/dark setting live; "--tema=acik" or "--tema=koyu" forces one (handy for screenshots).
        _theme = new ThemeManager(Resources, ThemeManager.ParseForcedTheme(e.Args));
        _theme.Refresh();

        // FoxDPI.exe opens only FoxDPI: no splash, no main window.
        var onlyFoxDpi = e.Args.Contains(DirectStart.Argument);

        // Something to look at right away; the rest of the start-up happens behind it.
        SplashWindow? splash = null;
        if (!onlyFoxDpi)
        {
            splash = new SplashWindow();
            splash.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        var network = new SystemNetworkInfoProvider();
        var services = new WmiServiceSource();
        var runner = new ProcessCommandRunner();
        var serviceManager = new SystemServiceManager(runner);
        var dnsConfigurator = new SystemDnsConfigurator(runner);
        var gdpiFolder = GoodbyeDpiLayout.DefaultFolder;
        var launcher = new SystemGoodbyeDpiLauncher();

        // FoxFix's own GoodbyeDPI lives under ...\FoxFix; the detector must not call it a leftover.
        var detector = new DpiResidueDetector(
            services, new SystemProcessSource(), new FileSystemPathProbe(), network, managedFolder: Path.GetDirectoryName(gdpiFolder));
        var gdpiStatus = new GoodbyeDpiStatusReader(services, launcher, detector, gdpiFolder);

        var engine = new DiagnosticsEngine(
        [
            new AdapterCheck(network),
            new IpConfigCheck(network),
            new PingCheck(network, new SystemPinger()),
            new DnsCheck(new SystemDnsProbe(), RedirectIsFoxFixs(gdpiStatus)),
            new HttpsCheck(new SystemHttpsProbe()),
            new DpiResidueCheck(detector),
        ]);

        var cleanup = new DpiCleanupFix(detector, services, serviceManager, new SystemProcessKiller());
        var dnsRepair = new DnsRepairFix(network, services, dnsConfigurator);
        var dnsReset = new DnsResetFix(network, services, dnsConfigurator);
        var journal = new UndoJournal(UndoJournal.DefaultPath);
        var startupFix = new StartupFix(new RegistryStartupStore());
        var cleanupTargets = CleanupTargets.Default();
        var cleanupFix = new DiskCleanupFix(cleanupTargets);
        var payload = new EmbeddedGoodbyeDpiPayload();
        var gdpiInstall = new GoodbyeDpiInstallFix(payload, launcher, serviceManager, gdpiStatus, gdpiFolder);
        var gdpiTeardown = new GoodbyeDpiTeardown(serviceManager, launcher, gdpiFolder);
        var gdpiRemove = new GoodbyeDpiRemoveFix(gdpiStatus, gdpiTeardown, gdpiFolder);
        var zapretFolder = ZapretLayout.DefaultFolder;
        var zapretLauncher = new SystemGoodbyeDpiLauncher("winws");
        var zapretPayload = new EmbeddedGoodbyeDpiPayload("Zapret.");
        var zapretTeardown = new GoodbyeDpiTeardown(serviceManager, zapretLauncher, zapretFolder, ZapretLayout.ServiceName);
        var zapretRemove = new ZapretRemoveFix(zapretTeardown, zapretLauncher, zapretFolder);
        var zapretInstall = new GoodbyeDpiInstallFix(
            zapretPayload, zapretLauncher, serviceManager, gdpiStatus, zapretFolder,
            engine: DpiEngines.Zapret, serviceName: ZapretLayout.ServiceName, fixId: ZapretLayout.InstallFixId);
        var foxDpi = new FoxDpiSession(
            payload, launcher, gdpiStatus, gdpiFolder,
            otherEngines: [new DpiRuntime(DpiEngines.Zapret, zapretPayload, zapretLauncher, zapretFolder)]);
        var notice = new NoticeViewModel();
        var autoStart = new FoxDpiAutoStart(runner);
        var autoStartFix = new FoxDpiAutoStartFix(autoStart, () => Environment.ProcessPath, () => Environment.UserDomainName + "\\" + Environment.UserName);
        var elevation = new WindowsElevation();
        var fixFlow = new FixFlowViewModel(
            [cleanup, gdpiRemove, zapretRemove, dnsRepair, dnsReset, gdpiInstall, zapretInstall, autoStartFix, startupFix, cleanupFix], journal, elevation, new WindowsRebootService(runner));

        Func<string?> trialBlocker = () =>
        {
            var status = gdpiStatus.Read();
            return status.OtherRunning ? Strings.Get(Strings.GdpiBlockedForeign) : status.IsActive ? Strings.Get(Strings.GdpiBlockedManaged) : null;
        };
        var trial = new GoodbyeDpiTrial(
            payload, launcher, new SystemHttpsProbe(), serviceManager, gdpiFolder + "-trial",
            blocker: trialBlocker,
            flushDns: ct => dnsConfigurator.FlushCacheAsync(ct));
        var zapretTrial = new GoodbyeDpiTrial(
            zapretPayload, zapretLauncher, new SystemHttpsProbe(), serviceManager, zapretFolder + "-trial",
            blocker: trialBlocker,
            flushDns: ct => dnsConfigurator.FlushCacheAsync(ct),
            engine: DpiEngines.Zapret);
        var gdpi = new GoodbyeDpiViewModel(
            trial, gdpiInstall, gdpiStatus, gdpiTeardown, foxDpi, fixFlow, elevation, new DpiChoiceStore(), notice, autoStart, autoStartFix, zapretTrial, zapretInstall, zapretTeardown);

        var reports = new DiagnosticReportService(network, detector, journal, new SystemInfo());
        var speed = new SpeedTestViewModel(new SpeedTestService(new CloudflareSpeedTransport(), new StopwatchClock()));
        var startupStore = new RegistryStartupStore();
        var startupVm = new StartupViewModel(
            startupStore, startupFix.SetDesired, () => fixFlow.StartOptionalCommand.ExecuteAsync(StartupFix.FixIdValue), StartupIcons.Load);
        var cleanupVm = new CleanupViewModel(
            cleanupTargets, cleanupFix.Select, () => fixFlow.StartOptionalCommand.ExecuteAsync(DiskCleanupFix.FixIdValue));
        _main = new MainViewModel(
            engine, fixFlow, reports, new WpfClipboardService(), new WindowsReportFileSaver(),
            speed, startupVm, cleanupVm, gdpi, OpenLink, notice, new AccessViewModel(new AccessTester(new SystemHttpsProbe()), gdpi));

        // The small FoxDPI window (and its notification-area icon) exists exactly while FoxDPI runs.
        _foxDpiHost = new FoxDpiHost(
            foxDpi, _theme, openFoxFix: () => _ = OpenMainWindowAsync(), openSettings: () => _ = OpenMainWindowAsync(AppPage.DnsDpi), notify: notice.Show,
            ended: () =>
            {
                if (_mainWindow is not { IsLoaded: true })
                {
                    Shutdown();
                }
            });
        gdpi.FoxDpiStarted += profile => _foxDpiHost.Show(profile);

        if (onlyFoxDpi)
        {
            await RunFoxDpiOnlyAsync(foxDpi);
            return;
        }

        // Opening FoxFix is the first run: it saves the setting that FoxDPI.exe will use later.
        gdpi.RememberChoice();

        // Built but not shown: nothing of it is on screen until it is ready.
        var window = CreateMainWindow();

        // Looking is read-only, so the first thing the user sees is already the answer.
        var firstCheck = _main.RunDiagnosticsCommand.ExecuteAsync(null);
        _main.Startup.RefreshCommand.Execute(null);
        _main.Cleanup.ScanCommand.Execute(null);
        _ = gdpi.RefreshStatusAsync();
        _mainStarted = true;

        // Hand over only when there is something worth showing: the first check is in (but never wait more than a couple of seconds; the rest fills in on screen), and the
        // splash has been up long enough to be seen rather than flash.
        await Task.WhenAll(Task.Delay(800), Task.WhenAny(firstCheck, Task.Delay(2500)));

        // Shown fully transparent, given a moment to draw its first frame, then revealed as the splash fades away.
        window.Show();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        await Task.WhenAll(window.FadeInAsync(), splash!.FadeOutAsync());
        splash.Close();

        // Screenshot helpers: "--goster=foxdpi" opens the small FoxDPI window and "--goster=bildirim" a sample message, without changing anything.
        if (e.Args.Contains("--goster=foxdpi"))
        {
            new FoxDpiWindow(new FoxDpiWindowViewModel(GoodbyeDpiProfiles.All[0].Title, () => { }, () => { }), _theme).Show();
        }
        else if (e.Args.Contains("--goster=bildirim"))
        {
            notice.Show("FoxDPI başlamadı", Strings.Get(Strings.GdpiBlockedForeign));
        }

        // The elevated relaunch from the GoodbyeDPI page comes back to that page.
        if (e.Args.Contains("--sayfa=dpi"))
        {
            _main.OpenPageCommand.Execute(AppPage.DnsDpi);
        }

        // "FoxDPI Başlat" asked for administrator rights, so the app came back and now starts FoxDPI.
        if (e.Args.Contains(GoodbyeDpiViewModel.RelaunchArgument))
        {
            await gdpi.StartFoxDpiAsync();
        }

        try
        {
            await fixFlow.RefreshUndoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Geri alma kayıtları okunamadı: {ex.Message}", "FoxFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_theme) { DataContext = _main };
        window.Closed += (_, _) =>
        {
            // Closing FoxFix ends the app, unless FoxDPI is running: then it stays in the notification area.
            if (ReferenceEquals(_mainWindow, window) && !_foxDpiHost.IsActive)
            {
                Shutdown();
            }
        };
        _mainWindow = window;
        return window;
    }

    /// <summary>
    /// "FoxFix'i aç" and "DPI Ayarları" in the FoxDPI window or its icon: shows the main window (making a new one if it was
    /// closed, or the first one when the program started as FoxDPI.exe), optionally on a given page.
    /// </summary>
    private async Task OpenMainWindowAsync(AppPage? page = null)
    {
        if (!_mainStarted)
        {
            // Started as FoxDPI.exe: the main window's work has not run yet.
            _mainStarted = true;
            _ = _main.RunDiagnosticsCommand.ExecuteAsync(null);
            _main.Startup.RefreshCommand.Execute(null);
            _main.Cleanup.ScanCommand.Execute(null);
            _ = _main.Dpi?.RefreshStatusAsync();
            _main.Dpi?.RememberChoice();
        }

        if (page is { } target)
        {
            _main.OpenPageCommand.Execute(target);
        }

        if (_mainWindow is { IsLoaded: true } open)
        {
            open.Show();
            open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        var window = CreateMainWindow();
        window.Show();
        await window.FadeInAsync();
    }

    /// <summary>What FoxDPI.exe does: start with the saved setting and open only the small FoxDPI window.</summary>
    private async Task RunFoxDpiOnlyAsync(FoxDpiSession session)
    {
        var (profile, error) = DirectStart.Resolve(new DpiChoiceStore().Load(), new DpiChoiceStore().LoadCustom());
        if (profile is null)
        {
            MessageBox.Show(error, "FoxDPI", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(DirectStart.ExitCodeNeedsFoxFix);
            return;
        }

        var result = await session.StartAsync(profile, CancellationToken.None);
        if (!result.Started)
        {
            MessageBox.Show(result.Message, "FoxDPI başlamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        _foxDpiHost.Show(profile);
    }
    private static void OpenLink(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>The DNS redirect is expected while FoxFix's own GoodbyeDPI runs with a setting that redirects DNS.</summary>
    private static Func<bool> RedirectIsFoxFixs(GoodbyeDpiStatusReader status) => () =>
    {
        var current = status.Read();
        return current.IsActive && GoodbyeDpiProfiles.Find(current.State?.ProfileId)?.RedirectsDns == true;
    };
}
