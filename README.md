# FoxFix

FoxFix, Windows işletim sistemlerinde internet, DNS ve DPI (Derin Paket İncelemesi) kaynaklı bağlantı sorunlarını teşhis eden ve onaran kapsamlı bir ağ asistanıdır. Tamamen Türkçe bir arayüze sahiptir. Kullanıcı gizliliğini merkeze alır; hiçbir telemetri verisi toplamaz ve dış sunuculara veri aktarmaz.

> **Durum: Deneysel sürüm.** Gömülü DPI atlatma modülü (FoxDPI) test edilmiştir ve sorunsuz çalışmaktadır. Uygulama içindeki diğer tüm özellikler (sistem optimizasyonu, onarım, FoxDPI'ı hizmet olarak kurma, Windows açılınca başlatma, DPI Sıfırlama, otomatik yeniden başlatma, uygulamayı kaldırma, tema ve renk ayarları, DNS'i 1.1.1.1 yapma düğmesi, Windows ayarları sayfası ve kurulum paketi) henüz gerçek bilgisayarlarda yeterince test edilmemiştir ve **deneysel** kabul edilmelidir. Sistemde kritik bir değişiklik yapmadan önce sunulan önizlemeyi dikkatlice okumanız ve olası sorunlarda "Geri Al" özelliğini kullanmanız önerilir. Güncelleme denetimi ve uygulama içi güncelleme gerçek bir bilgisayarda denenmiştir (0.10.1'den 0.10.2'ye) ve çalışmaktadır. Bir sorunla karşılaşırsanız [Hata Kodları](docs/HATA-KODLARI.md) tablosuna bakabilir veya aşağıdaki Discord bağlantısından bildirebilirsiniz.

## Açık Kaynak

FoxFix'in bütün kaynak kodu bu depoda, [MIT lisansıyla](LICENSE) yayımlanmıştır. Yönetici izniyle çalışan bir program olduğu için körü körüne güvenmeniz gerekmez:

- Kodu inceleyebilirsiniz. Sistemde değişiklik yapan her işlem `src/FoxFix.Core` altında, önizleme, onay ve Geri Al kaydıyla birlikte yer alır.
- Kendi bilgisayarınızda derleyip kendi `exe` dosyanızı üretebilirsiniz (aşağıdaki "Geliştiriciler İçin Derleme" bölümüne bakın).
- Uygulamanın içinde taşıdığı GoodbyeDPI ve WinDivert dosyaları SHA-256 özetleriyle sabitlenmiştir. Özetler `src/FoxFix.Core/GoodbyeDpi/Files.cs` dosyasındadır, dosyalar `src/FoxFix.App/Assets/GoodbyeDpi` klasöründedir; özetleri kendiniz hesaplayıp karşılaştırabilirsiniz. Özet tutmayan dosya çalıştırılmaz.
- Ağa çıkan işlemler şunlarla sınırlıdır: bağlantı kontrolü (açılışta ve "Yeniden kontrol et" ile ping, DNS ve HTTPS denemesi), erişim testi ve ayar denemesi (yazdığınız siteler), hız testi (siz başlatınca, Cloudflare) ve güncelleme denetimi (GitHub'a, yalnızca en son sürüm numarasını soran tek bir istek; hiçbir veri göndermez ve Ayarlar sayfasından kapatılabilir; yeni sürüm yalnızca "Şimdi güncelle" dediğinizde GitHub'dan indirilir). Telemetri ya da istatistik gönderen bir kod yoktur.

## Altyapı ve Teşekkür

