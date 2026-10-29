using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoxFix.Core;
using FoxFix.Core.SpeedTest;

namespace FoxFix.App;

/// <summary>Runs the speed test on demand, shows live numbers and lets the user stop it.</summary>
public sealed partial class SpeedTestViewModel : ObservableObject
{
    private const string NoValue = "–";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly ISpeedTest _test;

    private CancellationTokenSource? _cts;

    public SpeedTestViewModel(ISpeedTest test)
    {
        _test = test;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = Strings.Get(Strings.SpeedIdle);

    [ObservableProperty]
    private string _downloadText = NoValue;

    [ObservableProperty]
    private string _uploadText = NoValue;

    [ObservableProperty]
    private string _latencyText = NoValue;

    [ObservableProperty]
    private string _jitterText = NoValue;

    [ObservableProperty]
    private bool _hasResult;

    public bool CanStart => !IsRunning;

    public bool CanCancel => IsRunning;

    /// <summary>A decimal comma and one decimal place for small values: "87,3", "1240".</summary>
    public static string FormatNumber(double value) => value.ToString(value >= 100 ? "0" : "0.#", Turkish);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        HasResult = false;
        DownloadText = UploadText = LatencyText = JitterText = NoValue;
        Status = Strings.Get(Strings.SpeedLatencyPhase);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var result = await _test.RunAsync(new UiProgress(OnProgress), token);

            DownloadText = Mbps(result.DownloadMbps);
            UploadText = Mbps(result.UploadMbps);
            LatencyText = Milliseconds(result.LatencyMs);
            JitterText = Milliseconds(result.JitterMs);
            HasResult = true;
            Status = Strings.Get(Strings.SpeedDone);
        }
        catch (OperationCanceledException)
        {
            Status = Strings.Get(Strings.SpeedCancelled);
        }
        catch (Exception ex)
        {
            Status = Strings.Format(Strings.SpeedFailed, ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private void OnProgress(SpeedTestProgress progress)
    {
        // Progress callbacks can arrive after the run has ended; they must not overwrite the final numbers.
        if (!IsRunning)
        {
            return;
        }

        switch (progress.Phase)
        {
            case SpeedTestPhase.Latency:
                Status = Strings.Get(Strings.SpeedLatencyPhase);
                LatencyText = Milliseconds(progress.LatencyMs ?? 0);
                break;
            case SpeedTestPhase.Download:
                Status = Live(Strings.SpeedDownloadPhase, progress.CurrentMbps);
                DownloadText = Mbps(progress.CurrentMbps ?? 0);
                break;
            case SpeedTestPhase.Upload:
                Status = Live(Strings.SpeedUploadPhase, progress.CurrentMbps);
                UploadText = Mbps(progress.CurrentMbps ?? 0);
                break;
        }
    }

    private static string Live(string phaseKey, double? mbps) =>
        mbps is null ? Strings.Get(phaseKey) : Strings.Get(phaseKey).TrimEnd('…') + $" {FormatNumber(mbps.Value)} Mb/sn";

    private static string Mbps(double value) => Strings.Format(Strings.SpeedMbps, FormatNumber(value));

    private static string Milliseconds(double value) => Strings.Format(Strings.SpeedMs, FormatNumber(value));

    /// <summary>Posts to the UI thread when there is one; runs inline otherwise (tests).</summary>
    private sealed class UiProgress : IProgress<SpeedTestProgress>
    {
        private readonly Action<SpeedTestProgress> _handler;
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;

        public UiProgress(Action<SpeedTestProgress> handler) => _handler = handler;

        public void Report(SpeedTestProgress value)
        {
            if (_context is null)
            {
                _handler(value);
            }
            else
            {
                _context.Post(_ => _handler(value), null);
            }
        }
    }
}
