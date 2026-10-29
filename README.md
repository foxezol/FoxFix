# FoxFix

Windows'ta internet, DNS ve DPI sorunlarına bakan, bulduğu sorunu önce göstererek düzelten bir araç. Arayüz Türkçe. Veri toplamaz, hiçbir yere bir şey göndermez.

> **Durum:** FoxDPI (DPI aşma kısmı) test edildi ve sorunsuz çalışıyor. Diğer özellikler tam olarak test edilmedi, deneysel say. Önemli bir şeyi değiştirmeden önce önizlemeyi oku, Geri Al'ı unutma.

## Neye dayanıyor

FoxFix'in DPI kısmı kendi DPI aracı değildir. Çalışan iş [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) ile onun Türkiye sürümü [GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey) (0.2.3rc3) tarafından yapılır. İkinci yöntem olarak [Zapret](https://github.com/bol-van/zapret) (`winws`) var; dosyaları ve sağlayıcıya göre hazır ayarları [SplitWire-Turkey](https://github.com/cagritaskn/SplitWire-Turkey) 1.5.7'den alındı. FoxFix bunları uygulamanın içinde taşır ve üstüne bir arayüz koyar: senin internetinde hangi ayarın çalıştığını dener, seçilen ayarı çalıştırır, her şeyi geri alabilir. Asıl emek o projelerin sahiplerinindir.

> Zapret yöntemi yeni eklendi ve henüz gerçek bir bilgisayarda denenmedi. GoodbyeDPI yöntemi test edildi.

FoxFix, Claude Code ile "vibe coding" yöntemiyle yazıldı. Amaç, bu araçları kullanmayı kolaylaştırmak ve seçenekleri çeşitlendirmek.

Made by foxezol, Claude Code

## Ne yapar

- **Bağlantı kontrolü:** adaptör, IP, ping, DNS ve HTTPS'i sırayla dener, neyin bozuk olduğunu söyler.
- **DNS ve DPI:** hazır ayarları senin internetinde dener, çalışanı bulur. Yöntem olarak GoodbyeDPI ya da Zapret seçilir. İki yöntemi de hizmet olarak kurabilir ya da FoxDPI penceresiyle kurmadan çalıştırabilir. Zapret'in seçili ayarı elle düzenlenebilir. Windows açılınca kendiliğinden de başlatabilir.
- **Erişim testi:** yazdığın sitelerin açılıp açılmadığını gösterir. FoxDPI açıkken ve kapalıyken çalıştırıp farkı görebilirsin.
- **Bilgisayarı hızlandır:** başlangıç uygulamalarını ve temizlenebilir dosyaları listeler, seçtiklerini kaldırır.
- **DPI Sıfırlama:** eski GoodbyeDPI, WinDivert ve Zapret kalıntılarını ve elle girilmiş DNS'i silip Windows'un kendi ayarına döner.
- **Rapor:** sorun anlatırken paylaşabileceğin bir metin üretir. Varsayılan olarak kullanıcı adı, bilgisayar adı, MAC ve IP adresleri gizlenir. Bilinen genel DNS adresleri (1.1.1.1, 8.8.8.8 gibi) ve 127.0.0.1 teşhis için kalır. İstersen "Kişisel bilgilerimi de göster" ile açabilirsin.

Sistemde bir şey değişecekse önce ne değişeceği gösterilir, sen onaylamadan uygulanmaz. Yapılanlar kaydedilir ve Geri Al ile geri alınabilir.

## İndirme ve kurulum

[Releases](../../releases) sayfasında iki paket var:

| Paket | Ne zaman |
|---|---|
| **FoxFixSetup.exe** | Normal kurulum. Kurulum klasörünü sen seçersin (varsayılan `Program Files\FoxFix`). Kurulumda "Verileri kurulum klasöründe tut" kutusunu işaretlersen veriler de oraya yazılır. |
| **FoxFix-…-tasinabilir.zip** | Kurulum yok. Bir klasöre çıkar, çalıştır. Her şey (günlük, geri alma kaydı, GoodbyeDPI kopyası) çıkardığın klasördeki `Veri` klasörüne yazılır. Klasörü silince iz kalmaz. |

Kurulumsuz kullanımda (ZIP) `FoxFix.portable` dosyası `FoxFix.exe`'nin yanında durmalı, bu dosya "verileri buraya yaz" demektir. Setup'ta kutuyu işaretlemezsen veriler `%LocalAppData%\FoxFix` ve `%ProgramData%\FoxFix` altına gider.

Windows SmartScreen imzasız uygulamalarda uyarı verebilir. "Ek bilgi", sonra "Yine de çalıştır" de. İstersen kaynak koddan kendin derleyebilirsin.

## Kullanım

1. `FoxFix.exe` ile `FoxDPI.exe` aynı klasörde dursun.
2. İlk seferde `FoxFix.exe`'yi aç. FoxDPI.exe, FoxFix bir kez açılmadan çalışmaz.
3. Sonrası için FoxDPI.exe yeter. FoxDPI penceresi açık olduğu sürece DPI açıktır, pencereyi kapatınca kapanır.
4. İstersen DNS ve DPI sayfasındaki "FoxDPI'yı Windows açılınca başlat" kutusunu işaretle. Hizmet kurulmaz, zamanlanmış bir görev eklenir.

İkisi de yönetici izni ister. Windows 10 ve 11 (64 bit) için. Çalıştırmak için ayrıca .NET kurmak gerekmez.

## Derleme

.NET 10 SDK gerekir.

```
dotnet build FoxFix.sln
dotnet test FoxFix.sln
powershell -File tools\paketle.ps1
```

Son komut `dist` klasörüne ZIP paketini koyar. Bilgisayarda [Inno Setup 6](https://jrsoftware.org/isinfo.php) kuruluysa `FoxFixSetup.exe` de çıkar.

## Elle denenmesi gerekenler

Birim testleri sistem komutlarını taklit eder. Şunlar gerçek bir Windows'ta elle denenmeden güvenilmemeli:

- Hizmet kurma ve kaldırma, DPI Sıfırlama (sanal makine anlık görüntüsünde dene).
- Zapret yöntemi: ayarları deneme, FoxDPI penceresiyle ve hizmet olarak çalıştırma, ayarı elle düzenleme, DPI Sıfırlama ile kaldırma. Zapret hizmeti `sc create` ile kuruluyor; winws.exe bir Windows hizmeti gibi davranmayabilir.
- Windows açılınca başlat kutusu: `schtasks` ile oluşturulan görevin gerçekten girişte çalıştığı.
- Kurulum paketi (`FoxFixSetup.exe`): kurulum, klasör seçimi, taşınabilir veri kutusu, kaldırma.

Adımlar için `VM-TEST-REHBERI.md` dosyasına bak.

## Lisanslar

FoxDPI, GoodbyeDPI-Turkey'in 0.2.3rc3 sürümünü, Zapret'i (`winws`, `cygwin1.dll`) ve ikisinin kullandığı WinDivert sürücüsünü uygulamanın içinde taşır. Hepsi kendi lisanslarıyla dağıtılır: `src/FoxFix.App/Assets/GoodbyeDpi/LICENSE-goodbyedpi.txt`, `LICENSE-windivert.txt` ve SplitWire-Turkey'in MIT lisansı `src/FoxFix.App/Assets/Zapret/LICENSE-splitwire-turkey.txt` (paketlerde `Licenses` klasöründe). Zapret'in kendi lisansı için [projesine](https://github.com/bol-van/zapret) bak.

## Geri bildirim

[Discord](https://discord.com/users/381533632852131841)
