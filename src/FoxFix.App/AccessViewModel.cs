using FoxFix.Core;
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

    /// <summary>While the site is being tried the text moves: "Deneniyor." "Deneniyor.." "Deneniyor...".</summary>
    [ObservableProperty]
    private string _statusText = Strings.Get(Strings.UiTestingBase) + ".";

    public bool IsTesting { get; private set; } = true;

    public void AdvanceDots(int step)
    {
        if (IsTesting)
        {
            StatusText = Strings.Get(Strings.UiTestingBase) + new string('.', step % 3 + 1);
        }
    }

    public void Stop()
    {
        IsTesting = false;
        StatusText = "durduruldu";
    }

    /// <summary>Ok, Bad or Neutral; the view maps it to a theme color.</summary>
    [ObservableProperty]
    private string _kind = "Neutral";

    [ObservableProperty]
    private string _elapsedText = "";

    public void Apply(AccessResult result)
    {
        IsTesting = false;
        StatusText = result.Text;
        Kind = result.Opened ? "Ok" : "Bad";
        ElapsedText = result.Opened ? $" · {result.Elapsed.TotalSeconds:0.0} sn" : "";
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
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _summaryText = "";

    public bool HasSummary => SummaryText.Length > 0;

    [ObservableProperty]
    private bool _isRunning;

    public bool HasRows => Rows.Count > 0;

    /// <summary>Tells whether FoxDPI is on, so the result is not read the wrong way round.</summary>
    public string DpiNote => _dpi?.IsActive switch
    {
        true => Strings.Get(Strings.AccessNoteDpiOn),
        false => Strings.Get(Strings.AccessNoteDpiOff),
        null => "",
    };

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken ct)
    {
        var sites = TrialTargets.Parse(Hosts);
        if (sites.Count == 0)
        {
            SummaryText = Strings.Get(Strings.AccessNeedHost);
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

        using var dotsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var dots = AnimateDotsAsync(dotsCts.Token);

        try
        {
            // Not Progress<T>: it posts later, so a row could change after the run ended.
            var results = await _tester.RunAsync(all, new InlineProgress(Show), ct);
            Summarize(results);
        }
        catch (OperationCanceledException)
        {
            foreach (var row in Rows.Where(r => r.IsTesting))
            {
                row.Stop();
            }

            SummaryText = Strings.Get(Strings.AccessStopped);
        }
        finally
        {
            dotsCts.Cancel();
            await dots;
            IsRunning = false;
        }
    }

    /// <summary>The dots after "Deneniyor" move while a site is being tried, so the screen does not look stuck.</summary>
    private async Task AnimateDotsAsync(CancellationToken ct)
    {
        var step = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(400, ct);
                step++;
                foreach (var row in Rows)
                {
                    row.AdvanceDots(step);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The test is over.
        }
    }

    private void Show(AccessResult result) =>
        Rows.FirstOrDefault(r => r.Host == result.Host && r.IsTesting)?.Apply(result);

    private void Summarize(IReadOnlyList<AccessResult> results)
    {
        var controls = results.Take(Controls.Length).ToList();
        var sites = results.Skip(Controls.Length).ToList();

        // One ordinary site may be down on its own; only when none opens is it the connection.
        if (controls.All(c => !c.Opened))
        {
            SummaryText = Strings.Get(Strings.AccessNoNormal);
            return;
        }

        if (sites.Count == 0)
        {
            SummaryText = Strings.Get(Strings.AccessNormalOpen);
            return;
        }

        var opened = sites.Count(s => s.Opened);
        SummaryText = opened == sites.Count
            ? sites.Count == 1 ? Strings.Get(Strings.AccessOneOpens) : Strings.Format(Strings.AccessAllOpen, sites.Count)
            : Strings.Format(Strings.AccessSomeOpen, sites.Count, opened);
    }

    private sealed class InlineProgress(Action<AccessResult> handler) : IProgress<AccessResult>
    {
        public void Report(AccessResult value) => handler(value);
    }
}
