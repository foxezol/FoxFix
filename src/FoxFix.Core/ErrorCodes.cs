namespace FoxFix.Core;

/// <summary>One thing that can go wrong, in words: its code, what it means and what the person can do.</summary>
public sealed record ErrorInfo(string Code, string Title, string Remedy);

/// <summary>
/// The error codes. Every message that says "[FX-xxxx]" is listed here and in docs/HATA-KODLARI.md, so no error is just
/// an unexplained sentence. 1xxx the app, 2xxx FoxDPI, 3xxx the scheduled task, 4xxx changes to the system, 5xxx reports,
/// 6xxx measurements, 9xxx the unexpected.
/// </summary>
public static class ErrorCodes
{
    public const string AdminRequired = "FX-1001";
    public const string AdminDenied = "FX-1002";
    public const string LinkFailed = "FX-1003";
    public const string ClipboardFailed = "FX-1004";
    public const string JournalUnreadable = "FX-1005";
    public const string DpiFilesBroken = "FX-2001";
    public const string DpiStartFailed = "FX-2002";
    public const string DpiNotRunning = "FX-2003";
    public const string DpiStoppedUnexpectedly = "FX-2004";
    public const string DpiOtherRunning = "FX-2005";
    public const string DpiAlreadyRunning = "FX-2006";
    public const string DpiServiceFailed = "FX-2007";
    public const string TrialFileError = "FX-2008";
    public const string TrialCouldNotStart = "FX-2009";
    public const string StatusUnreadable = "FX-2010";
    public const string DpiStopFailed = "FX-2011";
    public const string NeedsFirstRun = "FX-2012";
    public const string TaskFailed = "FX-3001";
    public const string TaskNoPath = "FX-3002";
    public const string PreviewFailed = "FX-4001";
    public const string ApplyFailed = "FX-4002";
    public const string UndoFailed = "FX-4003";
    public const string DnsChangeFailed = "FX-4004";
    public const string ServiceChangeFailed = "FX-4005";
    public const string StartupChangeFailed = "FX-4006";
    public const string CleanupFailed = "FX-4007";
    public const string RebootFailed = "FX-4008";
    public const string ReportBuildFailed = "FX-5001";
    public const string ReportSaveFailed = "FX-5002";
    public const string SpeedFailed = "FX-6001";
    public const string CheckFailed = "FX-6002";
    public const string Unexpected = "FX-9001";

