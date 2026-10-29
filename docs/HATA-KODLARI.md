# Hata Kodları ve Çözüm Adımları

FoxFix, sistemde beklenmeyen bir durumla karşılaşıldığında hata mesajlarının başına `[FX-xxxx]` formatında bir kod ekler. Bu doküman, ilgili hata kodlarının anlamlarını ve uygulanabilecek çözüm adımlarını açıklamaktadır.

Hata kodunun ilk hanesi, sorunun kaynaklandığı sistem bileşenini belirtir:

- **1:** Uygulama Arayüzü ve Temel İşlemler
- **2:** FoxDPI Modülü ve Ağ Tünelleme
- **3:** Zamanlanmış Görevler (Task Scheduler)
- **4:** Sistem Değişiklikleri ve Konfigürasyon
- **5:** Loglama ve Raporlama Sistemi
- **6:** Ağ Performans Ölçümleri
- **9:** Beklenmeyen Kritik Hatalar (Crash)

| Hata Kodu | Anlamı | Uygulanabilecek Çözüm Adımları |
|---|---|---|
| `FX-1001` | Yönetici yetkisi gereksinimi | Uygulama kısayoluna veya yürütülebilir dosyaya sağ tıklayarak "Yönetici olarak çalıştır" seçeneğini kullanınız. |
| `FX-1002` | Yönetici izni reddedildi | Kullanıcı Hesabı Denetimi (UAC) uyarısına "Evet" yanıtı verilmelidir. Sistemde hiçbir değişiklik yapılmamıştır; işlemi dilediğiniz zaman tekrarlayabilirsiniz. |
| `FX-1003` | Dış bağlantı açılamadı | Sistemde varsayılan bir web tarayıcısının ayarlandığından emin olunuz. İlgili bağlantıyı tarayıcınızın adres çubuğuna manuel olarak kopyalayabilirsiniz. |
| `FX-1004` | Panoya kopyalama başarısız | İşletim sisteminin panosu (Clipboard) başka bir uygulama tarafından kullanılıyor olabilir. Kısa bir süre sonra işlemi tekrarlayınız. |
| `FX-1005` | Geri alma kayıtları okunamadı | Log dosyası hasar görmüş olabilir. Uygulama, mevcut kayıtlar olmadan başlatılacaktır; önceki değişiklikler "Rapor ve Geçmiş" listesinde görüntülenemeyecektir. |
| `FX-2001` | Dosya bütünlüğü doğrulanamadı | Güvenlik yazılımları (Antivirüs vb.) ilgili çekirdek dosyayı silmiş veya değiştirmiş olabilir. FoxFix dizinini tarama istisnalarına ekleyiniz ve "DPI Sıfırlama" işleminin ardından tekrar deneyiniz. |
| `FX-2002` | FoxDPI modülü başlatılamadı | Uygulamanın yönetici haklarıyla çalıştığından ve antivirüs tarafından engellenmediğinden emin olunuz. Detaylar satırındaki hata mesajı sorunun teknik kaynağını belirtecektir. |
| `FX-2003` | FoxDPI işlemi sonlandırıldı | Antivirüs müdahalesi veya arka planda çalışan başka bir DPI aracı çakışmaya neden oluyor olabilir. DPI Sıfırlama işlemini gerçekleştirip tekrar deneyiniz. |
| `FX-2004` | FoxDPI beklenmeyen şekilde durduruldu | FoxDPI modülünü yeniden başlatınız. Sorun tekrarlanırsa, bilgisayarınızı yeniden başlatarak DPI Sıfırlama işlemini uygulayınız. |
| `FX-2005` | Çakışan DPI aracı tespit edildi | Arka planda çalışan diğer GoodbyeDPI, Zapret veya WinDivert işlemlerini Görev Yöneticisi'nden sonlandırınız veya DPI Sıfırlama aracıyla sistemden temizleyiniz. |
| `FX-2006` | FoxDPI zaten aktif | Arka planda açık olan FoxDPI penceresini kapatınız veya sistem hizmetini durdurarak işlemi tekrarlayınız. |
| `FX-2007` | Sistem hizmeti kurulamadı/başlatılamadı | Uygulanan işlemler otomatik olarak geri alınmıştır. Antivirüs yazılımınızı kontrol ediniz veya FoxDPI modülünü bağımsız pencere moduyla çalıştırmayı deneyiniz. |
| `FX-2008` | Optimizasyon testi dosya hatasıyla sonlandı | Ağ sürücüsü (driver) önbellekte takılı kalmış olabilir. Sisteminizi yeniden başlatınız veya DPI Sıfırlama yaparak işlemi tekrarlayınız. |
| `FX-2009` | Optimizasyon testi başlatılamadı | İşlemin ilk aşaması başlatılamadıysa, sorun genellikle yetki eksikliği veya antivirüs müdahalesidir. Lütfen FX-2002 çözüm adımlarını inceleyiniz. |
| `FX-2010` | Durum bilgisi alınamadı | Windows hizmet listesine erişilemedi. Uygulamayı yönetici haklarıyla yeniden başlatınız. |
| `FX-2011` | FoxDPI işlemi sonlandırılamadı | Görev Yöneticisi üzerinden goodbyedpi.exe veya ilgili işlemi manuel olarak sonlandırınız ya da DPI Sıfırlama aracını kullanınız. |
| `FX-2012` | İlk yapılandırma gereksinimi | FoxDPI bağımsız olarak çalıştırılmadan önce, sistemin yapılandırılabilmesi için FoxFix.exe'nin en az bir kez çalıştırılması gerekmektedir. |
| `FX-3001` | Zamanlanmış görev oluşturulamadı | Uygulamanın yönetici haklarıyla çalıştığından emin olunuz. Windows Görev Zamanlayıcı (Task Scheduler) hizmetinin aktif olup olmadığını kontrol ediniz. |
| `FX-3002` | Kurulum dizini tespit edilemedi | FoxFix.exe'yi sabit bir dizinden (ör. kurulum klasöründen) çalıştırınız ve ilgili zamanlama seçeneğini yeniden işaretleyiniz. |
| `FX-4001` | İşlem önizlemesi oluşturulamadı | Sistemde değişiklik yapılmamıştır. İşlemi tekrarlayınız; sorun devam ederse hata raporunu kaydedip geliştiriciye iletiniz. |
| `FX-4002` | Değişiklikler uygulanırken hata oluştu | Hata detaylarını inceleyiniz. İşlemlerin yalnızca bir kısmı uygulanmış olabilir; "Rapor ve geçmiş" sekmesinden yapılan değişiklikleri geri alınız. |
| `FX-4003` | Geri alma işlemi başarısız | Yönetici haklarıyla yeniden deneyiniz. Gerekirse eski sistem hizmetini veya ağ/DNS ayarlarınızı manuel olarak yapılandırabilirsiniz. |
| `FX-4004` | DNS yapılandırması başarısız | Ağ bağdaştırıcısı adı değişmiş veya bağlantı kesilmiş olabilir. Aktif ağ bağlantınızı kontrol edip tekrar deneyiniz. |
| `FX-4005` | Hizmet yönetimi başarısız | Sistem hizmeti başka bir işlem tarafından kullanılıyor (kilitlenmiş) olabilir. Bilgisayarınızı yeniden başlatarak işlemi tekrarlayınız. |
| `FX-4006` | Başlangıç kaydı değiştirilemedi | İlgili kayıt defteri (Registry) girdisi korunuyor olabilir. Uygulamayı yönetici yetkileriyle başlatıp yeniden deneyiniz. |
| `FX-4007` | Dosya temizleme işlemi başarısız | Hedef dosyalar halihazırda başka bir sistem işlemi tarafından kullanılıyor olabilir. İlgili uygulamaları kapatarak tekrar deneyiniz. |
| `FX-4008` | Yeniden başlatma komutu işlenemedi | Bilgisayarınızı manuel olarak yeniden başlatınız. Mevcut bir yeniden başlatma zamanlayıcısını iptal etmek için komut satırına (CMD) shutdown /a yazabilirsiniz. |
| `FX-5001` | Rapor oluşturulamadı | İşlemi tekrarlayınız. Sorun devam ederse uygulamayı tamamen kapatıp yeniden başlatınız. |
| `FX-5002` | Rapor dosyası kaydedilemedi | Kayıt işlemi için farklı bir dizin seçiniz veya dosya adının işletim sistemi standartlarında (geçerli karakterlerle) yazıldığından emin olunuz. |
| `FX-6001` | Hız testi başarısız | İnternet bağlantınızın aktif olduğunu ve Cloudflare sunucularına erişim engeli bulunmadığını doğrulayıp tekrar deneyiniz. |
| `FX-6002` | Alt test işlemi sırasında hata | Bu durum, devam eden diğer teşhis süreçlerini etkilemez. "Yeniden kontrol et" butonuyla taramayı tekrarlayınız. |
| `FX-9001` | Beklenmeyen kritik hata | Hata detayları log dizinindeki crash.txt dosyasına kaydedilmiştir. Sorunun tespiti için bu dosyayı Discord üzerinden bana iletebilirsiniz. |

## Geri Bildirim ve Destek

Öneri, geri bildirim veya çözemediğiniz hatalar için [Discord](https://discord.com/users/381533632852131841) üzerinden `foxezol` kullanıcı adıma doğrudan özel mesaj (DM) gönderebilirsiniz. Hata bildirimi yaparken, uygulamanın "Rapor ve geçmiş" sekmesinden oluşturduğunuz sistem raporunu mesaja eklemeniz destek sürecini hızlandıracaktır.
