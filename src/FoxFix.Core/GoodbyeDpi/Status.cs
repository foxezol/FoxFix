using FoxFix.Core.Dpi;

namespace FoxFix.Core.GoodbyeDpi;

public sealed record GoodbyeDpiStatus(
    bool ServiceInstalled,
    bool ServiceRunning,
    bool ManualRunning,
    bool OtherRunning,
    GoodbyeDpiState? State,
    bool OwnDriverRegistered = false,
    bool ZapretServiceInstalled = false,
    bool ZapretServiceRunning = false)
{
    /// <summary>FoxFix's own GoodbyeDPI (as a service or a window) or Zapret service is doing its job right now.</summary>
    public bool IsActive => ServiceRunning || ManualRunning || ZapretServiceRunning;

    public bool IsInstalled => ServiceInstalled || ManualRunning;
}

/// <summary>Read-only: what FoxFix installed, and whether somebody else's GoodbyeDPI or Zapret is running.</summary>
public sealed class GoodbyeDpiStatusReader
{
    private readonly IServiceSource _services;
    private readonly IGoodbyeDpiLauncher _launcher;
    private readonly DpiResidueDetector _detector;
    private readonly string _folder;

    public GoodbyeDpiStatusReader(IServiceSource services, IGoodbyeDpiLauncher launcher, DpiResidueDetector detector, string folder)
    {
        _services = services;
        _launcher = launcher;
        _detector = detector;
        _folder = folder;
    }

    public GoodbyeDpiStatus Read()
    {
        var entries = _services.GetServicesAndDrivers();
        var service = entries
            .FirstOrDefault(s => !s.IsDriver && s.Name.Equals(GoodbyeDpiLayout.ServiceName, StringComparison.OrdinalIgnoreCase));
        var zapretService = entries
            .FirstOrDefault(s => !s.IsDriver && s.Name.Equals(ZapretLayout.ServiceName, StringComparison.OrdinalIgnoreCase));
        var ownDriver = entries.Any(s => s.IsDriver && GoodbyeDpiLayout.IsManaged(s.PathName, GoodbyeDpiLayout.OwnRoot(_folder)));

        // The detector skips everything inside FoxFix's folder, so a service or program found running here belongs to someone else.
        // A driver that is still loaded is not counted: on its own it does not stop FoxDPI from working.
        var other = _detector.Scan().Any(f =>
            f.IsActive
            && f.Kind is ResidueKind.Service or ResidueKind.Process
            && f.RuleId is DpiSignatures.GoodbyeDpi or DpiSignatures.WinDivert or DpiSignatures.Zapret);

        return new GoodbyeDpiStatus(
            ServiceInstalled: service is not null,
            ServiceRunning: service?.IsRunning == true,
            ManualRunning: _launcher.IsRunningFromFolder(_folder),
            OtherRunning: other,
            State: GoodbyeDpiState.Read(_folder),
            OwnDriverRegistered: ownDriver,
            ZapretServiceInstalled: zapretService is not null,
            ZapretServiceRunning: zapretService?.IsRunning == true);
    }
}