    public static IReadOnlyList<ErrorInfo> All { get; } =
    [
        new(AdminRequired, "Yönetici izni gerekli", "Uygulamaya sağ tıklayıp \"Yönetici olarak çalıştır\" de."),
        new(AdminDenied, "Yönetici izni verilmedi", "Windows sorusuna \"Evet\" de. Hiçbir şey değişmedi, istediğin zaman yeniden dene."),
        new(LinkFailed, "Bağlantı açılamadı", "Varsayılan tarayıcının ayarlı olduğundan emin ol, adresi elle açabilirsin."),
        new(ClipboardFailed, "Panoya kopyalanamadı", "Pano başka bir program tarafından tutuluyor olabilir. Birkaç saniye sonra yeniden dene."),
        new(JournalUnreadable, "Geri alma kayıtları okunamadı", "Kayıt dosyası bozulmuş olabilir. FoxFix o kayıtlar olmadan açılır; eski değişiklikler Geri Al listesinde görünmez."),
        new(DpiFilesBroken, "Dosya doğrulaması tutmadı", "Antivirüs dosyayı silmiş ya da değiştirmiş olabilir. FoxFix klasörünü antivirüs istisnasına ekle ve DPI Sıfırlama ile yeniden dene."),
        new(DpiStartFailed, "FoxDPI başlatılamadı", "Yönetici izniyle çalıştığından emin ol, antivirüsü kontrol et. Ayrıntı satırındaki Windows mesajı nedeni söyler."),
        new(DpiNotRunning, "FoxDPI başlayıp hemen kapandı", "Antivirüs engelliyor olabilir ya da başka bir DPI aracı çalışıyordur. DPI Sıfırlama yapıp yeniden dene."),
        new(DpiStoppedUnexpectedly, "FoxDPI beklenmedik şekilde durdu", "FoxDPI'yı yeniden başlat. Sürekli oluyorsa bilgisayarı yeniden başlatıp DPI Sıfırlama yap."),
        new(DpiOtherRunning, "Başka bir DPI aracı çalışıyor", "Diğer GoodbyeDPI/Zapret/WinDivert araçlarını kapat ya da DPI Sıfırlama ile kaldır."),
        new(DpiAlreadyRunning, "FoxDPI zaten çalışıyor", "FoxDPI penceresini kapat ya da hizmeti durdur, sonra yeniden dene."),
        new(DpiServiceFailed, "Hizmet kurulamadı ya da başlamadı", "Yapılan her şey geri alındı. Antivirüsü kontrol et, FoxDPI penceresiyle çalıştırmayı dene."),
        new(TrialFileError, "Deneme bir dosya hatasıyla durdu", "Sürücü bellekte kalmış olabilir. Bilgisayarı yeniden başlat ya da DPI Sıfırlama yap, sonra yeniden dene."),
        new(TrialCouldNotStart, "Deneme başlatılamadı", "İlk ayar bile başlamadıysa neden hepsinde aynıdır: antivirüs ya da yönetici izni. FX-2002 çözümlerine bak."),
        new(StatusUnreadable, "Durum okunamadı", "Windows hizmet listesi okunamadı. Uygulamayı yönetici olarak yeniden aç."),
        new(DpiStopFailed, "FoxDPI durdurulamadı", "Görev Yöneticisi'nden goodbyedpi.exe işlemini bitir ya da DPI Sıfırlama yap."),
        new(NeedsFirstRun, "İlk çalıştırma gerekli", "Önce FoxFix.exe'yi bir kez aç, sonra FoxDPI.exe kullanılabilir."),
        new(TaskFailed, "Zamanlanmış görev ayarlanamadı", "Yönetici izniyle çalıştığından emin ol. Görev Zamanlayıcı hizmeti kapalı olabilir."),
        new(TaskNoPath, "FoxFix'in yeri bulunamadı", "FoxFix.exe'yi sabit bir klasörden aç (örneğin kurulum klasörü) ve kutuyu yeniden işaretle."),
        new(PreviewFailed, "Önizleme hazırlanamadı", "Hiçbir şey değişmedi. Yeniden dene; sürerse raporu kaydedip gönder."),
        new(ApplyFailed, "Uygulanırken hata çıktı", "Ayrıntı satırına bak. Bir kısmı uygulanmış olabilir: Rapor ve geçmiş sayfasından Geri Al."),
        new(UndoFailed, "Geri alınamadı", "Yönetici izniyle yeniden dene. Eski hizmet ya da DNS ayarını elle de geri kurabilirsin."),
        new(DnsChangeFailed, "DNS ayarı değiştirilemedi", "Bağlantı adı değişmiş ya da bağlantı kopmuş olabilir. Bağlantıyı kontrol edip yeniden dene."),
        new(ServiceChangeFailed, "Hizmet durdurulamadı ya da geri kurulamadı", "Hizmet başka bir program tarafından kullanılıyor olabilir. Bilgisayarı yeniden başlatıp dene."),
        new(StartupChangeFailed, "Açılış ayarı değiştirilemedi", "Kayıt defteri girdisi korumalı olabilir. Yönetici izniyle yeniden dene."),
        new(CleanupFailed, "Dosyalar temizlenemedi", "Dosyalar kullanımda olabilir. İlgili programları kapatıp yeniden dene."),
        new(RebootFailed, "Yeniden başlatma ayarlanamadı ya da iptal edilemedi", "Bilgisayarı kendin yeniden başlat. İptal gerekiyorsa komut satırında shutdown /a yaz."),
        new(ReportBuildFailed, "Rapor hazırlanamadı", "Yeniden dene. Sürerse uygulamayı kapatıp aç."),
        new(ReportSaveFailed, "Rapor kaydedilemedi", "Başka bir klasör seç ya da dosya adının geçerli olduğundan emin ol."),
        new(SpeedFailed, "Hız ölçülemedi", "İnternet bağlantını ve Cloudflare'e erişimini kontrol et, yeniden dene."),
        new(CheckFailed, "Bir kontrol çalışırken hata çıktı", "Diğer kontroller etkilenmez. \"Yeniden kontrol et\" ile tekrar dene."),
        new(Unexpected, "Beklenmedik hata", "Ayrıntılar crash.txt dosyasındadır (günlük klasöründe). Dosyayı bildirirsen bakarım."),
    ];

    public static ErrorInfo? Find(string code) => All.FirstOrDefault(e => e.Code == code);

    /// <summary>"[FX-2001] " in front of a message.</summary>
    public static string Tag(string code) => $"[{code}] ";
}
