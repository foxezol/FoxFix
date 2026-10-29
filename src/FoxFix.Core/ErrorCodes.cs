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
        new(AdminRequired, "Yönetici yetkisi gereksinimi", "Uygulama kısayoluna veya yürütülebilir dosyaya sağ tıklayarak \"Yönetici olarak çalıştır\" seçeneğini kullanınız."),
        new(AdminDenied, "Yönetici izni reddedildi", "Kullanıcı Hesabı Denetimi (UAC) uyarısına \"Evet\" yanıtı verilmelidir. Sistemde hiçbir değişiklik yapılmamıştır; işlemi dilediğiniz zaman tekrarlayabilirsiniz."),
        new(LinkFailed, "Dış bağlantı açılamadı", "Sistemde varsayılan bir web tarayıcısının ayarlandığından emin olunuz. İlgili bağlantıyı tarayıcınızın adres çubuğuna manuel olarak kopyalayabilirsiniz."),
        new(ClipboardFailed, "Panoya kopyalama başarısız", "İşletim sisteminin panosu (Clipboard) başka bir uygulama tarafından kullanılıyor olabilir. Kısa bir süre sonra işlemi tekrarlayınız."),
        new(JournalUnreadable, "Geri alma kayıtları okunamadı", "Log dosyası hasar görmüş olabilir. Uygulama, mevcut kayıtlar olmadan başlatılacaktır; önceki değişiklikler \"Rapor ve Geçmiş\" listesinde görüntülenemeyecektir."),
        new(DpiFilesBroken, "Dosya bütünlüğü doğrulanamadı", "Güvenlik yazılımları (Antivirüs vb.) ilgili çekirdek dosyayı silmiş veya değiştirmiş olabilir. FoxFix dizinini tarama istisnalarına ekleyiniz ve \"DPI Sıfırlama\" işleminin ardından tekrar deneyiniz."),
        new(DpiStartFailed, "FoxDPI modülü başlatılamadı", "Uygulamanın yönetici haklarıyla çalıştığından ve antivirüs tarafından engellenmediğinden emin olunuz. Detaylar satırındaki hata mesajı sorunun teknik kaynağını belirtecektir."),
        new(DpiNotRunning, "FoxDPI işlemi sonlandırıldı", "Antivirüs müdahalesi veya arka planda çalışan başka bir DPI aracı çakışmaya neden oluyor olabilir. DPI Sıfırlama işlemini gerçekleştirip tekrar deneyiniz."),
        new(DpiStoppedUnexpectedly, "FoxDPI beklenmeyen şekilde durduruldu", "FoxDPI modülünü yeniden başlatınız. Sorun tekrarlanırsa, bilgisayarınızı yeniden başlatarak DPI Sıfırlama işlemini uygulayınız."),
        new(DpiOtherRunning, "Çakışan DPI aracı tespit edildi", "Arka planda çalışan diğer GoodbyeDPI, Zapret veya WinDivert işlemlerini Görev Yöneticisi'nden sonlandırınız veya DPI Sıfırlama aracıyla sistemden temizleyiniz."),
        new(DpiAlreadyRunning, "FoxDPI zaten aktif", "Arka planda açık olan FoxDPI penceresini kapatınız veya sistem hizmetini durdurarak işlemi tekrarlayınız."),
        new(DpiServiceFailed, "Sistem hizmeti kurulamadı/başlatılamadı", "Uygulanan işlemler otomatik olarak geri alınmıştır. Antivirüs yazılımınızı kontrol ediniz veya FoxDPI modülünü bağımsız pencere moduyla çalıştırmayı deneyiniz."),
        new(TrialFileError, "Optimizasyon testi dosya hatasıyla sonlandı", "Ağ sürücüsü (driver) önbellekte takılı kalmış olabilir. Sisteminizi yeniden başlatınız veya DPI Sıfırlama yaparak işlemi tekrarlayınız."),
        new(TrialCouldNotStart, "Optimizasyon testi başlatılamadı", "İşlemin ilk aşaması başlatılamadıysa, sorun genellikle yetki eksikliği veya antivirüs müdahalesidir. Lütfen FX-2002 çözüm adımlarını inceleyiniz."),
        new(StatusUnreadable, "Durum bilgisi alınamadı", "Windows hizmet listesine erişilemedi. Uygulamayı yönetici haklarıyla yeniden başlatınız."),
        new(DpiStopFailed, "FoxDPI işlemi sonlandırılamadı", "Görev Yöneticisi üzerinden goodbyedpi.exe veya ilgili işlemi manuel olarak sonlandırınız ya da DPI Sıfırlama aracını kullanınız."),
        new(NeedsFirstRun, "İlk yapılandırma gereksinimi", "FoxDPI bağımsız olarak çalıştırılmadan önce, sistemin yapılandırılabilmesi için FoxFix.exe'nin en az bir kez çalıştırılması gerekmektedir."),
        new(TaskFailed, "Zamanlanmış görev oluşturulamadı", "Uygulamanın yönetici haklarıyla çalıştığından emin olunuz. Windows Görev Zamanlayıcı (Task Scheduler) hizmetinin aktif olup olmadığını kontrol ediniz."),
        new(TaskNoPath, "Kurulum dizini tespit edilemedi", "FoxFix.exe'yi sabit bir dizinden (ör. kurulum klasöründen) çalıştırınız ve ilgili zamanlama seçeneğini yeniden işaretleyiniz."),
        new(PreviewFailed, "İşlem önizlemesi oluşturulamadı", "Sistemde değişiklik yapılmamıştır. İşlemi tekrarlayınız; sorun devam ederse hata raporunu kaydedip geliştiriciye iletiniz."),
        new(ApplyFailed, "Değişiklikler uygulanırken hata oluştu", "Hata detaylarını inceleyiniz. İşlemlerin yalnızca bir kısmı uygulanmış olabilir; \"Rapor ve geçmiş\" sekmesinden yapılan değişiklikleri geri alınız."),
        new(UndoFailed, "Geri alma işlemi başarısız", "Yönetici haklarıyla yeniden deneyiniz. Gerekirse eski sistem hizmetini veya ağ/DNS ayarlarınızı manuel olarak yapılandırabilirsiniz."),
        new(DnsChangeFailed, "DNS yapılandırması başarısız", "Ağ bağdaştırıcısı adı değişmiş veya bağlantı kesilmiş olabilir. Aktif ağ bağlantınızı kontrol edip tekrar deneyiniz."),
        new(ServiceChangeFailed, "Hizmet yönetimi başarısız", "Sistem hizmeti başka bir işlem tarafından kullanılıyor (kilitlenmiş) olabilir. Bilgisayarınızı yeniden başlatarak işlemi tekrarlayınız."),
        new(StartupChangeFailed, "Başlangıç kaydı değiştirilemedi", "İlgili kayıt defteri (Registry) girdisi korunuyor olabilir. Uygulamayı yönetici yetkileriyle başlatıp yeniden deneyiniz."),
        new(CleanupFailed, "Dosya temizleme işlemi başarısız", "Hedef dosyalar halihazırda başka bir sistem işlemi tarafından kullanılıyor olabilir. İlgili uygulamaları kapatarak tekrar deneyiniz."),
        new(RebootFailed, "Yeniden başlatma komutu işlenemedi", "Bilgisayarınızı manuel olarak yeniden başlatınız. Mevcut bir yeniden başlatma zamanlayıcısını iptal etmek için komut satırına (CMD) shutdown /a yazabilirsiniz."),
        new(ReportBuildFailed, "Rapor oluşturulamadı", "İşlemi tekrarlayınız. Sorun devam ederse uygulamayı tamamen kapatıp yeniden başlatınız."),
        new(ReportSaveFailed, "Rapor dosyası kaydedilemedi", "Kayıt işlemi için farklı bir dizin seçiniz veya dosya adının işletim sistemi standartlarında (geçerli karakterlerle) yazıldığından emin olunuz."),
        new(SpeedFailed, "Hız testi başarısız", "İnternet bağlantınızın aktif olduğunu ve Cloudflare sunucularına erişim engeli bulunmadığını doğrulayıp tekrar deneyiniz."),
        new(CheckFailed, "Alt test işlemi sırasında hata", "Bu durum, devam eden diğer teşhis süreçlerini etkilemez. \"Yeniden kontrol et\" butonuyla taramayı tekrarlayınız."),
        new(Unexpected, "Beklenmeyen kritik hata", "Hata detayları log dizinindeki crash.txt dosyasına kaydedilmiştir. Sorunun tespiti için bu dosyayı Discord üzerinden bana iletebilirsiniz."),
    ];

    public static ErrorInfo? Find(string code) => All.FirstOrDefault(e => e.Code == code);

    /// <summary>"[FX-2001] " in front of a message.</summary>
    public static string Tag(string code) => $"[{code}] ";
}
