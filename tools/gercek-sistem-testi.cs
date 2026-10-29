#:project ../src/FoxFix.Core/FoxFix.Core.csproj
#:property PublishAot=false
#:property TargetFramework=net10.0-windows

// FoxFix'in gercek sistem kodlarini (sc, netsh, kayit defteri, shutdown) sanal makine olmadan,
// ZARARSIZ hedeflerle dener. Yonetici gerektirir.
//
//   dotnet run tools\gercek-sistem-testi.cs -- <log dosyasi>
//
// Neye dokunur:
//   - "FoxFixTestGDPI" adli sahte bir hizmet olusturur, FoxFix'in gercek temizleme duzeltmesiyle siler,
//     geri alir, sonra tamamen kaldirir. Gercek GoodbyeDPI / WinDivert'e DOKUNMAZ (dedektor yalnizca
//     bu sahte hizmeti gorur).
//   - Ag kartlarinin DNS ayarini okur ve AYNI DEGERLERLE geri yazar (net degisiklik yok).
//   - 600 saniyelik yeniden baslatma planlar ve HEMEN iptal eder.

using System.Security.Principal;
using FoxFix.Core.Commands;
using FoxFix.Core.Dpi;
using FoxFix.Core.Fixes;
using FoxFix.Core.Network;
using FoxFix.Core.Safety;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var logPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "foxfix-gercek-test.txt");
var log = new List<string>();
var failures = 0;

void Write(string line)
{
    Console.WriteLine(line);
    log.Add(line);
}

void Check(string name, bool pass, string detail = "")
{
    if (!pass)
    {
        failures++;
    }

    Write($"{(pass ? "GECTI" : "KALDI")}  {name}{(detail.Length > 0 ? "  |  " + detail : "")}");
}

