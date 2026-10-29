using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.Safety;

namespace FoxFix.Core.GoodbyeDpi;

/// <summary>
/// Installs FoxFix's own, hash-checked copy of GoodbyeDPI with the chosen setting as a Windows service that starts
/// with the computer. Undo removes everything again.
/// </summary>
public sealed class GoodbyeDpiInstallFix : IFix
{
    public const string FixIdValue = "goodbyedpi-install";

    private readonly IGoodbyeDpiPayload _payload;
    private readonly IServiceManager _services;
    private readonly GoodbyeDpiStatusReader _status;
    private readonly GoodbyeDpiTeardown _teardown;
    private readonly string _folder;
    private readonly IReadOnlyDictionary<string, string>? _expectedHashes;
    private readonly DpiEngine _engine;
    private readonly string _serviceName;
    private readonly string _fixId;

    private GoodbyeDpiProfile? _profile;

    public GoodbyeDpiInstallFix(
        IGoodbyeDpiPayload payload,
        IGoodbyeDpiLauncher launcher,
        IServiceManager services,
        GoodbyeDpiStatusReader status,
        string folder,
        IReadOnlyDictionary<string, string>? expectedHashes = null,
        DpiEngine? engine = null,
        string? serviceName = null,
        string? fixId = null)
    {
        _engine = engine ?? DpiEngines.GoodbyeDpi;
        _serviceName = serviceName ?? GoodbyeDpiLayout.ServiceName;
        _fixId = fixId ?? FixIdValue;
        _expectedHashes = expectedHashes;
        _payload = payload;
        _services = services;
        _status = status;
        _folder = folder;
        _teardown = new GoodbyeDpiTeardown(services, launcher, folder, _serviceName);
    }

    public string Id => _fixId;

    public string Title => Strings.Get(Strings.GdpiInstallTitle);

    public bool IncludedInRepair => false;

    public bool RequiresAdmin => true;

    public void Configure(string profileId) => _profile = GoodbyeDpiProfiles.Find(profileId);

    /// <summary>For a setting that is not one of the ready-made ones (the user edited it).</summary>
    public void Configure(GoodbyeDpiProfile profile) => _profile = profile;

    public Task<FixPreview> PreviewAsync(CancellationToken ct)
    {
        if (_profile is null)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.GdpiPickFirst)], HasChanges: false));
        }

        var status = _status.Read();
        if (status.OtherRunning)
        {
            return Task.FromResult(new FixPreview([Strings.Get(Strings.GdpiBlockedForeign)], HasChanges: false));
        }

        var lines = new List<string>
        {
            Strings.Format(Strings.GdpiPreviewProfile, _profile.Title),
            Strings.Format(Strings.GdpiPreviewFiles, _folder),
            Strings.Get(Strings.GdpiPreviewService),
        };

        if (_profile.RedirectsDns)
        {
            lines.Add(Strings.Get(Strings.GdpiPreviewDns));
        }

        if (status.IsInstalled)
        {
            lines.Add(Strings.Get(Strings.GdpiPreviewReplace));
        }

        lines.Add(Strings.Get(Strings.GdpiPreviewDriver));
        return Task.FromResult(new FixPreview(lines));
    }

    public async Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct)
    {
        if (_profile is null)
        {
            return new FixResult(false, Strings.Get(Strings.GdpiPickFirst));
        }

        if (_status.Read().OtherRunning)
        {
            return new FixResult(false, Strings.Get(Strings.GdpiBlockedForeign));
        }

        // An earlier FoxFix install is replaced, not stacked.
        await _teardown.RemoveAsync(deleteFolder: false, ct);

        var record = new UndoRecord(
            Guid.NewGuid(), _fixId, DateTimeOffset.UtcNow,
            Strings.Format(Strings.GdpiUndoDescription, _profile.Title),
            new Dictionary<string, string>
            {
                ["folder"] = _folder,
                ["profile"] = _profile.Id,
            });

        // Journal first: from here on there is something to take back.
        await journal.AppendAsync(record, ct);

        var problems = GoodbyeDpiFiles.Prepare(_payload, _folder, _expectedHashes ?? _engine.Hashes);
        if (problems.Count > 0)
        {
            return await FailAsync(journal, record, Strings.Format(Strings.GdpiFilesBroken, string.Join(", ", problems)));
        }

        if (!await StartServiceAsync(_profile, ct))
        {
            return await FailAsync(journal, record, Strings.Get(Strings.GdpiServiceFailed));
        }

        new GoodbyeDpiState(_profile.Id).Write(_folder);
        return new FixResult(true, Strings.Format(Strings.GdpiResultService, _profile.Title));
    }

    public async Task UndoAsync(UndoRecord record, CancellationToken ct)
    {
        // Only FoxFix's own folder is ever touched, whatever the record says.
        await _teardown.RemoveAsync(deleteFolder: true, ct);
    }

    private async Task<bool> StartServiceAsync(GoodbyeDpiProfile profile, CancellationToken ct)
    {
        var config = new ServiceConfig(
            _serviceName, Strings.Get(Strings.GdpiServiceDisplayName),
            $"\"{_engine.ExePath(_folder)}\" {_engine.BuildArguments(_folder, profile)}", StartType: 2, ServiceType: 0x10, IsDriver: false);

        return await _services.CreateAsync(config, ct) && await _services.StartAsync(_serviceName, ct);
    }

    private async Task<FixResult> FailAsync(UndoJournal journal, UndoRecord record, string message)
    {
        await _teardown.RemoveAsync(deleteFolder: true, CancellationToken.None);
        await journal.RemoveAsync(record.Id, CancellationToken.None);
        return new FixResult(false, message);
    }
}