FoxFix, çekirdek bir DPI atlatma aracı değildir. Arka planda [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) ve onun [Türkiye sürümü](https://github.com/cagritaskn/GoodbyeDPI-Turkey) (0.2.3rc3) projesini temel alır. FoxFix bu aracı entegre bir arayüzle sunar: ağınızda hangi ayarın çalıştığını dener, seçilen ayarı çalıştırır ve yapılan her değişikliği geri alınabilir tutar. Asıl teknik başarı bu açık kaynaklı projelerin geliştiricilerine aittir.

Windows ayarları sayfasındaki ayar listesi ve Türkçe açıklamaları, Raphire'nin açık kaynaklı [Win11Debloat](https://github.com/Raphire/Win11Debloat) projesinden (MIT) alınmıştır. FoxFix o betiği çalıştırmaz; yalnızca düz kayıt defteri değişikliklerini alır ve kendi koduyla uygular, böylece her ayar önizlenir, kaydedilir ve "Geri Al" ile geri alınabilir.

Proje, karmaşık ağ araçlarının son kullanıcı için basitleştirilmesi amacıyla Claude Code ve "vibe coding" yaklaşımıyla foxezol tarafından geliştirilmiştir.

Made by foxezol, Claude Code

## Temel Özellikler

- **Bağlantı Kontrolü:** Ağ adaptörü, IP yapılandırması, ping, DNS ve HTTPS protokollerini sırasıyla test ederek ağdaki sorunları tespit eder.
- **DNS ve DPI:** Hazır GoodbyeDPI ayarlarını ağınızda dener (her ayar için google.com ping değeri de gösterilir) ve çalışanı bulur. Sistem hizmeti (Service) olarak veya bağımsız bir pencere üzerinden çalıştırılabilir. Windows açılınca otomatik başlatılabilir. Bazı bağlantılarda FoxDPI'ın çalışması için gereken "DNS'i 1.1.1.1 ve 1.0.0.1 yap" düğmesi de bu sayfadadır; önizleme gösterir ve "Geri Al" ile geri alınabilir.
- **Erişim Testi:** Belirttiğiniz web sitelerinin erişilebilirliğini güvenli bağlantı üzerinden test eder. FoxDPI açıkken ve kapalıyken çalıştırıp karşılaştırma yapabilirsiniz.
- **Hız Testi:** İndirme, yükleme, ping ve titreşimi ölçer. Sonucu görsel olarak panoya kopyalayıp paylaşabilirsiniz.
- **Anlaşılır Hatalar:** Bir şey ters giderse mesajın başında `[FX-xxxx]` biçiminde bir kod bulunur. Her kodun anlamı ve çözümü [docs/HATA-KODLARI.md](docs/HATA-KODLARI.md) tablosundadır.
- **Windows Ayarları:** Win11Debloat'tan alınan, kayıt defterine dayanan yaklaşık 80 ayar: telemetri ve öneriler, yapay zeka (Copilot, Recall...), animasyonlar ve saydamlık, oyun kaydı, Başlat ve arama, görev çubuğu, Dosya Gezgini, Windows Update. Her ayarın yanında ne işe yaradığı, kabaca bir etki tahmini (ölçüm değildir) ve önerilenler işaretlidir. Ayar arama, katlanır listeler, "Önerilen ayarları seç" ve "Windows varsayılanına döndür" düğmeleri; uygulanmış her ayarın yanında da tek tek "Geri aç" düğmesi vardır. Sayfa, ayarlara dokunmadan önce şu anki Windows ayarlarınızı kaydetmenizi ister; kayıtlar buradan geri yüklenebilir. Hiçbiri siz onaylamadan değişmez. Uygulama kaldırma (Xbox, Edge vb.) ve geri alınamayan ayarlar bilerek dışarıda bırakılmıştır.
- **Sistem Hızlandırma:** Başlangıç uygulamalarını yönetmenizi ve gereksiz geçici dosyaları temizlemenizi sağlar. Windows ayarlarıyla birlikte "Hızlandırma ve ayarlar" sayfasındadır.
- **DPI Sıfırlama:** Eski GoodbyeDPI, WinDivert ve Zapret kalıntılarını ve manuel DNS girişlerini temizleyerek ağ ayarlarını Windows varsayılanlarına döndürür.
- **Güncelleme Denetimi:** FoxFix her açıldığında arka planda yeni bir sürüm olup olmadığına bakar; yalnızca yeni sürüm varsa bir pencere ile haber verir. Pencerede "Şimdi güncelle" dediğinizde yeni sürüm GitHub'dan indirilir ve GitHub'ın o dosya için yayımladığı SHA-256 özetiyle doğrulanır (uyuşmazsa hiçbir şey çalıştırılmaz); ardından FoxFix kapanır, güncellenir ve yeniden açılır. Verileriniz ve ayarlarınız korunur. Güncellemeyi siz başlatmadıkça hiçbir şey indirilmez veya kurulmaz. Ayarlar sayfasından kapatılabilir; aynı sayfadaki düğmeyle elle de denetlenebilir.
- **Ayarlar ve Görünüm:** Açık, karanlık veya Windows'a uyan tema; hazır listeden ya da `#RRGGBB` olarak yazılan vurgu rengi; logonun ve pencere simgesinin bu renge boyanması. Uygulamayı kaldırma düğmesi de bu sayfadadır.
- **Güvenli Raporlama:** Sorun tespiti için paylaşılabilir bir rapor oluşturur. Kullanıcı adı, bilgisayar adı, MAC ve IP adresleri gibi hassas veriler varsayılan olarak maskelenir. Teşhis için yalnızca `1.1.1.1` gibi bilinen genel DNS adresleri ve `127.0.0.1` görünür bırakılır. İstendiğinde maskeleme devre dışı bırakılabilir.

<img width="986" height="773" alt="image" src="https://github.com/user-attachments/assets/5967ae25-b091-428d-9205-f8cca651d928" />

**Önemli Not:** Sistemde yapılacak tüm değişiklikler öncesinde ekranda listelenir ve onayınız olmadan hiçbir işlem uygulanmaz. Yapılan değişiklikler kaydedilir ve "Geri Al" özelliği ile geri döndürülebilir.

## İndirme ve Kurulum

[Releases](../../releases) sayfasında iki dağıtım seçeneği bulunmaktadır:

| Paket | Açıklama |
|---|---|
| **Standart Kurulum** (`FoxFixSetup.exe`) | Klasik kurulum sihirbazıdır (varsayılan hedef: `Program Files\FoxFix`, klasörü siz seçersiniz). Kurulum sırasında "Verileri kurulum klasöründe tut" seçeneği işaretlenirse veriler kurulum klasöründe saklanır. İşaretlenmezse veriler `%LocalAppData%\FoxFix` ve `%ProgramData%\FoxFix` dizinlerine kaydedilir. |
| **Taşınabilir Sürüm** (`FoxFix-…-tasinabilir.zip`) | Kurulum gerektirmez. Arşivi bir klasöre çıkartıp doğrudan çalıştırabilirsiniz. İçindeki `FoxFix.portable` dosyası, tüm verilerin (günlükler, geri alma kayıtları, DPI dosyaları) programın bulunduğu dizindeki `Veri` klasöründe tutulmasını sağlar. Klasörü sildiğinizde sistemde iz kalmaz. |

**Güncelleme:** Uygulama içinden "Şimdi güncelle" düğmesiyle yapılır (yeni sürüm olduğunda açılan pencerede ya da Ayarlar sayfasında). Elle güncellemek isterseniz: kurulumlu sürümde yeni `FoxFixSetup.exe` dosyasını eskisinin üstüne çalıştırmanız yeterlidir; DPI dosyalarınız, ayarlarınız ve geri alma kayıtlarınız korunur. Taşınabilir sürümde yeni ZIP'i eski klasörün üstüne çıkartın (`Veri` klasörü ZIP'te olmadığı için silinmez). Güncellemeden önce FoxFix ve FoxDPI'ı kapatın.

