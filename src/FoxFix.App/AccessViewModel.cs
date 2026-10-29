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
    /// <summary>Ordinary sites that open on any working connection; they show whether the internet itself is fine.</summary>
    private static readonly Uri[] Controls = [TrialTargets.Control, new("https://www.google.com/")];

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
        true => "Şu anda FoxDPI aktif. Açılmayan bir site varsa FoxDPI'ya rağmen erişilemiyor demektir.",
        false => "Şu anda FoxDPI devre dışı. Kesin bir karşılaştırma için FoxDPI aktifken testi tekrarlayabilirsiniz.",
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

        var all = new List<Uri>(Controls);
        all.AddRange(sites.Where(s => Controls.All(c => c.Host != s.Host)));

        IsRunning = true;
        SummaryText = "";
        Rows.Clear();
        foreach (var site in all)
        {
            Rows.Add(new AccessRow(site.Host, Controls.Contains(site)));
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
            foreach (var row in Rows.Where(r => r.StatusText == "Deneniyor…"))
            {
                row.StatusText = "durduruldu";
            }

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
        var controls = results.Take(Controls.Length).ToList();
        var sites = results.Skip(Controls.Length).ToList();

        // One ordinary site may be down on its own; only when none opens is it the connection.
        if (controls.All(c => !c.Opened))
        {
            SummaryText = "Normal sitelerin hiçbiri açılmadı. Sorun sitelerde değil, internet bağlantında olabilir. Bağlantı kontrolünü dene.";
            return;
        }

        if (sites.Count == 0)
        {
            SummaryText = "Normal siteler açılıyor.";
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
