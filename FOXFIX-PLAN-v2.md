# FoxFix: Proje Planı

> Bu dosya Claude Code için hazırlanmış tam plandır. 600 satırı bağlamı şişirir, bu yüzden `CLAUDE.md` olarak **kullanma**: Bölüm 4, 11 ve 17'nin kısa özetini `CLAUDE.md` yap, bu dosyayı ayrı tut.
> Dil: Arayüz ve kullanıcıya dönük her şey **Türkçe**. Kod, sınıf ve değişken adları İngilizce.

> **v2 NOTU:** Bu sürüm, ilk planın gözden geçirilmesiyle düzeltildi: .NET 10 LTS, bozuk bölüm referansları, Zapret kalıntıları ve yeni **Bölüm 17 (Güvenilirlik)**. Bölüm 17, Bölüm 16 ile birlikte önceliklidir.

> **KARAR GÜNLÜĞÜ (güncel):** Bölüm **16** ("Seçenek C") bu belgenin **önceliklidir**. Önceki bölümlerle (özellikle 5.3, 14.5, 15.7) çelişen bir yer varsa **16 geçerlidir**. Özet: FoxFix = teşhis + onarım + temizlik + küçük bir GoodbyeDPI (blacklist'li) yöneticisi. Zapret/ByeDPI/WireSock gibi gelişmiş bypass yöntemleri için **SplitWire-Turkey'e yönlendirir**, onun kalıntılarını tanır ve temizler.

---

## 1. Özet

**FoxFix**, Windows'ta (özellikle Türkiye'deki kullanıcıların yaşadığı) ağ, DNS, GoodbyeDPI/Zapret kalıntısı ve genel performans sorunlarını **tek tıkla tespit edip düzelten**, sade ve şık menülü, açık kaynaklı bir masaüstü uygulamasıdır.