**Uyarı:** Uygulama dijital olarak imzalanmadığı için Windows SmartScreen uyarısı verebilir. Devam etmek için "Ek bilgi" ardından "Yine de çalıştır" seçeneğini kullanabilirsiniz. Güvenmiyorsanız projeyi kaynak kodundan kendiniz derleyebilirsiniz.

## Kullanım

1. `FoxFix.exe` ve `FoxDPI.exe` dosyalarının aynı dizinde bulunduğundan emin olun.
2. İlk kullanımda mutlaka `FoxFix.exe` dosyasını çalıştırın. FoxDPI, ana uygulama bir kez açılmadan çalışmaz.
3. Sonrasında kısıtlamaları aşmak için yalnızca `FoxDPI.exe` dosyasını çalıştırmanız yeterlidir. FoxDPI penceresi açık kaldığı sürece DPI atlatma etkindir, pencere kapatıldığında sona erer.
4. Otomatik başlatma için "FoxDPI'yı Windows açılınca başlat" seçeneğini kullanabilirsiniz. Bu işlem bir sistem hizmeti kurmaz, standart bir zamanlanmış görev (`schtasks`) ekler.

**Oyunlar ve hile önleme sistemleri:** FoxDPI, ağ paketlerini işlemek için WinDivert sürücüsünü kullanır. Bazı oyunların hile önleme sistemleri (EasyAntiCheat, Vanguard, FACEIT gibi) bu sürücüyü engelleyebilir veya oyunun açılmasını reddedebilir. Oyunu başlatmadan önce FoxDPI penceresini kapatmanız önerilir. Pencere kapatıldığında FoxFix, kendi yüklediği sürücüyü bellekten otomatik olarak kaldırır ve bilgisayarı yeniden başlatmanız gerekmez. Oyun sırasında FoxDPI'ın açık kalması hile önleme sistemi tarafından engellenebilir; bu durum oyuna bağlıdır ve garanti edilemez. Şüphe duyduğunuz durumlarda ilgili oyunun destek sayfasını kontrol ediniz.

