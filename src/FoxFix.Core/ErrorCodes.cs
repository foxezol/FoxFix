namespace FoxFix.Core;

/// <summary>One thing that can go wrong, in words: its code, what it means and what the person can do.</summary>
public sealed record ErrorInfo(string Code, string Title, string Remedy);

/// <summary>
/// The error codes. Every message that says "[FX-xxxx]" is listed here and in docs/HATA-KODLARI.md, so no error is just
/// an unexplained sentence. 1xxx the app, 2xxx FoxDPI, 3xxx the scheduled task, 4xxx changes to the system, 5xxx reports,
/// 6xxx measurements, 7xxx sound and camera checks, 9xxx the unexpected.
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
    public const string TweakFailed = "FX-4009";
    public const string BackupSaveFailed = "FX-4010";
    public const string BackupRestoreFailed = "FX-4011";
    public const string BackupUnreadable = "FX-4012";
    public const string ReportBuildFailed = "FX-5001";
    public const string ReportSaveFailed = "FX-5002";
    public const string SpeedFailed = "FX-6001";
    public const string CheckFailed = "FX-6002";
    public const string UpdateCheckFailed = "FX-6003";
    public const string UpdateInstallFailed = "FX-6004";
    public const string DeviceCheckFailed = "FX-7001";
    public const string OverlayMeasureFailed = "FX-8001";
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
        new(TweakFailed, "Windows ayarı uygulanamadı", "Yönetici haklarıyla çalıştığınızdan emin olunuz. İlgili kayıt defteri anahtarı kurumsal bir ilke (grup ilkesi) veya güvenlik yazılımı tarafından korunuyor olabilir. Değişiklik yarım kalırsa FoxFix eski değerleri geri yazar; yazamazsa mesajda belirtir ve \"Rapor ve geçmiş\" sayfasındaki Geri Al ile denenebilir."),
        new(BackupSaveFailed, "Windows ayarları kaydedilemedi", "Kayıtlar FoxFix veri klasöründeki ayar-yedekleri klasörüne yazılır. Diskte yer olduğundan ve klasöre yazma iznizin bulunduğundan emin olunuz; güvenlik yazılımı klasörü engelliyor olabilir."),
        new(BackupRestoreFailed, "Kayıtlı Windows ayarları geri yüklenemedi", "Yönetici haklarıyla çalıştığınızdan emin olunuz. Geri yükleme yarım kalırsa FoxFix önceki değerleri geri yazar; yazamazsa mesajda belirtir ve \"Rapor ve geçmiş\" sayfasındaki Geri Al ile denenebilir."),
        new(BackupUnreadable, "Kayıtlı Windows ayarı okunamadı", "Kayıt dosyası silinmiş, değiştirilmiş veya bozulmuş olabilir. Dosyayı ayar-yedekleri klasöründen silip ayarları yeniden kaydedebilirsiniz."),
        new(ReportBuildFailed, "Rapor oluşturulamadı", "İşlemi tekrarlayınız. Sorun devam ederse uygulamayı tamamen kapatıp yeniden başlatınız."),
        new(ReportSaveFailed, "Rapor dosyası kaydedilemedi", "Kayıt işlemi için farklı bir dizin seçiniz veya dosya adının işletim sistemi standartlarında (geçerli karakterlerle) yazıldığından emin olunuz."),
        new(SpeedFailed, "Hız testi başarısız", "İnternet bağlantınızın aktif olduğunu ve Cloudflare sunucularına erişim engeli bulunmadığını doğrulayıp tekrar deneyiniz."),
        new(CheckFailed, "Alt test işlemi sırasında hata", "Bu durum, devam eden diğer teşhis süreçlerini etkilemez. \"Yeniden kontrol et\" butonuyla taramayı tekrarlayınız."),
        new(UpdateCheckFailed, "Güncelleme denetlenemedi", "İnternet bağlantınızı kontrol ediniz. Denetim yalnızca GitHub'a (api.github.com) bağlanır; erişim engelliyse FoxDPI açıkken tekrar deneyiniz. Yeni sürümleri depodaki Releases sayfasından da görebilirsiniz."),
        new(UpdateInstallFailed, "Güncelleme indirilemedi veya doğrulanamadı", "İnternet bağlantınızı kontrol edip tekrar deneyiniz. İndirilen dosyanın SHA-256 özeti yayımlananla uyuşmazsa hiçbir şey çalıştırılmaz. Sorun sürerse yeni sürümü Releases sayfasından elle indirip kurabilirsiniz."),
        new(DeviceCheckFailed, "Ses veya kamera aygıtı kontrol edilemedi", "Windows aygıtla ilgili soruya yanıt vermedi. Hiçbir şey değiştirilmedi. Aygıtı yeniden takınız, onu kullanıyor olabilecek uygulamaları kapatınız ve kontrolü tekrar deneyiniz."),
        new(OverlayMeasureFailed, "Oyun göstergesinde kare sayısı ölçülemedi", "Windows'un görüntü olayları dinlenemedi; FPS ve kare süresi N/A görünür, işlemci ve ekran kartı değerleri çalışır. FoxFix'i yönetici olarak açtığınızdan emin olun, güvenlik yazılımınızın engellemediğini kontrol edin ve göstergeyi kapatıp açın. Hiçbir şey değiştirilmedi."),
        new(Unexpected, "Beklenmeyen kritik hata", "Hata detayları log dizinindeki crash.txt dosyasına kaydedilmiştir. Sorunun tespiti için bu dosyayı Discord üzerinden bana iletebilirsiniz."),
    ];

    /// <summary>The same table in English (docs/ERROR-CODES.md); the codes and their order are the same.</summary>
    public static IReadOnlyList<ErrorInfo> English { get; } =
    [
        new(AdminRequired, "Administrator rights required", "Right-click the shortcut or the program file and choose \"Run as administrator\"."),
        new(AdminDenied, "Administrator permission refused", "Answer \"Yes\" to the User Account Control (UAC) prompt. Nothing was changed on the system; you can try again any time."),
        new(LinkFailed, "An external link could not be opened", "Make sure a default web browser is set. You can also copy the link into your browser's address bar yourself."),
        new(ClipboardFailed, "Copying to the clipboard failed", "Another app may be using the clipboard. Try again in a moment."),
        new(JournalUnreadable, "The undo records could not be read", "The record file may be damaged. The app starts without them; earlier changes will not be listed on the \"Report and history\" page."),
        new(DpiFilesBroken, "File integrity could not be verified", "Security software (antivirus etc.) may have deleted or changed a core file. Add the FoxFix folder to its exclusions, run \"DPI reset\" and try again."),
        new(DpiStartFailed, "The FoxDPI module could not start", "Make sure the app runs with administrator rights and is not blocked by antivirus. The error text in the message names the technical cause."),
        new(DpiNotRunning, "The FoxDPI process ended", "Antivirus or another DPI tool running in the background may conflict with it. Run DPI reset and try again."),
        new(DpiStoppedUnexpectedly, "FoxDPI stopped unexpectedly", "Start FoxDPI again. If it happens again, restart the computer and run DPI reset."),
        new(DpiOtherRunning, "A conflicting DPI tool was found", "End other GoodbyeDPI, Zapret or WinDivert processes in Task Manager, or remove them with DPI reset."),
        new(DpiAlreadyRunning, "FoxDPI is already running", "Close the FoxDPI window running in the background or stop the system service, then try again."),
        new(DpiServiceFailed, "The system service could not be installed or started", "What was done has been undone automatically. Check your antivirus, or run FoxDPI in its own window instead."),
        new(TrialFileError, "The trial stopped with a file error", "The network driver may be stuck in memory. Restart the computer or run DPI reset and try again."),
        new(TrialCouldNotStart, "The trial could not start", "If the first step cannot start, the cause is usually missing rights or antivirus. See the steps for FX-2002."),
        new(StatusUnreadable, "The status could not be read", "The Windows service list could not be read. Restart the app with administrator rights."),
        new(DpiStopFailed, "FoxDPI could not be stopped", "End goodbyedpi.exe or the related process in Task Manager, or use DPI reset."),
        new(NeedsFirstRun, "First set-up needed", "Before FoxDPI can run on its own, FoxFix.exe has to be opened at least once to set things up."),
        new(TaskFailed, "The scheduled task could not be created", "Make sure the app runs with administrator rights and that the Windows Task Scheduler service is running."),
        new(TaskNoPath, "The program folder could not be found", "Run FoxFix.exe from a fixed folder (for example the install folder) and tick the option again."),
        new(PreviewFailed, "The preview could not be prepared", "Nothing was changed on the system. Try again; if it keeps happening, save the report and send it to the developer."),
        new(ApplyFailed, "An error occurred while applying changes", "Read the error details. Only part of the changes may have been made; undo them on the \"Report and history\" page."),
        new(UndoFailed, "Undo failed", "Try again with administrator rights. If needed, set the old service or network/DNS settings back by hand."),
        new(DnsChangeFailed, "The DNS setting could not be changed", "The adapter name may have changed or the connection dropped. Check your active connection and try again."),
        new(ServiceChangeFailed, "The service could not be managed", "The service may be in use (locked) by another process. Restart the computer and try again."),
        new(StartupChangeFailed, "The start-up entry could not be changed", "The registry entry may be protected. Start the app with administrator rights and try again."),
        new(CleanupFailed, "File cleanup failed", "The files may be in use by another process. Close the related apps and try again."),
        new(RebootFailed, "The restart command could not be processed", "Restart the computer yourself. To cancel a pending restart, type shutdown /a at the command prompt."),
        new(TweakFailed, "A Windows setting could not be applied", "Make sure you run with administrator rights. The registry key may be protected by a policy (group policy) or security software. If a change stops half way, FoxFix writes the old values back; if it cannot, the message says so and you can try Undo on the \"Report and history\" page."),
        new(BackupSaveFailed, "Windows settings could not be saved", "Records are written to the ayar-yedekleri folder in FoxFix's data folder. Make sure there is disk space and you can write to the folder; security software may be blocking it."),
        new(BackupRestoreFailed, "Saved Windows settings could not be restored", "Make sure you run with administrator rights. If a restore stops half way, FoxFix writes the previous values back; if it cannot, the message says so and you can try Undo on the \"Report and history\" page."),
        new(BackupUnreadable, "A saved Windows setting could not be read", "The record file may have been deleted, changed or damaged. Delete it from the ayar-yedekleri folder and save your settings again."),
        new(ReportBuildFailed, "The report could not be created", "Try again. If it keeps happening, close the app completely and start it again."),
        new(ReportSaveFailed, "The report file could not be saved", "Choose another folder, or make sure the file name uses only valid characters."),
        new(SpeedFailed, "The speed test failed", "Make sure your internet connection works and Cloudflare servers are not blocked, then try again."),
        new(CheckFailed, "A check failed while running", "This does not affect the other checks. Run the scan again with \"Check again\"."),
        new(UpdateCheckFailed, "The update check failed", "Check your internet connection. The check only connects to GitHub (api.github.com); if it is blocked, try again with FoxDPI on. New versions are also listed on the repository's Releases page."),
        new(UpdateInstallFailed, "The update could not be downloaded or verified", "Check your internet connection and try again. If the SHA-256 hash of the downloaded file does not match the published one, nothing is run. If it keeps happening, download and install the new version from the Releases page."),
        new(DeviceCheckFailed, "A sound or camera device could not be checked", "Windows did not answer the question about the device. Nothing was changed. Reconnect the device, close apps that may be using it, and try the check again."),
        new(OverlayMeasureFailed, "The overlay could not count frames", "Windows' display events could not be listened to; FPS and frame time show N/A while the processor and graphics card numbers keep working. Make sure FoxFix runs as administrator, check that security software does not block it, and turn the overlay off and on. Nothing was changed."),
        new(Unexpected, "Unexpected critical error", "The details were written to crash.txt in the log folder. You can send this file to me on Discord to find the cause."),
    ];

    /// <summary>The table in the language the program shows.</summary>
    public static IReadOnlyList<ErrorInfo> Current => Strings.IsEnglish ? English : All;

    public static ErrorInfo? Find(string code) => All.FirstOrDefault(e => e.Code == code);

    /// <summary>"[FX-2001] " in front of a message.</summary>
    public static string Tag(string code) => $"[{code}] ";
}