- Platform: Windows 11 birincil, Windows 10 22H2 en iyi çabayla (Win10 desteği bitti, DoH gibi özellikler Win10'da kapalı olabilir)
- Teknoloji: C# / **.NET 10 LTS** (.NET 8 desteği Kasım 2026'da bitiyor, doğrula) / WPF
- Dağıtım: GitHub Releases (tek dosya `.exe`)
- Lisans: MIT (üçüncü taraf bileşenler kendi lisanslarıyla, bkz. bölüm 10 ve 16.7)

### Hedef kullanıcı
Bilgisayardan pek anlamayan, "internet var ama hiçbir şey açılmıyor" diyen, GoodbyeDPI/Zapret kurup sonra başı belaya giren Türk kullanıcı. Arayüz korkutmamalı, teknik terim yığmamalı.

### Tasarım felsefesi
1. **Önce teşhis, sonra düzeltme.** Kullanıcıya neyin bozuk olduğunu sade dille göster, onay al, düzelt.
2. **Hiçbir şeyi sessizce değiştirme.** Her değişiklik önizlenir, loglanır, geri alınabilir.
3. **Çevrimdışı çalışmalı.** İnterneti olmayan bilgisayarda da açılıp düzeltebilmeli. Tüm gerekli dosyalar pakete gömülü.
4. **Telemetri yok.** Hiçbir veri dışarı gönderilmez.

---

## 2. Doğuş hikâyesi (neden bu araç)

Gerçek bir vaka, aracın çekirdek mantığını belirliyor:

- Belirti: "Wi-Fi bağlı, sinyal %100, ama hiçbir siteye girilmiyor". `ping 8.8.8.8` çalışıyor, `ping google.com` "host bulunamadı" diyor.
- Telefon hotspot'unda da aynı sorun, yani sorun ağda değil bilgisayarın içinde.
- Kök neden: GoodbyeDPI **servis olarak** kurulmuştu ve komut satırında `--dns-addr 77.88.8.8 --dns-port 1253` vardı. Bu, bilgisayardan çıkan **tüm UDP DNS isteklerini** Yandex DNS'e 1253 portundan yönlendiriyor, cevap gelmeyince DNS tamamen çöküyordu.
- Ayırt edici kanıt: `nslookup google.com 8.8.8.8` **UDP'de zaman aşımı**, ama `nslookup -vc google.com 8.8.8.8` **TCP'de çalışıyor**. GoodbyeDPI sadece UDP DNS'i yakalıyor.
- Kullanıcı "GoodbyeDPI'ı kapattım" sanıyordu, ama servis arka planda çalışıyordu (`goodbyedpi.exe`). `goodbyedpi` klasörü bu yüzden silinemiyordu, `WinDivert64.sys` kilitliydi.
- Çözüm: servisi yolundan bulup durdurmak ve silmek, WinDivert sürücüsünü kaldırmak, DNS'i onarmak, gerekirse yeniden başlatmak.

Bu zinciri otomatikleştirmek, FoxFix'in ilk ve en önemli özelliği.

---

## 3. Mimari

### Teknoloji yığını
| Katman | Seçim |
|---|---|
| Dil / çalışma zamanı | C# 14, .NET 10 LTS |
| Arayüz | WPF (alternatif: WinUI 3, ama WPF Win10'da daha az sorun çıkarır) |
| Desen | MVVM, `CommunityToolkit.Mvvm` |
| Bağımlılık enjeksiyonu | `Microsoft.Extensions.DependencyInjection` |
| Loglama | `Serilog` (dosya sink, `%LocalAppData%\FoxFix\logs`) |
| Sistem erişimi | `System.Management` (WMI), `System.ServiceProcess`, `Microsoft.Win32.Registry`, `Microsoft.Win32.TaskScheduler` (NuGet) |
| Ağ | `System.Net.NetworkInformation`, `System.Net.Sockets` |
| Test | xUnit, Moq, FluentAssertions |
| Paketleme | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` |
| CI | GitHub Actions |

### Yetki
`app.manifest` içinde `requireAdministrator`. Uygulama yönetici olarak açılır, kullanıcıya bunu ilk açılışta kısaca açıkla.

### Klasör yapısı
```
FoxFix/
├─ CLAUDE.md                  (bu dosyanın kısa özeti ve çalışma kuralları)
├─ FOXFIX-PLAN.md             (bu dosya)
├─ README.md                  (Türkçe, ekran görüntülü)
├─ LICENSE                    (MIT)
├─ THIRD-PARTY-NOTICES.md     (GoodbyeDPI, WinDivert, Zapret lisansları)
├─ src/
│  ├─ FoxFix.App/             (WPF arayüzü, ViewModel'lar, tema)
│  ├─ FoxFix.Core/            (iş mantığı, arayüzden bağımsız)
│  │  ├─ Diagnostics/         (teşhis motoru ve kontroller)
│  │  ├─ Fixes/               (düzeltme işlemleri, hepsi IFix)
│  │  ├─ Network/             (DNS, adaptör, ping, DoH)
│  │  ├─ Dpi/                 (GoodbyeDPI yönetimi; Zapret/SplitWire için yalnızca tespit ve temizlik)
│  │  ├─ Maintenance/         (temizlik, başlangıç, SFC/DISM)
│  │  ├─ Safety/              (geri yükleme noktası, geri alma kaydı)
│  │  └─ Reporting/           (rapor üretimi)
│  └─ FoxFix.Resources/       (gömülü GoodbyeDPI/Zapret dosyaları, ikonlar)
├─ tests/
│  ├─ FoxFix.Core.Tests/
│  ├─ fixtures/               (gerçek sc/ipconfig/nslookup çıktıları, Türkçe ve İngilizce Windows)
│  └─ scenarios/              (elle test senaryoları, bölüm 9)
└─ .github/workflows/         (build ve release)
```

### Temel soyutlamalar
```csharp
public interface ICheck {                 // Bir şeyi inceler, değiştirmez
    string Id { get; }
    string Title { get; }                 // Türkçe, sade
    Task<CheckResult> RunAsync(CancellationToken ct);
}

public interface IFix {                   // Bir şeyi değiştirir
    string Id { get; }
    string Title { get; }
    Task<FixPreview> PreviewAsync();      // Ne değişecek, kullanıcıya göster
    Task<FixResult> ApplyAsync(UndoJournal journal, CancellationToken ct);
    Task UndoAsync(UndoRecord record);    // Mümkünse geri al
}
```
Sistem komutları (`netsh`, `sc`, `powershell`) tek bir `ICommandRunner` arkasında olsun. Böylece testlerde sahte (mock) komut çalıştırıcı kullanılabilir. Mümkün olduğunda native API tercih et (servisler için `ServiceController`, DNS için WMI), komut çalıştırma yedek yol olsun.

---

## 4. Güvenlik ve geri alma ilkeleri (pazarlık yok)

| İlke | Uygulama |
|---|---|
| Önizleme | Her düzeltme çalışmadan önce "şunu değiştireceğim" listesi gösterir |
| Onay | Silme, sürücü kaldırma, ayar değiştirme için açık onay kutusu |
| Geri yükleme noktası | Büyük işlemlerden önce `Checkpoint-Computer` ile oluştur (başarısızsa kullanıcıyı uyar, sessizce geçme) |
| Geri alma günlüğü | Değiştirilen her ayarın eski değerini `%LocalAppData%\FoxFix\undo.json`'a yaz. "Geri Al" butonu bununla çalışır |
| Güvenli liste | Microsoft servislerini, imzalı sistem sürücülerini, kullanıcının kişisel dosyalarını **asla** silme. Silinecekler açık bir izin listesinden (allowlist) gelir |
| Eşleştirme sıkılığı | Servis/dosya eşleştirmede geniş regex kullanma. `goodbyedpi`, `windivert`, `winws` gibi kesin adlar ve yol kontrolü |
| Loglama | Her işlem, komut ve sonuç loglanır, kullanıcı raporda görebilir |
| Silme koruması | Klasör silerken sadece bilinen ad kalıplarını (`goodbyedpi*`) ve kullanıcı seçimini kabul et, yol kökünü (`C:\`, profil kökü) hiçbir zaman silme |

---

## 5. Modüller

### 5.1 Ağ Onarım ("İnternetim Çalışmıyor")
Bu, uygulamanın ana butonu: **Beni Düzelt**.

**Teşhis katmanları (sırayla, ilk bozuk katmanda dur ve raporla):**

| Katman | Kontrol | Bozuksa anlamı |
|---|---|---|
| 1. Adaptör | Fiziksel adaptör `Up` mı, devre dışı mı, "Not Present" mı | Sürücü / donanım / Wi-Fi kapalı |
| 2. IP / DHCP | Geçerli IPv4, gateway var mı (169.254.x.x APIPA mı) | Modemle anlaşma yok |
| 3. Ağ | Gateway'e ve `8.8.8.8` / `1.1.1.1`'e ping | Hat kopuk |
| 4. DNS | Aşağıdaki **UDP / TCP / DoH karşılaştırması** | DNS sorunu (en sık) |
| 5. HTTPS | `https://1.1.1.1` ve bir site için HTTPS isteği | Güvenlik duvarı / proxy / DPI |
| 6. Yazılım | GoodbyeDPI/Zapret/VPN/proxy kalıntısı, çoklu antivirüs | Yazılım kaynaklı engel |

**DNS karar ağacı (en değerli kısım):**
```
ping 8.8.8.8 BAŞARILI + ping google.com BAŞARISIZ → DNS sorunu
    UDP DNS (8.8.8.8:53) zaman aşımı + TCP DNS çalışıyor
        → UDP 53 yakalanıyor: DPI aracı / filtre sürücüsü / güvenlik yazılımı şüphesi
          (WinDivert sürücüsü yüklü mü? GoodbyeDPI servisi var mı?)
    UDP ve TCP ikisi de başarısız
        → DNS sunucusuna hiç ulaşılamıyor: sunucu engelli / güvenlik duvarı
    Sistem DNS'i başarısız ama doğrudan sorgu çalışıyor
        → Yerel DNS ayarı bozuk: DNS'i otomatik/1.1.1.1/8.8.8.8 yap, önbelleği temizle
```

**Düzeltme adımları (`IFix` olarak):**
1. Çalışan `goodbyedpi.exe` / `winws.exe` işlemlerini sonlandır
2. DPI servislerini **çalıştırdığı dosya yoluna** bakarak bul, durdur, sil (adı ne olursa olsun)
3. WinDivert sürücüsünü durdur ve sil (`WinDivert`, `WinDivert14`, `WinDivert1.4`)
4. Zamanlanmış görev, `Run` kayıt defteri girdisi ve Başlangıç klasörü kalıntılarını temizle
5. Proxy'yi sıfırla (`netsh winhttp reset proxy`)
6. DNS'i ayarla ve DoH'yi etkinleştir (bölüm 5.2)
7. `ipconfig /flushdns`, gerekirse DHCP yenile
8. Yeniden başlatma gerekiyorsa kullanıcıya sor ve **yeniden başladığında otomatik devam et** (RunOnce)
9. Sonuç testi ve kullanıcıya sade özet

### 5.2 DNS Ayarları
- Seçenekler: Otomatik (modem), Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9), AdGuard, özel
- **DNS Hız Testi:** Her sağlayıcıya birkaç sorgu at, medyan gecikmeyi göster, en hızlıyı öner. (Gerçek vaka: DNS seçimi `google.com` pingini 26 ms'den 69 ms'ye çıkarabiliyor, çünkü seni uzak bir sunucuya yönlendiriyor.)
- **DoH desteği:** Windows 11 (22000+) ve 10 (yeni sürümler) kontrol et, desteklemeyen sürümde gizle.
- **Win10 uyarısı:** `netsh dns add encryption` büyük olasılıkla yalnızca Windows 11'de (22000+) çalışır. Win10'da çalıştığı doğrulanmadan DoH'yi orada gösterme.
- **DİKKAT, hata düzeltmesi:** `netsh dns add encryption` komutunda parametre `autoupgrade=yes`'tir. `autodoh` **geçersizdir** ve komut hata verir.
  ```
  netsh dns add encryption server=1.1.1.1 dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=yes
  ```
- DNS'i elle ayarlarken tüm **fiziksel ve bağlı** adaptörlere uygula, sanal adaptörlere dokunma.
- Eski ayarı geri almak için undo günlüğüne kaydet.

### 5.3 GoodbyeDPI / Zapret Yöneticisi
**Amaç:** Kullanıcı doğru kursun, yanlış kurup başını belaya sokmasın.

- **Profiller** (kullanıcının sorunsuz çalışan ayarlarına göre, bölüm 12'deki dosyalardan çıkarılacak):
  - "Önerilen": DNS yönlendirmesi **olmadan**, sadece parçalama ve TTL ayarları
  - "DNS yönlendirmeli": `--dns-addr` / `--dns-port` ile (çakışma uyarısıyla)
  - ~~Zapret için ayrı profiller~~ (Bölüm 16: kapsam dışı, SplitWire'a yönlendirilir)
- **Çalıştırma modu:** "Elle başlat/durdur" (varsayılan, güvenli) ya da "Servis olarak kur" (uyarıyla).
- **Tek tık tam kaldırma:** Bölüm 5.1'deki temizlik adımlarının aynısı.
- **Çakışma tespiti:** GoodbyeDPI ve Zapret aynı anda çalışıyorsa, ya da iki WinDivert sürümü yüklüyse uyar.
- **Durum paneli:** Çalışıyor mu, hangi profil, hangi sürücü, hangi servis.
- **Durdururken:** Süreç sonlanana ve sürücü inene kadar bekle, `STOP_PENDING`'de takılırsa kullanıcıya yeniden başlatma öner.
- GoodbyeDPI'ın kurulum betiğindeki `--dns-addr` benzeri parametreler **sorun çıkarabilir**, bu yüzden varsayılan profilde kullanma ve kullanıcıyı bilgilendir.

### 5.4 Erişim Testi
Hangi servisler açılıyor, hangileri açılmıyor:
- Discord, Roblox, Steam, Epic, YouTube, Twitch, Instagram, vb. (liste ayarlanabilir, JSON)
- Her biri için DNS çözümü + HTTPS el sıkışma + durum (Açık / DNS engeli / Bağlantı engeli)
- Sonuca göre profil önerisi ("Discord açılmıyor, şu profili dene")

### 5.5 Windows Bakım ve Performans
Her madde **önizleme + onay + geri alma** ile. Agresif "optimizasyon" hurafelerinden kaçın.

| Alt modül | Ne yapar |
|---|---|
| Temizlik | Geçici dosyalar, Windows Update önbelleği, çöp kutusu, tarayıcı önbelleği (**önce boyutları göster**, sonra onayla) |
| Başlangıç yöneticisi | Açılışı yavaşlatan programları etkisiyle listele, devre dışı bırak (silme değil) |
| Güç planı | Yüksek performans / dengeli geçişi, laptoplarda pil uyarısı |
| Depolama | Disk doluluğu, büyük klasörler, Depolama Algısı önerisi |
| Disk sağlığı | SMART özeti (varsa), sağlıksız diski uyar |
| Sistem onarımı | SFC, DISM ve CHKDSK'yi arayüzden ilerleme göstererek çalıştır |
| Windows Update sıfırlama | Takılan güncellemeler için bilinen güvenli sıfırlama adımları |
| Teslim Optimizasyonu | Diğer bilgisayarlara yükleme paylaşımını kapatma seçeneği |
| Çoklu antivirüs uyarısı | `root/SecurityCenter2` ile yüklü antivirüsleri listele, 2+ ise yavaşlama uyarısı ver (kaldırmayı önerir, kendisi kaldırmaz) |
| Sürücü kontrolü | Sorunlu / eski sürücüleri listele (Aygıt Yöneticisi hata kodları: 10, 43, 45...) |
| Pil raporu | Laptopta `powercfg /batteryreport` özeti |
| Oyun için | Game Mode durumu, arka plan uygulama yükü, 3. taraf servislerin listesi |
| Hayalet aygıtlar | "Not Present" ağ adaptörleri listesi (zararsız, bilgilendir, istenirse temizle) |

**Yapma:** Kayıt defteri "temizleyicileri", sistem servislerini toplu kapatma, "RAM boşaltıcı", Defender'ı kapatma. Bunlar fayda sağlamaz, zarar verir.

### 5.6 Rapor
- Tek tıkla **Teşhis Raporu**: Sistem sürümü, adaptörler, IP/DNS, ping/DNS testleri, yüklü güvenlik yazılımları, sürücüler, FoxFix'in yaptığı işlemler.
- Düz metin (`.txt`) ve isteğe bağlı panoya kopyala. Kişisel veri (kullanıcı adı, MAC adresi) varsayılan olarak **maskelenir**, kullanıcı isterse açar.
- Amaç: Kullanıcı bunu arkadaşına ya da bir destek forumuna atabilsin.

### 5.7 Ayarlar / Hakkında
Tema (koyu/açık), dil (şimdilik TR), "yeniden başlatma sonrası devam et", log klasörünü aç, sürüm, GitHub bağlantısı, güncelleme kontrolü (**kullanıcı isterse**, otomatik değil).

---

## 6. Arayüz tasarımı ("klas menü")

- **Tema:** Koyu zemin (`#14161A`), vurgu rengi **tilki turuncusu** (`#FF7A1A`), kartlar yuvarlatılmış köşeli (12 px), hafif gölge. Açık tema da olsun.
- **Düzen:** Solda dikey menü (ikon + yazı), sağda içerik. Üstte sistem durum şeridi ("Ağ: Sağlıklı", "DPI aracı: Çalışmıyor").
- **Ana ekran:** Ortada büyük **"Beni Düzelt"** butonu, altında kartlar: Ağ, DNS, GoodbyeDPI, Performans, Rapor.
- **Menü:** Ana Sayfa, İnternet, DNS, GoodbyeDPI/Zapret, Erişim Testi, Windows Bakım, Rapor, Ayarlar, Hakkında.
- **İlerleme:** Düzeltme sırasında adım listesi: ✔ tamam, ⏳ sürüyor, ✖ hata, ⚠ dikkat. Her satırın yanında kısa açıklama.
- **Animasyon:** Hafif geçişler (200 ms), `prefers-reduced-motion` benzeri bir "animasyonları azalt" ayarı.
- **Dil üslubu:** Teknik terimi sadeleştir.
  - "DNS çözümleme hatası" → "İnternet adreslerini bulamıyorum"
  - "WinDivert sürücüsü yüklü" → "Eski bir internet aracından kalan bir parça arkada çalışıyor"
  - Teknik ayrıntı "Ayrıntıları göster" arkasında.
- **Erişilebilirlik:** Klavye gezinmesi, yüksek kontrast, ekran okuyucu etiketleri.
- **Logo:** Basit, geometrik bir tilki başı (SVG), uygulama ikonu `.ico`.

---

## 7. Bilinen tuzaklar (bugünkü olaydan öğrenilenler)

Claude Code bunları **mutlaka** dikkate almalı:

1. **Servis adı tahmin edilmez.** GoodbyeDPI servisi kullanıcıya göre farklı adda olabilir. `Win32_Service` içinde `PathName` değerine bakarak bul (`goodbyedpi`, `windivert`, `winws`).
2. **Servis ≠ görev yöneticisindeki işlem.** `taskkill` yetmez, servis yeniden başlatabilir. Önce servisi durdur ve sil, sonra işlemi öldür.
3. **WinDivert sürücüsü `STOP_PENDING`'de takılabilir.** `sc delete` "silinmek üzere işaretlendi" (1072) der, bellekten inmesi için **tam yeniden başlatma** gerekir. Hızlı başlatma sürücüyü bellekte tutabilir, `shutdown /r /f /t 0` kullan (Kapat değil Yeniden Başlat).
4. **Klasör kilidi:** `WinDivert64.sys` yüklüyken klasör silinmez. Önce sürücüyü indir, gerekirse yeniden başlat, sonra sil. Güvenli mod son çare.
5. **UDP vs TCP DNS testi** teşhisin en güçlü kanıtı. İkisini de mutlaka yap.
6. **Ping gateway'e cevap vermeyebilir.** Timeout tek başına arıza değil. Gateway adresini `ipconfig`'ten oku, sabit `192.168.1.1` varsayma (bazı ağlarda `192.168.0.1`, `10.0.0.1`).
7. **Yönetici yetkisi şart.** `ipconfig /flushdns` yetkisiz çalışmaz ("yükseltme gerekiyor").
8. **Adaptör adı sabit değil.** `Wi-Fi` varsayma. `Get-NetAdapter -Physical` ile aktif olanları bul. "Wi-Fi 2/3 Not Present" hayalet adaptörler normal, sanal "Wi-Fi 4/5" Wi-Fi Direct adaptörleridir, dokunma.
9. **Hosts, proxy, DNS Client servisi** (`dnscache`) kontrol listesinde olsun. Bunlar elenince sorun başka yerdedir.
10. **Çoklu antivirüs** (Malwarebytes + McAfee + 360 + Defender) gerçek vakada vardı. Suçlu çıkmasa da uyarı değerli.
11. **Karakter kodlaması:** `sc` ve `netsh` çıktıları Türkçe Windows'ta OEM kod sayfasındadır, çıktı ayrıştırırken `Console.OutputEncoding` / doğru `Encoding` kullan (CP857/CP850). Dile bağlı metinlere güvenme, mümkünse WMI/API kullan, çıktı metni ayrıştırma.
12. **İnternet yokken çalışmalı.** Hiçbir temel işlev internet gerektirmesin.
13. **Zamanlanmış görev / Run kaydı / Startup klasörü** her üçüne de bakılmalı, GoodbyeDPI "her açılışta başlasın" diye herhangi birini kullanmış olabilir.

---

## 8. Yol haritası

> **ESKİ, GEÇERSİZ.** Güncel yol haritası Bölüm 16.6'dadır (Zapret ve strateji sihirbazı kapsam dışı). Aşağıdaki liste yalnızca tarihçe içindir.

### v0.1: Çekirdek (MVP)
- [ ] Çözüm iskeleti, MVVM, DI, loglama, tema, ana pencere ve menü
- [ ] `ICommandRunner`, `ICheck`, `IFix`, `UndoJournal`, geri yükleme noktası
- [ ] Teşhis katmanları 1-6 ve DNS karar ağacı
- [ ] "Beni Düzelt": DPI kalıntı temizliği + DNS onarımı + sonuç testi
- [ ] Rapor üretimi (maskeleme ile)
- [ ] Yeniden başlatma sonrası otomatik devam (RunOnce)
- [ ] Birim testleri (ayrıştırıcılar, karar ağacı)

### v0.2: DNS ve GoodbyeDPI yöneticisi
- [ ] DNS sağlayıcı seçimi + hız testi + DoH
- [ ] GoodbyeDPI/Zapret profilleri, kur/başlat/durdur/kaldır
- [ ] Çakışma tespiti, durum paneli
- [ ] Erişim testi (Discord vb.)

### v0.3: Windows bakım
- [ ] Temizlik, başlangıç yöneticisi, güç planı, çoklu antivirüs uyarısı
- [ ] SFC / DISM / Windows Update sıfırlama arayüzü
- [ ] Disk sağlığı, sürücü kontrolü, pil raporu

### v1.0: Yayın
- [ ] README (Türkçe, ekran görüntüsü, GIF)
- [ ] GitHub Actions ile otomatik derleme ve Release, SHA256 özeti
- [ ] SmartScreen/antivirüs yanlış alarmı için dokümantasyon ve Microsoft Defender'a gönderim
- [ ] Kod imzalama araştırması (açık kaynak projeler için **SignPath Foundation** ücretsiz imzalama sunuyor, koşulları doğrula)
- [ ] Yardım/SSS sayfası, issue şablonları (hata raporu şablonu rapor dosyası ister)

---

## 9. Test planı

### Birim testleri
- Komut çıktısı ayrıştırıcıları (sahte çıktı örnekleriyle)
- DNS karar ağacı (her kombinasyon için)
- Servis eşleştirme (yanlış pozitif olmamalı: `Windows Update`, `Dnscache`, vs. eşleşmemeli)
- Silme güvenlik kontrolü (`C:\`, profil kökü, sistem klasörü reddedilmeli)

### Elle senaryo testleri (sanal makinede)
Bir Windows 10 ve bir Windows 11 VM'de, **anlık görüntü (snapshot)** alarak:

1. **GoodbyeDPI servis kalıntısı:** GoodbyeDPI'ı `--dns-addr 77.88.8.8 --dns-port 1253` ile servis olarak kur → DNS bozulsun → FoxFix düzeltmeli
2. **WinDivert takılı sürücü:** `STOP_PENDING` durumunu yeniden oluştur → yeniden başlatma yönlendirmesi çalışmalı
3. **Elle bozuk DNS:** Var olmayan bir DNS adresi gir → FoxFix tespit edip düzeltmeli
4. **Proxy bozukluğu:** Sahte proxy ekle → temizlenmeli
5. **Çoklu antivirüs uyarısı:** (taklit) → uyarı çıkmalı
6. **Geri alma:** Her düzeltmeden sonra "Geri Al" ayarı eski haline getirmeli
7. **Çevrimdışı:** İnternet kapalıyken uygulama açılmalı ve teşhis çalışmalı
8. **Yetkisiz kullanıcı:** UAC reddedilirse düzgün hata mesajı

### Kabul ölçütü
Her modül için: hata verirse çökmemeli, kullanıcıya anlaşılır Türkçe mesaj göstermeli, log yazmalı.

---

## 10. Dağıtım ve hukuki notlar

- **FoxFix lisansı:** MIT.
- **Üçüncü taraf:** GoodbyeDPI (Apache-2.0 olarak biliniyor), WinDivert (LGPL-3.0 / GPL-2.0 çift lisans olarak biliniyor). Zapret FoxFix ile dağıtılmaz (Bölüm 16), yalnızca tanınır ve temizlenir. **Yayından önce her birinin güncel lisansını resmi depolarından doğrula** ve `THIRD-PARTY-NOTICES.md`'ye lisans metinleriyle ekle.
- **Dağıtım biçimi:** İki seçenek, birlikte düşün:
  - (a) İkili dosyaları pakete **gömmek** (çevrimdışı çalışır, ama lisans yükümlülüğü ve güncel kalma sorumluluğu senin)
  - (b) Kullanıcıdan klasör seçtirmek ya da resmi GitHub'dan indirmek (internet gerekir)
  - Öneri: v0.2'de (a), kaynak ve sürüm bilgisini açıkça göster.
- **Antivirüs yanlış alarmı:** WinDivert sürücüsü kuran her uygulama antivirüsleri tetikleyebilir. README'de açıkla, kaynak kodu açık tut.
- **Sorumluluk reddi:** README'de "kendi sorumluluğunuzda kullanın, geri yükleme noktası oluşturulur" notu.
- **Yasal:** Bu araç erişim engellerini aşmak için DPI araçlarını yönetiyor. Kullanıcıya hukuki sorumluluğun kendisinde olduğunu belirten kısa bir uyarı ekle, aracı tanıtırken "yasa dışı içerik için" gibi bir dil kullanma.

---

## 11. Claude Code için çalışma kuralları

Bu bölüm `CLAUDE.md`'ye de konabilir.

1. **Küçük adımlarla ilerle.** Her modülü bitir, derle, test et, commit at, sonra diğerine geç.
2. **Önce `FoxFix.Core`,** sonra arayüz. Arayüz sadece Core'u çağırsın.
3. **Her `IFix` için** önizleme, geri alma ve birim testi yaz.
4. **Sistem komutlarını** doğrudan çağırma, `ICommandRunner` kullan.
5. **Türkçe metinleri** `Resources/Strings.tr.resx` içinde tut, kodun içine gömme.
6. **Hata yutma yok.** Her istisna loglanır ve kullanıcıya sade bir mesajla gösterilir.
7. **Silme ve sürücü işlemleri** için her zaman allowlist ve onay.
8. **Belirsiz kaldığında** tahmin yürütme, bana sor. Özellikle: lisans, sürücü kaldırma mantığı, GoodbyeDPI parametreleri.
9. **Windows'ta gerçek test** yapamadığın yerde bunu açıkça belirt, "çalışıyor" deme, "şu senaryoyla elle test edilmeli" de.
10. **Commit mesajları** Türkçe ya da İngilizce, ama tutarlı ve açıklayıcı.

### Başlangıç komutu (Claude Code'a verilecek ilk istek)
> `FOXFIX-PLAN.md` dosyasını baştan sona oku. Sonra v0.1 yol haritası için bir çözüm iskeleti kur: `FoxFix.sln`, `FoxFix.App` (WPF, .NET 8), `FoxFix.Core`, `FoxFix.Core.Tests`. Bölüm 3'teki `ICheck`, `IFix`, `ICommandRunner` ve `UndoJournal` arayüzlerini yaz. Ardından Bölüm 5.1'deki teşhis katmanlarından 1, 2 ve 3'ü (adaptör, IP, ping) uygula, birim testleriyle birlikte. Her adımda derle ve testleri çalıştır. Bitince ne yaptığını, ne test edemediğini ve bir sonraki önerilen adımı özetle.

---

## 12. Açık kararlar (kullanıcıdan)

GoodbyeDPI ve Zapret dosyaları teslim edildi (Bölüm 14). Kalanlar:
- [ ] Hangi Windows sürümlerinde test edilecek (10 / 11), sanal makine (Hyper-V / VMware) var mı
- [ ] Hedef kitle için Discord / Roblox dışında öncelikli servis var mı
- [ ] Logo ve renk: tilki turuncusu (`#FF7A1A`) uygun mu
- [ ] Uygulama adı kesin **FoxFix** mi, GitHub kullanıcı adı / depo adı
- [ ] Kod imzalama için SignPath başvurusu yapılacak mı

---

## 13. Referans: bugün çalışan teşhis ve onarım komutları

```
:: Teşhis
ipconfig /all
ping 8.8.8.8
ping google.com
nslookup google.com 8.8.8.8          :: UDP
nslookup -vc google.com 8.8.8.8      :: TCP (UDP'den farklıysa DPI/filtre şüphesi)
curl.exe -I --max-time 8 https://1.1.1.1
sc query type= driver state= all | findstr /i "divert"
netsh winhttp show proxy
type C:\Windows\System32\drivers\etc\hosts

:: DPI kalıntı temizliği (servisi yolundan bul)
:: UYARI: aşağıdaki regex yalnızca TEŞHİS içindir (geniş, yanlış pozitif verebilir).
:: Silme işlemi Bölüm 4'teki kesin ad + yol allowlist'iyle yapılır, bu regex ile ASLA.
powershell -Command "Get-CimInstance Win32_Service | Where-Object { $_.PathName -match 'goodbye|windivert|dpi' } | Select Name,PathName,State"
sc stop <Ad>
sc delete <Ad>
sc stop WinDivert
sc delete WinDivert
taskkill /f /im goodbyedpi.exe
taskkill /f /im winws.exe
shutdown /r /f /t 0

:: DNS onarımı
netsh interface ip set dns name="<AdaptörAdı>" static 1.1.1.1 primary
netsh interface ip add dns name="<AdaptörAdı>" 1.0.0.1 index=2
netsh dns add encryption server=1.1.1.1 dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=yes
ipconfig /flushdns
```
Bunlar **referans** içindir, uygulamada mümkün olduğunca native API ile yapılmalı (bölüm 3).

---

## 14. DNS.zip analizi: GoodbyeDPI ve Zapret profilleri (gerçek dosyalardan çıkarıldı)

Kaynak: kullanıcının yüklediği `DNS.zip` (GoodbyeDPI-Turkey 0.2.3rc3 + `zapret-win-turkey` topluluk paketi). Aşağıdakiler `.cmd` / `.au3` dosyalarından **okunarak** çıkarıldı, Windows'ta çalıştırılıp test edilmedi.

### 14.1 GoodbyeDPI-Turkey profilleri
Servis adı **sabit: `GoodbyeDPI`**. Servis `binPath` içinde `%CD%\x86_64\goodbyedpi.exe` yolunu saklar, klasör taşınırsa servis bozulur (README de bunu söylüyor). Mimari: `PROCESSOR_ARCHITECTURE=AMD64` ise `x86_64`, değilse `x86`.

| Profil (dosya adı) | Parametreler | DNS yönlendirme | Not |
|---|---|---|---|
| `…dnsredir_turkey` (varsayılan) | `-5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253` | **Evet (Yandex)** | Gerçek vakada DNS'i bozan profil bu |
| `…alternative_superonline` | `--set-ttl 3` | Hayır | Windows DNS'i elle ayarlanmalı |
| `…alternative2_superonline` | `-5` | Hayır | TTL yok. README yavaş siteler için bunu öneriyor |
| `…alternative3_superonline` | `--set-ttl 3 --dns-addr … --dns-port 1253 …` | Evet | |
| `…alternative4_superonline` | `-5 --dns-addr … --dns-port 1253 …` | Evet | TTL yok |
| `…alternative5_superonline` | `-9 --dns-addr … --dns-port 1253 …` | Evet | |
| `…alternative6_superonline` | `-9` | Hayır | |

Elle çalıştırma (`turkey_dnsredir*.cmd`) aynı parametrelerle `goodbyedpi.exe`'yi `start` eder, pencere kapanınca durur. Servis kurulumu (`service_install_*`) `start= auto` ile her açılışta başlatır.

**FoxFix önerisi:**
- Varsayılan profil: **alternatif 2 (`-5`)**, elle başlat/durdur modunda. DNS yönlendirmesi yok, Windows DNS'ine dokunmaz.
- DNS yönlendirmeli profilleri "gelişmiş" altına koy ve açık uyarı göster: "Bu profil bilgisayarın tüm DNS isteklerini Yandex'e yönlendirir. Bozulursa internet adresleri çözülmez."
- Yönlendirmeli profil kurulurken, ilk başlatmadan sonra **otomatik DNS testi** yap (`nslookup` UDP + TCP). Cevap gelmiyorsa profili hemen durdur ve kullanıcıya bildir.
- Her profil için parametreleri bir JSON'da tut, kod içine gömme:
```json
{ "id": "gdpi-alt2", "engine": "goodbyedpi", "args": ["-5"], "dnsRedirect": false,
  "title": "Önerilen (DNS'e dokunmaz)", "risk": "low" }
```

### 14.2 `zapret-win-turkey` paketi (topluluk derlemesi)
Bu **resmi bol-van/zapret değil**, AutoIt ile yazılmış bir arayüz (`zapret-win-turkey.exe`) etrafında toplanmış bir paket. İçeriği:
- `bin\zapret\zapret-winws\winws.exe` ve `winws2.exe` + `WinDivert.dll` + **`Monkey64.sys`** (WinDivert sürücüsünün **yeniden adlandırılmış** hâli)
- `bin\dnscrypt-proxy\` (DNS'i yerel proxy üzerinden şifreleyen servis)
- `bin\go-pcap2socks\` (ek bir proxy/yönlendirme aracı)
- `bin\zapret\blockcheck\` (hangi stratejinin çalıştığını test eden araç, Cygwin dahil)
- `config\`: `config.ini`, `strategy2.txt`, `hostlist.txt`, `excludelist.txt`, `autohostlist.txt`

**Kullanıcının çalışan ayarı:**
- `config.ini`: `ActiveEngine=0`, `ActiveStrategy=Analiz Sonucu`, `FilterMode=1`, `DnscryptInstalled=0`
- `strategy2.txt` (blockcheck sonucu, roblox.com için `AVAILABLE` çıkan strateji):
  `--payload="tls_client_hello" --lua-desync="tcpseg:pos=0,1:ip_id=rnd:repeats=20"`
- `hostlist.txt`: Discord (`discord.com`, `discord.gg`, `discordapp.com`, `discordapp.net`, `discord.media`, `discord.store`, `discord.net`), Roblox (`roblox.com`, `rbxcdn.com`, `rbxinfra.net`, `rbxtrk.com`), `pastebin.com`
- `excludelist.txt`: `com.tr`, `gov.tr`, `google.com`, `googleapis.com`

Paketin yerleşik strateji listesi (`.au3`, satır 549-580), `winws2` (lua) için ör. `multidisorder:pos=2:seqovl=1`, `multisplit ... ip_ttl=5`, `fake:blob=0x00000000:ip_ttl=5`; klasik `winws` için `--dpi-desync=fake --dpi-desync-ttl=3/4/5`, `fake,multisplit --dpi-desync-fooling=badseq`, `multisplit --dpi-desync-split-pos=2` vb.

### 14.3 KRİTİK bulgular (FoxFix mantığını doğrudan etkiler)
1. **Zapret'in sürücü servisi adı `monkey`.** `.au3` temizlik kodu şunu çalıştırıyor: `sc stop/delete WinDivert, WinDivert14, monkey`. FoxFix'in (ve bugünkü `ag-tam-onarim.bat`'ın) temizliği `monkey` servisini ve `Monkey64.sys` sürücüsünü de kapsamalı. **Eski betikte `monkey` yok, eklenmeli.**
2. **Servis yolundan bulma yaklaşımı yetmeyebilir:** `monkey` servisinin `PathName` değeri `Monkey64.sys`'e gider, `goodbye|windivert|dpi` eşleşmesi onu yakalamaz. Eşleştirmeye `monkey`, `winws`, `zapret` ve sürücü dosyası adları (`WinDivert*.sys`, `Monkey*.sys`) eklenmeli. Yine de **sadece allowlist** ile: rastgele "monkey" içeren servisleri silme, yol kontrolüyle doğrula (`...\zapret-win-turkey\...`).
3. **dnscrypt-proxy DNS'i değiştirir.** Servis kurulursa DNS genelde `127.0.0.1`'e döner. Dnscrypt servisi silinir ama DNS `127.0.0.1`'de kalırsa internet adresleri çözülmez ("DNS zehirlenmesi", `.au3` içindeki yorum da bunu söylüyor). FoxFix: `dnscrypt-proxy` servisini ve DNS = `127.0.0.1` durumunu tespit edip DHCP/otomatik DNS'e döndürsün.
4. **Aynı anda iki araç olmamalı.** GoodbyeDPI ve Zapret aynı WinDivert katmanını kullanır. `.au3` açılışta `GoodbyeDPI` servisini kendisi siliyor (satır 43). FoxFix bu çakışmayı önceden uyarmalı.
5. **`go-pcap2socks.exe`** paketin içinde, güvenlik duvarı kurallarına dokunuyor (`netsh advfirewall firewall delete rule ... program=...`, satır 355-356). FoxFix bu modülü **kullanmasın**, sadece kalıntısını (güvenlik duvarı kuralı, süreç) listeleyebilir.
6. **Kaspersky uyarısı (README):** Kaspersky GoodbyeDPI'ı engelliyor. FoxFix çoklu antivirüs taramasında Kaspersky'i özel olarak işaretlesin.
7. **`.au3` açılışta tüm klasöre `Unblock-File` çalıştırıyor** (satır 32). FoxFix kendi içeriğine dokunmasın, bunu taklit etme.

### 14.4 Dağıtım önerisi (bu paket için)
- **Bu 70 MB'lık topluluk paketini olduğu gibi FoxFix'e gömme.** Kaynağını, derlemesini ve ikili dosyaların bütünlüğünü doğrulayamıyoruz (AutoIt ile derlenmiş `.exe`, ek proxy araçları, Cygwin). AutoIt exe'leri antivirüslerde sık yanlış alarm verir, kullanıcıya zarar riski ve itibar riski taşır.
- **Daha temiz yol:** FoxFix profilleri ve parametreleri **kendisi yönetsin**, ikili dosyaları (`goodbyedpi.exe`, `winws.exe` + WinDivert) **resmi sürümlerden** kullansın:
  - GoodbyeDPI: ValdikSS/GoodbyeDPI (resmi) ya da bu Turkey fork'u, kaynak ve sürüm gösterilerek
  - Zapret: bol-van/zapret, Windows sürümü
- Çevrimdışı çalışma gerekiyorsa sadece gereken minimum dosyaları (`goodbyedpi.exe`, `WinDivert.dll`, `WinDivert64.sys`, lisanslar) paketle. `licenses/` klasöründe GoodbyeDPI, WinDivert, getline ve uthash lisansları var, `THIRD-PARTY-NOTICES.md`'ye eksiksiz kopyala. **Lisans metinlerini ben okumadım**, yayından önce kontrol et.
- Her ikili dosya için **SHA-256** doğrulaması yap, uygulamada göster.

### 14.5 FoxFix'e eklenecek yeni özellikler (bu analizden)
- [ ] `monkey` / `Monkey64.sys` / `winws*.exe` / dnscrypt-proxy temizliği ve tespiti
- [ ] "DNS = 127.0.0.1 ama dnscrypt yok" durumu için otomatik düzeltme
- [ ] Blockcheck benzeri **erişim testi** (Discord, Roblox, pastebin), hangi stratejinin açtığını raporlama
- [ ] ~~Zapret `hostlist` / `excludelist` düzenleyici~~ (ertelendi, Bölüm 16)
- [ ] ~~Strateji deneme sihirbazı~~ (ertelendi, Bölüm 16). GoodbyeDPI profilleri için basit "sırayla dene" kalabilir
- [ ] Servis `binPath` kontrolü: klasör taşınmışsa ("Dosya yolu bulunamadı") onar ya da kaldır

---

## 15. Araç indirme (GoodbyeDPI-Turkey) ve Zapret kararı

### 15.1 Kaynak
- **Depo:** `github.com/cagritaskn/GoodbyeDPI-Turkey` (yazım: `cagritaskn`). README'de bu fork'un resmi `ValdikSS/GoodbyeDPI` üzerine kurulduğu ve WinDivert'in (basil00) ayrı bir proje olduğu belirtiliyor.
- **Sürüm:** `0.2.3rc3` (kullanıcının "0.23" dediği). README, indirilecek dosyayı `goodbyedpi-0.2.3rc3-turkey.zip` olarak adlandırıyor. **Claude Code: depo sayfasından `releases` listesini okuyup dosya adını ve bağlantıyı doğrula, tahmin etme.** Bu bir *release candidate* (`rc3`), kullanıcı bunu bilerek seçti, ama arayüzde sürümü açıkça göster.
- Başka fork'ları (ör. `LiXRDev/GoodbyeDPI-Turkey-Roblox-Fix`) **kullanma**: topluluk modları, güvenilirlikleri doğrulanmadı.

### 15.2 İndirme akışı (hibrit)
1. **Önce onar:** Ağ teşhisi, DNS onarımı. İnternet/DNS çalışmıyorsa indirme düğmesi gri, nedenini söyle ("Önce internet sorununu düzeltelim").
2. **GitHub'ı DNS'e güvenmeden çöz:** `HttpClient` için özel `SocketsHttpHandler.ConnectCallback` yaz, `api.github.com` ve release dosya sunucusunu DoH ile (`https://1.1.1.1/dns-query`, adres IP olarak) çöz. Sistem DNS'i bozuk olsa da indirme çalışsın. TLS sertifika doğrulaması **açık kalacak** (ana makine adı doğrulaması yapılır).
3. **Release bilgisini al:** `GET /repos/cagritaskn/GoodbyeDPI-Turkey/releases` (kimliksiz istek hız sınırı: saatte 60). Sonucu önbelleğe al.
4. **Zip'i indir, SHA-256 hesapla**, FoxFix içindeki **sabitlenmiş (pinned) hash** ile karşılaştır. Eşleşmezse **kurma**, kullanıcıya net uyarı ver.
5. **Güvenli aç:** Zip-slip'e karşı yol doğrulaması (`..` içeren giriş reddedilir), yalnızca beklenen dosya listesi çıkarılır (`x86_64\goodbyedpi.exe`, `x86_64\WinDivert.dll`, `x86_64\WinDivert64.sys`, `x86\...`, `licenses\*`).
6. **Sabit konuma yerleştir:** `C:\ProgramData\FoxFix\tools\goodbyedpi-0.2.3rc3-turkey\`. Masaüstü/İndirilenler değil. (Servis `binPath`'i klasör yoluna bağlı, taşınırsa bozuluyor.)
7. **Mimariyi seç:** `AMD64` veya `PROCESSOR_ARCHITEW6432` varsa `x86_64`, yoksa `x86`.

### 15.3 Çevrimdışı yedek
- Pakete **sadece** şunları göm: `x86_64\goodbyedpi.exe`, `x86_64\WinDivert.dll`, `x86_64\WinDivert64.sys` (ve gerekirse `x86` karşılıkları), `licenses\*`. Yaklaşık 200-300 KB. Aynı hash listesiyle doğrulansın.
- "Zip dosyasını kendim seçeyim" seçeneği (kullanıcı başka yerden indirmişse).
- Çevrimdışı yedek, çevrimiçi indirmenin **aynı sürümü** olsun, farklı sürüm karışmasın.

### 15.4 Hash sabitleme (pinning)
- `known-good.json` içinde: sürüm, dosya adı, **SHA-256**, kaynak URL, eklenme tarihi. Hash'leri **geliştirici elle doğrulayıp ekler**: zip'i resmi depodan indir, hash'i hesapla, GitHub'ın gösterdiği hash'le karşılaştır.
- "latest" otomatik çekilmez. Yeni sürüm: önce test, sonra listeye ekle, FoxFix güncellemesiyle dağıt.
- Sürücü çekirdek yetkisiyle çalıştığı için bu kural **pazarlık dışı**.

### 15.5 Antivirüs davranışı
- İndirme/açma sonrası dosyalar yok olduysa (karantina), bunu tespit et ve açıkla: "Antivirüsün WinDivert sürücüsünü engellemiş olabilir." Dışlama eklemeyi **önerebilirsin ama kendisi ekleme.**
- README'ye göre Kaspersky GoodbyeDPI'ı engelliyor: çoklu antivirüs taramasında Kaspersky varsa özel uyarı göster.

### 15.6 Kaldırma (bu fork'un belgelediği "doğru kaldırma" ile uyumlu)
`service_remove.cmd` mantığını uygula: `GoodbyeDPI`, `WinDivert`, `WinDivert14` servislerini durdur ve sil, süreci sonlandır, sürücü bellekten inene kadar bekle, gerekirse yeniden başlatma öner, sonra klasörü sil. Bölüm 5.1 ve 14.3'teki ek temizlikleri de (monkey, dnscrypt, 127.0.0.1 DNS) çalıştır.

### 15.7 Zapret kararı
**v0.x - v1.0'da Zapret ENTEGRASYONU YOK.** Gerekçe:
- Kapsamı büyütür: strateji seçimi ISS'e göre değişir (blockcheck dakikalarca sürer), Windows sürümü hızlı gelişir (`winws2`/lua).
- GoodbyeDPI ile aynı WinDivert katmanını kullanır, ikisi çakışır. Çift destek = çift hata yüzeyi.
- Kullanıcının elindeki paket resmi sürüm değil (yeniden adlandırılmış sürücü `monkey`, ek proxy araçları).

**Ama FoxFix Zapret'i TANIMALI ve TEMİZLEYEBİLMELİ** (kullanıcılar kuracak): `winws.exe` / `winws2.exe` süreçleri, `monkey` servisi/`Monkey64.sys`, dnscrypt-proxy servisi, DNS=`127.0.0.1` durumu.

**v1.x'te isteğe bağlı "deneysel" Zapret desteği** değerlendirilebilir. Talep gerçek: GoodbyeDPI-Turkey README'si, Süperonline/fiber kullanıcılarında Discord sorunları için Zapret ve benzeri araçları öneriyor. O aşamada:
- Resmi `bol-van/zapret` Windows sürümünü kullan, bu topluluk paketini değil.
- Strateji sihirbazı (sırayla dene + otomatik DNS testi) ekle.

**İncelenecek ilgili proje:** README, aynı geliştiricinin `cagritaskn/SplitWire-Turkey` aracından söz ediyor ("GoodbyeDPI'ı kaldırdıktan sonra deneyebilirsiniz"). FoxFix'e entegre etmeden önce lisansına ve ne yaptığına bak, gerekiyorsa sadece "alternatif araç" olarak bahset.

---

## 16. KARAR: Seçenek C (karma yaklaşım)

> Bu bölüm, SplitWire-Turkey 1.5.7 sürüm sayfası ve README'sinin incelenmesi sonrası alınan karardır. **Kaynak kodu, kurulum dosyasının içeriği ve VirusTotal sonuçları incelenmedi**, aşağıdaki bilgi README'ye dayanır. Claude Code: ilgili kısımları `src/` klasöründen ve depodan doğrula.

### 16.1 Kapsam kararı
| Alan | FoxFix'te | Nasıl |
|---|---|---|
| Ağ / DNS teşhis ve onarım | **Evet (çekirdek)** | Bölüm 5.1, 5.2 |
| Windows bakım ve performans | **Evet** | Bölüm 5.5 |
| Rapor | **Evet** | Bölüm 5.6 |
| GoodbyeDPI (basit, blacklist'li) | **Evet, küçük modül** | 16.2 |
| Zapret / Zapret2 | **Hayır** (tanı + temizle) | SplitWire'a yönlendir |
| ByeDPI / ProxiFyre / drover | **Hayır** (tanı + temizle) | SplitWire'a yönlendir |
| WireSock / WARP tünelleme | **Hayır** (tanı + temizle) | SplitWire'a yönlendir |
| Discord onarımı ("Checking for updates") | **Hayır** | SplitWire "Onarım" sayfası |

**Gerekçe:** SplitWire açık kaynak (MIT), aktif bakımlı ve bu işi kapsamlı yapıyor. Aynısını yeniden yazmak emek ve **çekirdek sürücüsü dağıtma sorumluluğu** getirir. FoxFix'in farkı teşhis ve açıklamadır, ikisi rakip değil tamamlayıcıdır.

### 16.2 GoodbyeDPI modülü (küçük ve güvenli)
- **Varsayılan profil:** `-5` (DNS yönlendirmesiz, TTL'siz). README'ye göre TTL'li profiller bazı sitelerde yavaşlık yapabiliyor.
- **Blacklist modu (varsayılan AÇIK):** GoodbyeDPI yalnızca seçili alan adları için çalışsın. SplitWire README'si aynı yaklaşımı varsayılan olarak Discord, Roblox ve Wattpad için uyguluyor ve bunun "bazı sitelerde bağlantı sorunlarını" azalttığını belirtiyor. GoodbyeDPI'da bunun için `--blacklist <dosya>` seçeneği var, **Claude Code: indirdiğin sürümde `goodbyedpi.exe --help` ile seçeneğin adını ve söz dizimini doğrula, tahmin etme.**
  - Varsayılan liste: `discord.com`, `discord.gg`, `discordapp.com`, `discordapp.net`, `discord.media`, `roblox.com`, `rbxcdn.com`, `rbxinfra.net`, `rbxtrk.com`. Kullanıcı düzenleyebilsin.
- **Çalıştırma modu:** "Elle başlat/durdur" varsayılan. "Servis olarak kur" gelişmiş ayar, uyarıyla.
- **DNS yönlendirmeli profiller** (`--dns-addr ...`) **gizli**, "gelişmiş" arkasında, uyarıyla (Bölüm 14.1).
- Her başlatmadan sonra **otomatik DNS testi** (UDP + TCP). Başarısızsa profili hemen durdur ve açıkla.
- **Çakışma kontrolü:** SplitWire'ın GoodbyeDPI/Zapret hizmeti ya da başka bir WinDivert kullanıcısı çalışıyorsa FoxFix kendi GoodbyeDPI'ını başlatmasın, nedenini göstersin.
- İkili dosya kaynağı: Bölüm 15 (hibrit indirme + sabitlenmiş hash + çevrimdışı yedek).

### 16.3 SplitWire-Turkey ile ilişki
**Kural: FoxFix, SplitWire'ın kurduklarını kendi başına silmez, önce SplitWire'ın kendi kaldırma yollarını önerir.**

1. **Tespit:** `C:\Program Files\SplitWire-Turkey\` ve `%LocalAppData%\SplitWire-Turkey\` varlığı, kaldırma paketi `unins000.exe`, kurulu hizmetler.
2. **Kullanıcıya göster:** "SplitWire-Turkey kurulu. Bu araç şunları değiştirir: ..." (hizmet listesi, DNS durumu).
3. **Kaldırma önerileri (sırayla):**
   - SplitWire'ı aç → **Gelişmiş → Tüm Hizmetleri Kaldır** → **DNS ve DoH Ayarlarını Geri Al**
   - Veya Windows "Program ekle/kaldır"dan **SplitWire-Turkey'i kaldır**
   - Veya FoxFix, `unins000.exe`'yi **kullanıcının onayıyla** çalıştırır
4. **Yedek yol:** SplitWire bozuksa (açılmıyor, kaldırıcısı yoksa) ve kullanıcı açıkça onaylarsa FoxFix kendi allowlist'iyle kalıntıları temizler.
5. **"Bypass'a ihtiyacım var" akışı:** FoxFix **SplitWire'ı kendisi indirip kurmaz.** Resmi sürüm sayfasını **tarayıcıda açar** (`github.com/cagritaskn/SplitWire-Turkey/releases`). Kurulum güveni SplitWire'ın kendi sorumluluğunda kalır. Bu, FoxFix'in indirdiği kodu doğrulama yükünü de ortadan kaldırır.

### 16.4 FoxFix'in tanıyıp raporlaması gereken yeni kalıntılar
**Kesin adları `cagritaskn/SplitWire-Turkey/src` içinden oku**, aşağıdakiler README'den çıkarıldı, hizmet/görev adlarını tahmin etme:
- `C:\Program Files\SplitWire-Turkey\res\byedpi\ciadpi.exe` ve `...\res\proxifyre\ProxiFyre.exe` (süreçler ve güvenlik duvarı kuralları)
- WireSock hizmeti(leri) ve "WireSock Refresh" **Görev Zamanlayıcı görevi** (yineleyici)
- DnsCrypt-Proxy hizmeti, DNS = `127.0.0.1` / `::1` durumu
- Discord klasöründe `drover` dosyaları (`%LocalAppData%\Discord\...`). **FoxFix bunları kendi başına silmesin**, SplitWire'ın "kaldır" yoluna yönlendirsin.
- Discord `Update.exe` için uygulanmış Windows 7/8 uyumluluk modu ayarı (SplitWire'ın sorun giderme rehberi öneriyor), kullanıcı bunu yaptıysa rapora "bilgi" olarak yaz.
- Kaspersky varlığı (WinDivert'i engelliyor), SplitWire ve GoodbyeDPI README'leri bunu belirtiyor.

### 16.5 DNS tutarlılığı (bilinen tehlike)
SplitWire, "DNS Yöntemi" anahtarına göre ya DnsCrypt-Proxy ile DNS'i `127.0.0.1`'e çevirir ya da Windows DoH (Google + Quad9, Windows 11) kullanır. **İkisi asla aynı anda kullanılmaz.** FoxFix:
- DNS `127.0.0.1` ve `dnscrypt-proxy` servisi **yoksa** → "DNS zehirlenmesi" tanısı, DHCP'ye geri al.
- DNS `127.0.0.1` ve `dnscrypt-proxy` servisi **çalışıyorsa** → bilinçli bir yapılandırma olabilir, kullanıcıya sor, kendi DNS ayarını **üstüne yazma**.
- FoxFix kendi DoH ayarını yapmadan önce başka bir aracın DNS ayarı olup olmadığını denetlesin ve çakışmayı bildirsin.

### 16.6 Güncellenmiş yol haritası (Bölüm 8'in yerine geçer)
**v0.1: Çekirdek**
- [ ] İskelet, MVVM, DI, loglama, tema, ana pencere
- [ ] `ICommandRunner`, `ICheck`, `IFix`, `UndoJournal`, geri yükleme noktası
- [ ] Teşhis katmanları 1-6 + DNS karar ağacı (UDP/TCP/DoH)
- [ ] DPI kalıntı tespiti ve temizliği: GoodbyeDPI, WinDivert, `monkey`, `winws*`, dnscrypt; **SplitWire tespiti** (yalnızca bildirir, dokunmaz)
- [ ] "Beni Düzelt" + rapor + yeniden başlatma sonrası devam (RunOnce)

**v0.2: DNS ve GoodbyeDPI**
- [ ] DNS sağlayıcı seçimi + hız testi + DoH (`autoupgrade=yes` söz dizimi)
- [ ] GoodbyeDPI modülü (16.2): hibrit indirme, hash sabitleme, blacklist, otomatik DNS testi
- [ ] SplitWire yönlendirme akışı (16.3)
- [ ] Erişim testi (Discord, Roblox vb.)

**v0.3: Windows bakım** (Bölüm 5.5)

**v1.0: Yayın** (Bölüm 8'deki liste)

**Ertelendi / kapsam dışı:** Zapret entegrasyonu, strateji sihirbazı, hostlist düzenleyici, Discord onarımı, WireSock/ByeDPI yönetimi.

### 16.7 Lisans ve atıf
- SplitWire-Turkey **MIT**: fikirleri ve yaklaşımları kullanabiliriz. **Kod kopyalarsak** telif ve lisans notunu `THIRD-PARTY-NOTICES.md`'ye ekle.
- Mümkünse kod kopyalamak yerine kendi uygulamanı yaz, ve `README`'de SplitWire'a, GoodbyeDPI'a (ValdikSS, ve Turkey fork'u için cagritaskn), WinDivert'e (basil00) **atıf** ver.
- SplitWire geliştiricisine ulaşmayı düşün: FoxFix'in onunla çakışmasını önlemek (tespit/kaldırma yolları) için isimlendirmeleri doğrulamak faydalı olur.

---

## 17. Güvenilirlik (v2'de eklendi, Bölüm 4 ve 16 ile birlikte önceliklidir)

**Hedef "hatasız" değil, "güvenli başarısızlık"tır:** FoxFix bir şey ters giderse bilgisayarı kötü durumda bırakmaz, yaptığını geri alır ve kullanıcıya ne olduğunu söyler.

### 17.1 Otomatik geri dönüşlü onay
DNS, proxy ve servis değişikliklerinden sonra FoxFix bağlantıyı test eder (ping + DNS + HTTPS). Test başarısızsa **değişikliği kendisi geri alır**. Başarılıysa kullanıcıya "Her şey çalışıyor mu?" sorar. 20 sn içinde onay gelmezse yine geri alır (Windows ekran çözünürlüğü onayı mantığı). Geri alma zamanlayıcısı, uygulama çökse bile çalışsın diye ayrı bir zamanlanmış görev olarak da kurulur.

### 17.2 İki aşamalı yetki
- **Teşhis modu:** Yönetici yetkisi **istemez**, yalnızca okur. Uygulama normal kullanıcı olarak açılır.
- **Düzeltme:** Kullanıcı "Düzelt" deyince yükseltilmiş ikinci bir süreç başlar (UAC). Böylece çoğu kullanım korkutan bir izin penceresi olmadan başlar. Bölüm 3'teki `requireAdministrator` kararı bu şekilde değişir.
- Yeniden başlatma sonrası devam için `RunOnce` yerine **en yüksek ayrıcalıkla tek seferlik zamanlanmış görev** kullan (`RunOnce` yükseltilmiş uygulamayı güvenilir açmaz).

### 17.3 Çökmeye dayanıklı ve tekrar çalıştırılabilir adımlar
- Her `IFix` **idempotent** olsun (iki kez çalışınca zarar vermesin).
- İlerleme `%LocalAppData%\FoxFix\state.json` içine adım adım yazılsın. Elektrik kesilse ya da uygulama kapansa, bir sonraki açılışta "Şu adıma kadar geldim: devam edeyim mi, geri alayım mı?" sorulur.

### 17.4 Silmeden önce yedek
Bir servis, sürücü veya kayıt defteri girdisi silinmeden önce: `sc qc` çıktısı, ilgili kayıt defteri dalı (`.reg` dışa aktarımı) ve DNS ayarları yedeklenir. Bölüm 4'teki "geri al" bu yedeklerle gerçekten servisi geri getirebilir. Geri yükleme noktası: `Checkpoint-Computer` 24 saatte bir noktayla sınırlıdır ve Sistem Koruması kapalıysa çalışmaz. Bu iki durumu ayrı mesajlarla yönet.

### 17.5 Test stratejisi
1. **Gerçek çıktı fixtures:** Türkçe ve İngilizce Windows'tan alınmış gerçek `sc`, `ipconfig`, `nslookup`, `netsh` çıktıları `tests/fixtures/` içinde, ayrıştırıcılar bunlarla test edilir. Kullanıcı yoksa tahmini çıktıyla test yazılmaz, fixture elle alınır.
2. **Zararsız sahte servis:** Adı `goodbyedpi` olan, sadece bekleyen sahte bir servis ve sahte `.exe` ile temizlik mantığı, gerçek sürücü kurmadan test edilir.
3. **Gerçek WinDivert testleri yalnızca VM anlık görüntüsünde** (Bölüm 9). Geliştirme makinesinde riskli adım çalıştırılmaz.
4. **CI:** GitHub Actions `windows-latest` üzerinde derleme + birim testleri. Servis/sürücü testleri CI'da çalışmaz.
5. **Kapalı beta:** Yayından önce 5-10 kişiyle; farklı ISS, Win10/Win11, Türkçe/İngilizce Windows.

### 17.6 Kapsam disiplini
- v0.1 kapsamı **yalnızca**: teşhis, DPI/Zapret/SplitWire kalıntı tespiti ve temizliği, DNS onarımı, rapor. GoodbyeDPI yöneticisi v0.2, Windows bakım v0.3.
- Her sürüm bir öncekinin testleri yeşil olmadan başlamaz.

### 17.7 Güven işaretleri
Kod imzalama (SignPath), yayında SHA-256 özeti, açık kaynak, antivirüs yanlış alarmı için Defender'a gönderim (Bölüm 8 v1.0 maddeleri).

### 17.8 Claude Code için ek kural
"Çalışıyor" demeden önce: derle, birim testlerini çalıştır, sonucu göster. Windows'ta VM'de denenmemiş her fix için "elle test edilmeli" yaz (Bölüm 11, madde 9).