**Gereksinimler:** Uygulamalar yönetici hakları gerektirir. Yalnızca Windows 10 ve 11 (64 bit) desteklenir. Herhangi bir .NET çalışma zamanı kurulumu gerekmez.

## Geliştiriciler İçin Derleme

Projeyi derlemek için .NET 10 SDK gerekir.

```
dotnet build FoxFix.sln
dotnet test FoxFix.sln
powershell -File tools\paketle.ps1
```

Son komut, `dist` klasöründe ZIP paketini oluşturur. Sistemde [Inno Setup 6](https://jrsoftware.org/isinfo.php) kuruluysa `FoxFixSetup.exe` kurulum dosyası da derlenir.

## Manuel Test Gereksinimleri

Birim testleri sistem komutlarını taklit eder. Aşağıdakilerin güvenilirliği için gerçek veya sanal bir Windows ortamında elle test edilmesi gerekir:

- Hizmet (Service) kurma/kaldırma ve DPI Sıfırlama (sanal makine anlık görüntüsü ile test edilmesi önerilir).
- Uygulamayı kaldırma ("Bilgi" penceresindeki "FoxFix'i kaldır" düğmesi) ve işlemler sonrası otomatik yeniden başlatma.
- Hile önleme sistemleriyle (EasyAntiCheat, Vanguard, FACEIT) uyumluluk: oyun bazında değişir, test edilmemiştir.
- DNS düğmesi (1.1.1.1 / 1.0.0.1): ayarın uygulanması ve "Geri Al" ile eski değerlerin geri gelmesi.
- Tema ve vurgu rengi: farklı renklerde okunabilirlik; pencere ve görev çubuğu simgesinin renklenmesi.
- "Şimdi güncelle": bir kopyada denenmiş ve çalışmıştır; kurulumlu sürümde sessiz kurulumun (`FoxFixSetup.exe /VERYSILENT /YENIDEN=1`) ve ZIP sürümde iki exe dosyasının değiştirilmesinin ikisinin de ayrı ayrı, yönetici izniyle çalışırken UAC çıkmadan denenmesi gerekir.
- Windows ayarları: kayıt defteri değişikliklerinin gerçek bir Windows'ta uygulanması ve "Geri Al" ile eski değerlerin (ve FoxFix'in oluşturduğu anahtarların silinmesinin) geri gelmesi. Sanal makine anlık görüntüsüyle denenmesi önerilir.
- Windows ayarları: "Şu anki ayarları kaydet", ayarları uygulama ve kayıttan "Geri yükle"; "Windows varsayılanına döndür" düğmesinin gerçek Windows varsayılanlarını verdiği.
- Güncelleme: eski sürümün üstüne yeni kurulum ya da ZIP çıkartma (hizmet kuruluyken de).
- Zamanlanmış görev ile Windows başlangıç entegrasyonu: `schtasks` ile oluşturulan görevin gerçekten oturum açılışında çalıştığı.
- Kurulum paketi (`FoxFixSetup.exe`): kurulum, klasör seçimi, taşınabilir veri seçeneği ve kaldırma.

## Lisans ve Telif Hakları

FoxFix MIT lisanslıdır. FoxDPI, GoodbyeDPI'ın Türkiye sürümünü (0.2.3rc3) ve onun kullandığı WinDivert sürücüsünü uygulamanın içinde taşır; Windows ayarları listesi Win11Debloat'tan alınmıştır. Her bileşen kendi lisansıyla dağıtılır:

- `LICENSE-goodbyedpi.txt`
- `LICENSE-windivert.txt`
- `LICENSE-win11debloat.txt` (Windows ayarları listesi)

Bu dosyalar kaynak kodda `src/FoxFix.App/Assets/GoodbyeDpi` ve `src/FoxFix.App/Assets/Licenses` dizinlerinde, derlenmiş paketlerde `Licenses` klasöründe yer alır.

## Geri Bildirim ve Destek

[Discord](https://discord.com/users/381533632852131841)
