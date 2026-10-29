# Hata kodları

FoxFix bir şey ters gittiğinde mesajın başına `[FX-xxxx]` biçiminde bir kod koyar. Bu sayfa her kodun ne demek olduğunu ve ne yapılabileceğini anlatır.

Kodun ilk hanesi alanı gösterir: 1 uygulama, 2 FoxDPI, 3 zamanlanmış görev, 4 sistemde yapılan değişiklikler, 5 raporlar, 6 ölçümler, 9 beklenmedik hatalar.

| Kod | Anlamı | Ne yapılabilir |
|---|---|---|
| `FX-1001` | Yönetici izni gerekli | Uygulamaya sağ tıklayıp "Yönetici olarak çalıştır" de. |
| `FX-1002` | Yönetici izni verilmedi | Windows sorusuna "Evet" de. Hiçbir şey değişmedi, istediğin zaman yeniden dene. |
| `FX-1003` | Bağlantı açılamadı | Varsayılan tarayıcının ayarlı olduğundan emin ol, adresi elle açabilirsin. |
| `FX-1004` | Panoya kopyalanamadı | Pano başka bir program tarafından tutuluyor olabilir. Birkaç saniye sonra yeniden dene. |
| `FX-1005` | Geri alma kayıtları okunamadı | Kayıt dosyası bozulmuş olabilir. FoxFix o kayıtlar olmadan açılır; eski değişiklikler Geri Al listesinde görünmez. |
| `FX-2001` | Dosya doğrulaması tutmadı | Antivirüs dosyayı silmiş ya da değiştirmiş olabilir. FoxFix klasörünü antivirüs istisnasına ekle ve DPI Sıfırlama ile yeniden dene. |
| `FX-2002` | FoxDPI başlatılamadı | Yönetici izniyle çalıştığından emin ol, antivirüsü kontrol et. Ayrıntı satırındaki Windows mesajı nedeni söyler. |
| `FX-2003` | FoxDPI başlayıp hemen kapandı | Antivirüs engelliyor olabilir ya da başka bir DPI aracı çalışıyordur. DPI Sıfırlama yapıp yeniden dene. |
| `FX-2004` | FoxDPI beklenmedik şekilde durdu | FoxDPI'yı yeniden başlat. Sürekli oluyorsa bilgisayarı yeniden başlatıp DPI Sıfırlama yap. |
| `FX-2005` | Başka bir DPI aracı çalışıyor | Diğer GoodbyeDPI/Zapret/WinDivert araçlarını kapat ya da DPI Sıfırlama ile kaldır. |
| `FX-2006` | FoxDPI zaten çalışıyor | FoxDPI penceresini kapat ya da hizmeti durdur, sonra yeniden dene. |
| `FX-2007` | Hizmet kurulamadı ya da başlamadı | Yapılan her şey geri alındı. Antivirüsü kontrol et, FoxDPI penceresiyle çalıştırmayı dene. |
| `FX-2008` | Deneme bir dosya hatasıyla durdu | Sürücü bellekte kalmış olabilir. Bilgisayarı yeniden başlat ya da DPI Sıfırlama yap, sonra yeniden dene. |
| `FX-2009` | Deneme başlatılamadı | İlk ayar bile başlamadıysa neden hepsinde aynıdır: antivirüs ya da yönetici izni. FX-2002 çözümlerine bak. |
| `FX-2010` | Durum okunamadı | Windows hizmet listesi okunamadı. Uygulamayı yönetici olarak yeniden aç. |
| `FX-2011` | FoxDPI durdurulamadı | Görev Yöneticisi'nden goodbyedpi.exe işlemini bitir ya da DPI Sıfırlama yap. |
| `FX-2012` | İlk çalıştırma gerekli | Önce FoxFix.exe'yi bir kez aç, sonra FoxDPI.exe kullanılabilir. |
| `FX-3001` | Zamanlanmış görev ayarlanamadı | Yönetici izniyle çalıştığından emin ol. Görev Zamanlayıcı hizmeti kapalı olabilir. |
| `FX-3002` | FoxFix'in yeri bulunamadı | FoxFix.exe'yi sabit bir klasörden aç (örneğin kurulum klasörü) ve kutuyu yeniden işaretle. |
| `FX-4001` | Önizleme hazırlanamadı | Hiçbir şey değişmedi. Yeniden dene; sürerse raporu kaydedip gönder. |
| `FX-4002` | Uygulanırken hata çıktı | Ayrıntı satırına bak. Bir kısmı uygulanmış olabilir: Rapor ve geçmiş sayfasından Geri Al. |
| `FX-4003` | Geri alınamadı | Yönetici izniyle yeniden dene. Eski hizmet ya da DNS ayarını elle de geri kurabilirsin. |
| `FX-4004` | DNS ayarı değiştirilemedi | Bağlantı adı değişmiş ya da bağlantı kopmuş olabilir. Bağlantıyı kontrol edip yeniden dene. |
| `FX-4005` | Hizmet durdurulamadı ya da geri kurulamadı | Hizmet başka bir program tarafından kullanılıyor olabilir. Bilgisayarı yeniden başlatıp dene. |
| `FX-4006` | Açılış ayarı değiştirilemedi | Kayıt defteri girdisi korumalı olabilir. Yönetici izniyle yeniden dene. |
| `FX-4007` | Dosyalar temizlenemedi | Dosyalar kullanımda olabilir. İlgili programları kapatıp yeniden dene. |
| `FX-4008` | Yeniden başlatma ayarlanamadı ya da iptal edilemedi | Bilgisayarı kendin yeniden başlat. İptal gerekiyorsa komut satırında shutdown /a yaz. |
| `FX-5001` | Rapor hazırlanamadı | Yeniden dene. Sürerse uygulamayı kapatıp aç. |
| `FX-5002` | Rapor kaydedilemedi | Başka bir klasör seç ya da dosya adının geçerli olduğundan emin ol. |
| `FX-6001` | Hız ölçülemedi | İnternet bağlantını ve Cloudflare'e erişimini kontrol et, yeniden dene. |
| `FX-6002` | Bir kontrol çalışırken hata çıktı | Diğer kontroller etkilenmez. "Yeniden kontrol et" ile tekrar dene. |
| `FX-9001` | Beklenmedik hata | Ayrıntılar crash.txt dosyasındadır (günlük klasöründe). Dosyayı bildirirsen bakarım. |

Çözüm bulamazsan hata kodunu ve mesajın ayrıntı satırını [Discord](https://discord.com/users/381533632852131841) üzerinden bildir. "Rapor ve geçmiş" sayfasından alınan rapor da işe yarar.
