using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core.GoodbyeDpi;

namespace FoxFix.App;

/// <summary>One line of the access test: a site and whether it opened.</summary>
public sealed partial class AccessRow : ObservableObject
{
    public AccessRow(string host, bool isControl)
    {
        Host = host;
        IsControl = isControl;
    }

    public string Host { get; }

    /// <summary>The ordinary site that shows whether the internet itself works.</summary>
    public bool IsControl { get; }

    [ObservableProperty]
    private string _statusText = "Deneniyor…";

    /// <summary>Ok, Bad or Neutral; the view maps it to a theme color.</summary>
    [ObservableProperty]
    private string _kind = "Neutral";

    [ObservableProperty]
    private string _elapsedText = "";

    public void Apply(AccessResult result)
    {
        StatusText = result.Text;
        Kind = result.Opened ? "Ok" : "Bad";
        ElapsedText = result.Opened ? $"{result.Elapsed.TotalSeconds:0.0} sn" : "";
    }
}

/// <summary>"Erişim testi": tries the user's sites and says which open. It only looks and changes nothing.</summary>
public sealed partial class AccessViewModel : ObservableObject
{
    private readonly AccessTester _tester;
    private readonly GoodbyeDpiViewModel? _dpi;

    public AccessViewModel(AccessTester tester, GoodbyeDpiViewModel? dpi = null)
    {
        _tester = tester;
        _dpi = dpi;

        if (dpi is not null)
        {
            dpi.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GoodbyeDpiViewModel.IsActive))
                {
                    OnPropertyChanged(nameof(DpiNote));
                }
            };
        }
    }

    public ObservableCollection<AccessRow> Rows { get; } = [];

    [ObservableProperty]
    private string _hosts = GoodbyeDpiViewModel.DefaultHosts;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private bool _isRunning;

    public bool HasRows => Rows.Count > 0;

    /// <summary>Tells whether FoxDPI is on, so the result is not read the wrong way round.</summary>
    public string DpiNote => _dpi?.IsActive switch
    {
        true => "FoxDPI açık. Açılmayan site varsa FoxDPI'ya rağmen açılmıyor demektir.",
        false => "FoxDPI kapalı. Sonucu FoxDPI açıkken bir de bu şekilde karşılaştırabilirsin.",
        null => "",
    };

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken ct)
    {
        var sites = TrialTargets.Parse(Hosts);
        if (sites.Count == 0)
        {
            SummaryText = "Denenecek bir site adı yaz. Örnek: discord.com";
            return;
        }

        var all = new List<Uri> { TrialTargets.Control };
        all.AddRange(sites);

        IsRunning = true;
        SummaryText = "";
        Rows.Clear();
        foreach (var site in all)
        {
            Rows.Add(new AccessRow(site.Host, site == TrialTargets.Control));
        }

        OnPropertyChanged(nameof(HasRows));

        try
        {
            // Not Progress<T>: it posts later, so a row could change after the run ended.
            var results = await _tester.RunAsync(all, new InlineProgress(Show), ct);
            Summarize(results);
        }
        catch (OperationCanceledException)
        {
            SummaryText = "Test durduruldu.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Show(AccessResult result) =>
        Rows.FirstOrDefault(r => r.Host == result.Host && r.StatusText == "Deneniyor…")?.Apply(result);

    private void Summarize(IReadOnlyList<AccessResult> results)
    {
        var control = results[0];
        var sites = results.Skip(1).ToList();

        if (!control.Opened)
        {
            SummaryText = "Normal bir site bile açılmadı. Sorun sitelerde değil, internet bağlantında olabilir. Bağlantı kontrolünü dene.";
            return;
        }

        var opened = sites.Count(s => s.Opened);
        SummaryText = opened == sites.Count
            ? sites.Count == 1 ? "Site açılıyor." : $"{sites.Count} sitenin hepsi açılıyor."
            : $"{sites.Count} siteden {opened} tanesi açılıyor.";
    }

    private sealed class InlineProgress(Action<AccessResult> handler) : IProgress<AccessResult>
    {
        public void Report(AccessResult value) => handler(value);
    }
}