bool isAdmin;
using (var identity = WindowsIdentity.GetCurrent())
{
    isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

if (!isAdmin)
{
    Write("Yönetici olarak çalışmıyorum. Hiçbir şey yapılmadı.");
    File.WriteAllLines(logPath, log);
    return 2;
}

var ct = CancellationToken.None;
var runner = new ProcessCommandRunner();
var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
var netshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

const string DummyName = "FoxFixTestGDPI";
const string TestNetServer = "203.0.113.250";

var work = Path.Combine(Path.GetTempPath(), "foxfix-gercek-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
var dummyExe = Path.Combine(work, "goodbyedpi.exe");
File.Copy(Path.Combine(Environment.SystemDirectory, "notepad.exe"), dummyExe);

var wmi = new WmiServiceSource();
string GdpiState() =>
    wmi.GetServicesAndDrivers().FirstOrDefault(s => s.Name == "GoodbyeDPI") is { } g ? (g.IsRunning ? "çalışıyor" : "durmuş") : "yok";

var gdpiBefore = GdpiState();
Write($"Gerçek GoodbyeDPI hizmeti (dokunulmayacak): {gdpiBefore}");

var network = new SystemNetworkInfoProvider();
var dns = new SystemDnsConfigurator(runner);
var adapters = network.GetAdapters().Where(a => a.IsPhysical && a.IsUp).ToList();
var dnsBefore = new Dictionary<string, DnsSnapshot>();
foreach (var adapter in adapters)
{
    var snapshot = await dns.ReadAsync(adapter, ct);
    if (snapshot is not null)
    {
        dnsBefore[adapter.Name] = snapshot;
    }
}

Write("");

try
{
    // ---------- A) Sahte hizmetle gercek temizleme ve geri alma ----------
    Write("== A) Sahte hizmet: gercek sc ile olustur, FoxFix ile sil, geri al ==");

    var create = await runner.RunAsync(
        scPath,
        ["create", DummyName, "binPath=", $"\"{dummyExe}\" -5", "start=", "demand", "DisplayName=", "FoxFix test hizmeti"],
        ct);
    Check("A1 sahte hizmet olusturuldu", create.Success, $"sc cikis kodu {create.ExitCode}");

    if (create.Success)
    {
        var only = new OnlyService(wmi, DummyName);
        var detector = new DpiResidueDetector(only, new NoProcesses(), new FileSystemPathProbe(), network, []);
        var manager = new SystemServiceManager(runner);
        var fix = new DpiCleanupFix(detector, only, manager, new SystemProcessKiller(), TimeSpan.FromSeconds(5));
        var journal = new UndoJournal(Path.Combine(work, "undo.json"));

        var before = await manager.ReadConfigAsync(DummyName, ct);
        Check(
            "A2 kayit defterinden hizmet ayari okundu",
            before is not null && before.BinaryPath.Contains("goodbyedpi.exe") && before.StartType == 3,
            before?.ToString() ?? "yok");

        var preview = await fix.PreviewAsync(ct);
        Check(
            "A3 onizleme yalnizca sahte hizmeti listeledi",
            preview.HasChanges && preview.Changes.Count(l => l.Contains("silinecek")) == 1 && preview.Changes.Any(l => l.Contains(DummyName)),
            string.Join(" / ", preview.Changes));

        var result = await fix.ApplyAsync(journal, ct);
        Check("A4 temizleme basarili", result.Success, result.Summary);

        var gone = await manager.ReadConfigAsync(DummyName, ct);
        var query = await runner.RunAsync(scPath, ["query", DummyName], ct);
        Check("A5 hizmet gercekten silindi", gone is null && query.ExitCode == 1060, $"sc query cikis kodu {query.ExitCode}");

        var records = await journal.LoadAsync(ct);
        Check(
            "A6 geri alma kaydi yazildi",
            records.Count == 1 && records[0].Data["name"] == DummyName && records[0].Data["binaryPath"].Contains("goodbyedpi.exe"),
            $"{records.Count} kayit");

        if (records.Count == 1)
        {
            try
            {
                await fix.UndoAsync(records[0], ct);
                var after = await manager.ReadConfigAsync(DummyName, ct);
                Check(
                    "A7 geri alma hizmeti ayni ayarlarla yeniden kurdu",
                    after is not null && before is not null
                        && after.BinaryPath == before.BinaryPath && after.StartType == before.StartType && after.ServiceType == before.ServiceType,
                    after?.ToString() ?? "yok");
            }
            catch (Exception ex)
            {
                Check("A7 geri alma hizmeti ayni ayarlarla yeniden kurdu", false, ex.Message);
            }
        }
    }

    // ---------- B) Gercek DNS geri yukleme yolu, net degisiklik yok ----------
    Write("");
    Write("== B) Gercek DNS: eski ayari oku, AYNI degerlerle geri yaz ==");

    foreach (var adapter in adapters)
    {
        if (!dnsBefore.TryGetValue(adapter.Name, out var snapshot))
        {
            Check($"B {adapter.Name}: eski ayar okundu", false, "okunamadi");
            continue;
        }

        var restored = await dns.RestoreAsync(snapshot, ct);
        var again = await dns.ReadAsync(adapter, ct);
        var same = again is not null
            && again.V4Servers.SequenceEqual(snapshot.V4Servers)
            && again.V6Servers.SequenceEqual(snapshot.V6Servers);

        Check(
            $"B {adapter.Name}: geri yukleme calisti, ayar ayni kaldi",
            restored && same,
            $"v4=[{string.Join(",", snapshot.V4Servers)}] v6=[{string.Join(",", snapshot.V6Servers)}] -> v4=[{string.Join(",", again?.V4Servers ?? [])}] v6=[{string.Join(",", again?.V6Servers ?? [])}]");
    }

    var resolves = await new SystemDnsProbe().ResolveWithSystemAsync("google.com", TimeSpan.FromSeconds(5), ct);
    Check("B Windows hala site adlarini cozuyor", resolves);

    // ---------- D) DNS onbellegi ----------
    Write("");
    Write("== D) DNS onbellegini temizle ==");
    Check("D1 ipconfig /flushdns basarili", await dns.FlushCacheAsync(ct));

    // ---------- E) Yeniden baslatmayi planla ve hemen iptal et ----------
    Write("");
    Write("== E) Yeniden baslatma: 600 sn planla, hemen iptal et ==");

    var scheduled = await runner.RunAsync(shutdownPath, ShutdownArguments.Restart(600, "FoxFix testi, hemen iptal edilecek"), ct);
    var aborted = await runner.RunAsync(shutdownPath, ShutdownArguments.Abort(), ct);
    var abortedAgain = await runner.RunAsync(shutdownPath, ShutdownArguments.Abort(), ct);
    Check("E1 yeniden baslatma planlandi", scheduled.Success, $"cikis kodu {scheduled.ExitCode}");
    Check("E2 iptal edildi", aborted.Success, $"cikis kodu {aborted.ExitCode}");
    Check("E3 iptalden sonra planli bir sey kalmadi", abortedAgain.ExitCode == 1116, $"cikis kodu {abortedAgain.ExitCode}");
}
catch (Exception ex)
{
    Check("Test beklenmedik bir hatayla durdu", false, ex.ToString());
}
finally
{
    // Ne olursa olsun: arkada hicbir sey birakma.
    Write("");
    Write("== Temizlik ==");
    await Safely(() => runner.RunAsync(scPath, ["delete", DummyName], ct));
    await Safely(() => runner.RunAsync(shutdownPath, ShutdownArguments.Abort(), ct));
    foreach (var snapshot in dnsBefore.Values)
    {
        await Safely(() => dns.RestoreAsync(snapshot, ct));
    }

    try
    {
        Directory.Delete(work, recursive: true);
    }
    catch (IOException)
    {
        // gecici klasor, onemsiz
    }

    var leftover = await runner.RunAsync(scPath, ["query", DummyName], ct);
    Check("Sahte hizmet sistemde kalmadi", leftover.ExitCode == 1060, $"sc query cikis kodu {leftover.ExitCode}");
    Check("Gercek GoodbyeDPI hizmeti dokunulmadi", GdpiState() == gdpiBefore, $"{gdpiBefore} -> {GdpiState()}");
}

Write("");
Write(failures == 0 ? "SONUC: hepsi gecti." : $"SONUC: {failures} adim KALDI.");
File.WriteAllLines(logPath, log, new System.Text.UTF8Encoding(true));
return failures == 0 ? 0 : 1;

static async Task Safely(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  (temizlik adimi hata verdi: {ex.Message})");
    }
}

/// <summary>Detector and fix only see the dummy service, so the real GoodbyeDPI can never be in the plan.</summary>
sealed class OnlyService(IServiceSource inner, string name) : IServiceSource
{
    public IReadOnlyList<ServiceEntry> GetServicesAndDrivers() =>
        inner.GetServicesAndDrivers().Where(s => s.Name == name).ToList();
}

sealed class NoProcesses : IProcessSource
{
    public IReadOnlyList<ProcessEntry> GetProcesses() => [];
}
