# FoxFix: Sanal Makinede Gerçek Test Rehberi

Bu rehber, otomatik testlerin **kanıtlayamadığı** kısımları denemek içindir: gerçek servis/sürücü silme ve kurma, DNS değişikliği, geri alma ve yeniden başlatma. Otomatik testler (275 adet) bunların mantığını sahte bir Windows'la doğruladı, gerçek `sc.exe`, `netsh.exe`, kayıt defteri ve `shutdown.exe` ile **hiç denenmedi**.

> **Altın kural:** Bu testleri asla kendi bilgisayarında yapma. Hepsi sanal makinede (VM), anlık görüntü (snapshot) alınmış hâlde yapılır. Bir şey ters giderse anlık görüntüye dönersin, hiçbir şey kaybetmezsin.

---

## 0. Hazırlık

### 0.1 Sanal makine
- **Windows 11** VM (asıl test). Mümkünse bir de **Windows 10 22H2** VM.
- Hyper-V, VMware ya da VirtualBox olur. Ağ: **NAT** ya da köprü, internet çıkışı olmalı.
- Windows'u kur, güncelle, **bir tane Ethernet bağdaştırıcısı** olsun (testler adaptör adına bakar).
- VM'de yerel yönetici hesabı kullan (UAC açık kalsın, UAC testi de var).

### 0.2 Anlık görüntüler (snapshot)
Şu adlarla al, her büyük adımdan önce geri dönebilmek için:

| Ad | Ne zaman |
|---|---|
| `01-temiz` | Windows kurulu, güncel, FoxFix yok |
| `02-gdpi-kurulu` | GoodbyeDPI servis olarak kurulu (bölüm 2) |

### 0.3 FoxFix'i VM'ye taşı
Geliştirme bilgisayarında (proje klasöründe):

```powershell
dotnet publish src/FoxFix.App -c Release -r win-x64 --self-contained true -o publish
```

`publish` klasörünü VM'ye kopyala (paylaşımlı klasör ya da USB). Not: Bu komutu ben henüz denemedim. Sorun çıkarsa yedek yol: VM'ye ".NET 10 Desktop Runtime" kur ve `src\FoxFix.App\bin\Debug\net10.0-windows\` klasörünü kopyala.

> Yayın derlemesinde **trim/AOT kullanma**: WMI (servis okuma) bunlarla çalışmıyor.

### 0.4 Test araçları
VM'de bir **yönetici PowerShell** ve bir **yönetici Komut İstemi** açık dursun. Adaptör adını öğren:

```powershell
Get-NetAdapter | Where-Object Status -eq Up | Select-Object Name, InterfaceDescription
```

Bu rehberde adaptör adı **`Ethernet`** diye yazıldı. Seninki farklıysa komutlarda değiştir.

### 0.5 "Önceki durum" kaydı
Her testten önce şunları bir metin dosyasına kaydet, sonra karşılaştıracaksın:

```powershell
ipconfig /all > C:\test\ipconfig-once.txt
netsh interface ip show dns > C:\test\dns-once.txt
sc query type= driver state= all | findstr /i "divert monkey" > C:\test\surucu-once.txt
```

---

## 1. Temel: uygulama açılıyor mu, teşhis doğru mu

**Snapshot:** `01-temiz`

1. `FoxFix.App.exe`'yi çift tıkla (yönetici olmadan).
2. **Beklenen:** Pencere açılır açılmaz kontrol kendiliğinden başlar ("Bağlantın kontrol ediliyor…"). Solda altı sayfalık menü, sağda tilki logosu, "Beni Düzelt" ve "Yeniden kontrol et" düğmeleri, altında bağlantı hattı (altı adım) görünür. Tarayıcı eklenti düğmeleri **DNS ve DPI** sayfasındadır.
3. Sonuçları gör ya da **Yeniden kontrol et**'e bas. **Beklenen (temiz VM):**
   - Ağ bağlantısı ✔, Modemden adres alma ✔, Modem ve internet hattı ✔, DNS ✔, HTTPS ✔, DPI kalıntıları ✔ ("Bilinen bir DPI aracı ya da kalıntısı bulunmadı").
   - Üstte büyük yazıyla "Her şey yolunda".
4. **Beni Düzelt**'e bas. **Beklenen:** "Temizlenecek bir şey yok." ve "DNS ayarında düzeltilecek bir şey yok." satırları, **Uygula düğmesi yok**. **Kapat**'a bas.

| Kontrol | Sonuç |
|---|---|
| Pencere açıldı | ☐ |
| Teşhis tüm katmanlar ✔ | ☐ |
| Beni Düzelt "bir şey yok" dedi, Uygula yok | ☐ |

---

## 2. GoodbyeDPI'ı VM'ye kur (silinecek şey bu)

**Snapshot:** `01-temiz` üzerinden devam et.

1. GoodbyeDPI Türkiye sürümü klasörünü (`goodbyedpi-0.2.3rc3-turkey`) VM'ye kopyala, örneğin `C:\gdpi\` altına.
2. Klasörde `service_install` ile başlayan ve `dnsredir` içeren `.cmd` dosyasını **yönetici olarak** çalıştır (varsayılan profil: `--dns-addr 77.88.8.8 --dns-port 1253`).
3. Doğrula:

```cmd
sc query GoodbyeDPI
sc query WinDivert
tasklist | findstr /i goodbyedpi
sc qc GoodbyeDPI > C:\test\gdpi-qc-once.txt
sc qc WinDivert  > C:\test\windivert-qc-once.txt
```

**Beklenen:** `GoodbyeDPI` servisi RUNNING, `WinDivert` sürücüsü RUNNING, `goodbyedpi.exe` işlemi var.

4. **Snapshot al: `02-gdpi-kurulu`.**
5. FoxFix'te **Yeniden kontrol et**. **Beklenen:**
   - DNS satırında ⚠ "DNS istekleri başka bir sunucuya yönlendiriliyor" (tuzak sorgu `192.0.2.1` yanıt verir).
   - DPI kalıntıları satırında ⚠ ve ayrıntıda: "GoodbyeDPI şu an çalışıyor", "WinDivert sürücüsü şu an çalışıyor", **"GoodbyeDPI'ın DNS yönlendirmesi açık: ... 77.88.8.8 (port 1253)"**.

| Kontrol | Sonuç |
|---|---|
| Servis, sürücü, işlem çalışıyor | ☐ |
| Teşhis DNS yönlendirmesini ⚠ olarak gösterdi | ☐ |
| Teşhis 77.88.8.8 (port 1253) adresini yazdı | ☐ |

---

## 3. Temizleme: önizleme, UAC, uygulama

**Snapshot:** `02-gdpi-kurulu`

### 3.1 Önizleme
1. **Beni Düzelt**'e bas (hâlâ yönetici değilsin).
2. **Beklenen önizleme:**
   - `Durdurulup silinecek: GoodbyeDPI (...goodbyedpi.exe)`
   - `Durdurulup silinecek: WinDivert (\??\...WinDivert64.sys)`
   - `Kapatılacak işlem: goodbyedpi`
   - "Silinen hizmetler Geri Al ile döner. Kapatılan işlemler geri gelmez."
   - "Sürücünün bellekten inmesi için temizlikten sonra bilgisayarı yeniden başlatmak gerekecek."
   - `ℹ Bu değişiklikler için yönetici izni gerekiyor.`
   - Onay kutusu, **"Temizlik bitince bilgisayarı kendiliğinden yeniden başlat"** kutusu, **"Yönetici olarak uygula"** düğmesi (onay işaretlenene kadar soluk).
3. **Hiçbir şeyin değişmediğini doğrula:** `sc query GoodbyeDPI` hâlâ RUNNING.

### 3.2 UAC yeniden başlatma
1. Onay kutusunu işaretle. "Yönetici olarak uygula" etkinleşir.
2. Önce UAC'yi **reddet** ("Hayır").
   **Beklenen:** "Yönetici izni verilmedi. Hiçbir şey değişmedi." Servis hâlâ RUNNING.
3. Tekrar bas, bu sefer UAC'yi **onayla**.
   **Beklenen:** Uygulama kapanıp yönetici olarak yeniden açılıyor. (Eski pencere kapanır.)
4. Yeni pencerede **Beni Düzelt**'e bas. Önizleme aynı. Artık düğme **"Uygula"** yazıyor, "Yönetici olarak uygula" değil, ve yönetici notu yok.

| Kontrol | Sonuç |
|---|---|
| Önizleme doğru, sistem değişmedi | ☐ |
| UAC reddi: değişiklik yok, mesaj var | ☐ |
| UAC onayı: yönetici olarak yeniden açıldı | ☐ |
| Yönetici pencerede düğme "Uygula" | ☐ |

### 3.3 Uygulama (kendiliğinden yeniden başlatma KAPALI)
1. Onay kutusunu işaretle, **kendiliğinden yeniden başlat kutusunu işaretleme**.
2. **Uygula**.
3. **Beklenen:**
   - Sonuç: "N öğe temizlendi." Sürücü bellekten inmediyse sonuna "Bitirmek için bilgisayarı yeniden başlat."
   - Turuncu "Yeniden başlatma gerekiyor" kutusu ve **"Şimdi yeniden başlat"** düğmesi (sürücü inmediyse).
   - "Yapılan değişiklikler (geri alınabilir)" kutusunda `GoodbyeDPI hizmeti ...` ve `WinDivert hizmeti ...` kayıtları.
4. **Gerçekten silindi mi?**

```cmd
sc query GoodbyeDPI
sc query WinDivert
tasklist | findstr /i goodbyedpi
```

- `sc query GoodbyeDPI` → **`[SC] EnumQueryServicesStatus:OpenService FAILED 1060`** (hizmet yok) beklenir.
- `WinDivert` → ya 1060 (silindi) ya da "silinmek üzere işaretli" (STOP_PENDING / 1072). İkincisi **normal**, yeniden başlatma bunu çözer.
- `goodbyedpi.exe` işlemi yok olmalı.

5. **Geri alma kaydı yazıldı mı?**

```powershell
Get-Content "$env:LOCALAPPDATA\FoxFix\undo.json"
```

Dosyada her hizmet için `name`, `binaryPath`, `startType`, `serviceType`, `isDriver`, `wasRunning` alanları olmalı. `wasRunning` GoodbyeDPI için `True`.

> Uyarı: Uygulamayı **farklı bir yönetici hesabıyla** yükseltirsen `%LocalAppData%` farklı olur ve kayıt başka klasörde görünür. Aynı kullanıcıyla yükselt.

| Kontrol | Sonuç |
|---|---|
| Sonuç mesajı doğru | ☐ |
| `sc query GoodbyeDPI` → 1060 | ☐ |
| `WinDivert` silindi ya da silinmek üzere işaretli | ☐ |
| `goodbyedpi.exe` işlemi yok | ☐ |
| `undo.json` dolu, alanlar doğru | ☐ |
| Uygulamada "Yapılan değişiklikler" listesi var | ☐ |

---

## 4. Yeniden başlatma akışı

> **Önemli:** "Yeniden başlatma gerekiyor" kutusu yalnızca temizlik sonucunda **gerçekten** gerekirse çıkar (sürücü bellekten inmediyse ya da Windows silmeyi yeniden başlatmaya bıraktıysa). Sürücü temiz inerse kutu **hiç çıkmaz** ve kendiliğinden yeniden başlatma da tetiklenmez. Bu bir hata değil. O durumda yeniden başlatma akışını denemek için bölüm 10.1'deki yöntemle sürücüyü takılı bırak.

### 4.1 Elle başlatma + iptal
1. (Bölüm 3.3'ten sonra yeniden başlatma kutusu çıktıysa) **"Şimdi yeniden başlat"**'a bas.
2. **Beklenen:** "Bilgisayar 10 saniye sonra yeniden başlayacak..." mesajı, turuncu "Yeniden başlatma planlandı" kutusu, Windows'un kendi geri sayım bildirimi.
3. Geri sayım bitmeden **"Yeniden başlatmayı iptal et"**'e bas.
   **Beklenen:** "Yeniden başlatma iptal edildi...", Windows'un bildirimi kaybolur, bilgisayar yeniden **başlamaz**. Tekrar "Yeniden başlatma gerekiyor" kutusu görünür.
4. Alternatif doğrulama (uygulamayı kullanmadan): yönetici komut isteminde `shutdown /a` bir "iptal edilecek yeniden başlatma yok (1116)" hatası verir, yani iptal gerçekten yapıldı.
5. Şimdi **"Şimdi yeniden başlat"**'a bas ve bu kez bırak: bilgisayar **yeniden başlamalı**.

### 4.2 Kendiliğinden yeniden başlatma
**Snapshot:** `02-gdpi-kurulu` (bölüm 3'ü sıfırdan yap)

1. Yönetici olarak aç, **Beni Düzelt**.
2. Hem onay kutusunu **hem** "kendiliğinden yeniden başlat" kutusunu işaretle, **Uygula**.
3. **Beklenen:** Temizlik bitince 20 saniyelik geri sayım başlıyor, mesajda "20 saniye sonra yeniden başlayacak". İlk denemede **iptal et** ve bilgisayarın açık kaldığını gör.
4. Sonra bir kez de iptal etmeden başlat: bilgisayar yeniden başlamalı.

### 4.3 Yeniden başlatma sonrası
1. Açıldıktan sonra:

```cmd
sc query WinDivert
dir C:\gdpi
```

- `WinDivert` → **1060** (artık yok).
- `C:\gdpi` klasörünü **silmeyi dene**: artık kilitli olmamalı (`WinDivert64.sys` serbest).

2. FoxFix'i aç. **Beklenen:** "Yapılan değişiklikler (geri alınabilir)" kutusu, **önceki oturumdan** kalan kayıtlarla dolu geliyor (Geri Al kalıcı).

| Kontrol | Sonuç |
|---|---|
| Elle başlat: geri sayım göründü | ☐ |
| İptal: bilgisayar yeniden başlamadı | ☐ |
| Başlatma: bilgisayar yeniden başladı | ☐ |
| Otomatik tik: 20 sn geri sayım, iptal çalıştı | ☐ |
| Otomatik tik: iptal etmeyince yeniden başladı | ☐ |
| Yeniden başlatma sonrası WinDivert yok, klasör silinebiliyor | ☐ |
| Geri Al kayıtları oturum sonrası duruyor | ☐ |

---

## 5. Geri alma (temizlemeyi)

**Snapshot:** `02-gdpi-kurulu`, bölüm 3.3'ü uygula (yeniden başlatma yapma ya da yap, ikisini de dene).

1. "Yapılan değişiklikler" kutusunda **GoodbyeDPI** kaydının **Geri Al**'ına bas.
2. **Beklenen:** "Geri alındı: ..." mesajı, kayıt listeden kalkar.
3. Doğrula ve eski kayıtla **karşılaştır**:

```cmd
sc qc GoodbyeDPI > C:\test\gdpi-qc-sonra.txt
fc C:\test\gdpi-qc-once.txt C:\test\gdpi-qc-sonra.txt
sc query GoodbyeDPI
```

   - `BINARY_PATH_NAME`, `START_TYPE` (auto), `DISPLAY_NAME` **aynı** olmalı.
   - Hizmet önceden çalışıyorsa **RUNNING** olmalı, çalışmıyorsa durmuş kalmalı.
4. **WinDivert** kaydını da geri al. `sc qc WinDivert` ile `windivert-qc-once.txt`'i karşılaştır. Tür `KERNEL_DRIVER` olmalı, yol `\??\...WinDivert64.sys`.
   - **Beklenen tuhaflık:** GoodbyeDPI başlayınca WinDivert hizmetini kendisi de oluşturabilir. Bu yüzden WinDivert'i geri alırken "geri kurulamadı" (hizmet zaten var, hata 1073) çıkabilir. Bu bir hata sayılmaz ama **not et**; kayıt listede kalır, sorun değil. Önce WinDivert'i, sonra GoodbyeDPI'ı geri almayı da dene ve farkı yaz.
5. **Beklenmeyen durumlar (hatalar not et):**
   - "geri kurulamadı" mesajı çıkarsa: kayıt listede **kalmalı**, tekrar denenebilmeli.
   - Hizmet var olan bir adla çakışırsa (zaten kuruluysa) aynı hata beklenir.

> **Not:** `sc create` ile yeniden oluşturma, ayarların tamamını (bağımlılıklar, açıklama, kurtarma ayarları) geri getirmez. Yalnızca ad, yol, başlangıç türü, tür ve görünen ad. Fark görürsen not et, bilinen bir sınırlama.

| Kontrol | Sonuç |
|---|---|
| GoodbyeDPI geri alındı, `sc qc` aynı | ☐ |
| Eskiden çalışıyorsa tekrar çalışıyor | ☐ |
| WinDivert geri alındı (KERNEL_DRIVER) | ☐ |
| Geri alınan kayıt listeden kalktı | ☐ |
| Başarısız geri alma kaydı sildirmedi | ☐ |

---

## 6. Sınır testleri: FoxFix neye **dokunmamalı**

**Snapshot:** `01-temiz`. Bunlar sahte hizmetlerdir, gerçek bir şeye zarar vermez. Her testten sonra elle sil.

### 6.1 Benzer ad, ilgisiz program
```cmd
sc create monkey binPath= "C:\Windows\System32\notepad.exe"
```
FoxFix → Teşhis/Beni Düzelt. **Beklenen:** `monkey` **hiç listelenmez**. (Adı "monkey" ama dosyası `Monkey64.sys` değil.)

### 6.2 SplitWire-Turkey'e ait her şey
```cmd
mkdir "C:\Program Files\SplitWire-Turkey\res\x"
copy C:\gdpi\x86_64\goodbyedpi.exe "C:\Program Files\SplitWire-Turkey\res\x\goodbyedpi.exe"
sc create SWTest binPath= "\"C:\Program Files\SplitWire-Turkey\res\x\goodbyedpi.exe\" -5"
```
**Beklenen:** Beni Düzelt önizlemesinde `Atlanacak, SplitWire-Turkey'e ait. Onun kaldırıcısını kullan: SWTest` satırı. Başka temizlenecek bir şey yoksa **Uygula düğmesi hiç çıkmaz**, `SWTest` silinemez. (Daha sağlam deneme: aynı anda bölüm 2'deki gerçek GoodbyeDPI de kuruluyken uygula, `SWTest` ayakta kalmalı.) Test sonrası: `sc delete SWTest` ve `rmdir /s "C:\Program Files\SplitWire-Turkey"`.

### 6.3 Yalnızca ad eşleşmesi ("emin değilim")
```cmd
sc create WinDivert14 type= kernel binPath= C:\Windows\System32\drivers\null.sys
```
**Beklenen:** `Atlanacak, başka bir programa ait olabilir: WinDivert14`. Silinmemeli. Sonra: `sc delete WinDivert14`.

### 6.4 Yanlış alarm yok
Hiçbir şey kurmadan Beni Düzelt: yalnızca "bir şey yok" mesajları. `Dnscache`, `wuauserv`, `Npcap` (kuruluysa) listelenmemeli.

| Kontrol | Sonuç |
|---|---|
| `monkey` (notepad) hiç çıkmadı | ☐ |
| SplitWire altındaki hizmet atlandı, silinmedi | ☐ |
| Yalnızca ad eşleşen sürücü atlandı | ☐ |
| Temiz VM'de yanlış alarm yok | ☐ |

---

## 7. DNS onarımı (127.0.0.1)

**Snapshot:** `01-temiz`

1. Elle bozuk DNS ayarla (DnsCrypt artığı taklidi):

```cmd
netsh interface ip set dns "Ethernet" static 127.0.0.1
ipconfig /flushdns
```

2. FoxFix → **Yeniden kontrol et**. **Beklenen:**
   - DNS ❌ (sistem DNS'i çözemiyor), ayrıntıda "Sistem DNS'i: çalışmıyor, doğrudan UDP: çalışıyor, ..." ya da benzeri.
   - DPI kalıntıları ❌/⚠ "DNS bu bilgisayarı gösteriyor ama yanıt veren bir DNS programı çalışmıyor" (`dns-loopback-orphan`).
3. **Beni Düzelt** (yönetici). **Beklenen önizleme:**
   - Başlık "DNS ayarını onar"
   - `DNS ayarı otomatiğe (modemden) alınacak: Ethernet (şu an: 127.0.0.1)`
   - "DNS önbelleği temizlenecek", geri alma notu.
4. Onayla, **Uygula**. **Beklenen:** "1 bağlantının DNS ayarı otomatiğe alındı."
5. Doğrula:

```cmd
netsh interface ip show dns
ipconfig /all | findstr /i "DNS Servers DHCP"
nslookup google.com
```

   - "DNS servers configured through DHCP" yazmalı, `nslookup` çalışmalı.
   - `undo.json`'da `dns-repair` kaydı, `v4` alanı `127.0.0.1`.
6. **Geri Al:** kayıt "bağlantısının eski DNS ayarı geri getirilebilir". Bas. **Beklenen:** DNS tekrar `127.0.0.1` (statik) olur (bozuk hâli, çünkü geri alma tam olarak eski hâli getirir). `netsh interface ip show dns` ile doğrula.
7. Sonra düzgün yap: `netsh interface ip set dns "Ethernet" dhcp`.

### 7.1 IPv6 varyantı
```cmd
netsh interface ipv6 set dnsservers "Ethernet" static ::1
```
Aynı akışı tekrarla. **Beklenen:** `::1` de yakalanır ve düzelir. (`netsh interface ipv6 show dnsservers`)

### 7.2 Çalışan DnsCrypt-Proxy (dokunmamalı)
Gerçek DnsCrypt-Proxy kurmadan taklit etmek zor. Kurabiliyorsan DnsCrypt-Proxy'yi servis olarak kur ve DNS'i 127.0.0.1 yap. **Beklenen:** Beni Düzelt "DNS bu bilgisayardaki DnsCrypt-Proxy'ye ayarlı ve o çalışıyor. Bilerek yapılmış olabilir, dokunulmayacak." der, değişiklik yok. (Bu yol otomatik testlerle doğrulandı, burada yalnızca isteğe bağlı.)

| Kontrol | Sonuç |
|---|---|
| Bozuk DNS teşhiste ❌ | ☐ |
| Önizleme adaptör ve eski değeri gösterdi | ☐ |
| Uygula sonrası DNS otomatik, nslookup çalışıyor | ☐ |
| `undo.json`'da eski DNS kayıtlı | ☐ |
| Geri Al eski statik DNS'i geri getirdi | ☐ |
| IPv6 `::1` de düzeldi | ☐ |

---

## 8. FoxDPI (GoodbyeDPI tabanlı): deneme, kurulum, kaldırma (deneme, kurulum, kaldırma)

FoxDPI, GoodbyeDPI 0.2.3rc3 (Türkiye sürümü) üzerine kuruludur; FoxFix onun dosyalarını taşır. Dosyalar `C:\ProgramData\FoxFix\GoodbyeDPI` klasörüne açılır ve SHA-256 ile doğrulanır. Sürücü (WinDivert) yükler, bu yüzden **yalnızca VM anlık görüntüsünde dene.** Gerçek bir ISS engeli olmayan bir VM'de "Hepsi zaten açılıyor" sonucu çıkması normaldir; deneme için engelli bir site gerekir.

Başlamadan önce VM'de başka bir GoodbyeDPI/Zapret çalışmıyor olmalı (Bölüm 5'teki temizlik yapılmış olsun).

### 8.1 Yönetici izni
1. FoxFix'i **normal** aç → **DNS ve DPI**. "Denemek ve kurmak için yönetici izni gerekir" yazısı ve **Yönetici olarak yeniden aç** düğmesi görünmeli. "En uygun ayarı bul" ve kurma düğmeleri soluk olmalı.
2. Düğmeye bas, UAC'yi onayla. **Beklenen:** uygulama yönetici olarak açılır ve **DNS ve DPI sayfasında** kalır, düğmeler açılır.

### 8.2 Başka bir GoodbyeDPI çalışırken
Başka bir GoodbyeDPI hizmeti ya da işlemi çalışırken **En uygun ayarı bul**'a bas. **Beklenen:** hiçbir şey başlamaz, "Başka bir GoodbyeDPI ya da Zapret çalışıyor..." yazar. Kurma önizlemesi de aynı uyarıyı verip "değişecek bir şey yok" demeli.

### 8.3 Deneme
Denenecek siteler: `discord.com` (ya da engelli olan bir site). **En uygun ayarı bul.**
- 7 satır sırayla "Deneniyor…" olur, sonra "Hepsi açıldı / Bir kısmı açıldı / Hiçbiri açılmadı / Başlamadı" yazar. Bu sırada internet kısa süre kesilip gelebilir.
- **Beklenen sonuç:** "En uygun ayar: …" ve o satır "önerilen" diye işaretli ve seçili; ya da "Denenen ayarların hiçbiri siteleri açmadı"; ya da "Hedef sitelerin hepsi şu an zaten açılıyor".
- **Bitince hiçbir şey kalmamalı:** `Get-Process goodbyedpi` boş, `Test-Path C:\ProgramData\FoxFix\GoodbyeDPI-trial` False (silinemezse klasör kalabilir, bu zararsız), `sc query FoxFixGoodbyeDPI` "hizmet yok".
- **Durdur** düğmesi denemeyi ortada kesmeli ve yine temizlemeli.
- Antivirüs (ya da Kaspersky) engellerse: "GoodbyeDPI başlayamadı..." mesajı çıkmalı, internet bozulmamalı.

### 8.4 Hizmet olarak kur
Bir ayar seç (önerilen, ya da kendin) → **Hizmet olarak kur.**
1. **Önizleme:** seçilen ayar, klasör yolu, "Windows hizmeti kurulur...", DNS yönlendiren ayarlarda Yandex notu, sürücü uyarısı. **Onaylamadan hiçbir şey olmamalı** (`sc query FoxFixGoodbyeDPI` hâlâ "yok").
2. Onayla → **Uygula.** **Beklenen:** "GoodbyeDPI hizmet olarak kuruldu ve çalışıyor."
3. Kontrol: `sc query FoxFixGoodbyeDPI` → RUNNING. `sc qc FoxFixGoodbyeDPI` → `START_TYPE: AUTO_START`, `BINARY_PATH_NAME` seçtiğin ayarın argümanlarını içermeli. Klasörde `goodbyedpi.exe`, `WinDivert.dll`, `WinDivert64.sys`, iki lisans dosyası ve `foxfix-state.json` olmalı.
4. **Genel bakış:**
   - GoodbyeDPI satırı "Hizmet olarak çalışıyor. Ayar: …" demeli.
   - "Eski araçlardan kalanlar" satırı **bulgu göstermemeli**: FoxFix kendi kurduğunu kalıntı saymamalı.
   - DNS yönlendiren bir ayar seçtiysen DNS satırı **"Dikkat" olmamalı**, "DNS isteklerini FoxFix'in kurduğu GoodbyeDPI yönlendiriyor" demeli.
5. **Beni Düzelt** → "Eski DPI araçlarını temizle" işaretli: önizlemede FoxFix'in kendi kurulumu **listelenmemeli.**
6. Yeniden başlat. **Beklenen:** hizmet kendiliğinden başlar, engelli site açılır.

### 8.5 FoxDPI (hizmetsiz çalıştırma)
Önce hizmet kuruluysa **DPI Sıfırlama** ile kaldır. **Genel bakış** → **FoxDPI Başlat**.
- Yönetici değilsen UAC çıkar, uygulama yönetici olarak yeniden açılır ve FoxDPI kendiliğinden başlar.
- **Beklenen:** küçük bir **FoxDPI** penceresi açılır ("FoxDPI aktif", nabız gibi atan yeşil nokta, seçili ayar). Arka planda pencere açmadan `goodbyedpi.exe` çalışır (`Get-Process goodbyedpi`), `sc query FoxFixGoodbyeDPI` "yok". Genel bakışta düğme **FoxDPI Durdur** olur.
- **Pencereyi küçült:** pencere kaybolur, bildirim alanında (gizli simgeler) FoxFix simgesi kalır. Simgeye **sağ tık**: *Göster*, *FoxFix'i aç*, *Çıkış*. Çift tık pencereyi geri getirir.
- **Çıkış** ya da pencerenin **X**'i ya da **Durdur**: `Get-Process goodbyedpi` boşalır, pencere ve simge kaybolur.
- **FoxFix ana penceresini kapat** (FoxDPI açıkken): ana pencere kapanır ama FoxDPI çalışmaya devam eder. FoxDPI penceresini kapatınca uygulama tamamen biter.
- **Görev Yöneticisi'nden FoxFix'i sonlandır:** `goodbyedpi.exe` de onunla birlikte kapanmalı (arkada kalmamalı).
- Antivirüs engellerse: "FoxDPI başlamadı..." mesajı ortada bir pencerede çıkar, internet bozulmaz.
- Başka bir GoodbyeDPI **hizmeti ya da programı** çalışırken başlamamalı, ortada bir uyarı çıkmalı. Yalnızca sürücü (WinDivert) yüklü kalmışsa **engellememeli**.
- **FoxDPI.exe (FoxFix açılmadan):** yayın klasöründe FoxFix.exe
in yanında FoxDPI.exe durur. Çift tıkla: UAC sorar, sonra yalnızca küçük FoxDPI penceresi açılır, ana pencere ve açılış ekranı açılmaz, açıklama penceresi çıkmaz.
  - FoxFix'i **hiç açmamış** bir bilgisayarda (silinmiş %LocalAppData%\FoxFix\dpi-choice.txt) FoxDPI.exe açılmamalı, "FoxFix'i bir kez açman gerekiyor" demeli.
  - FoxDPI penceresinde yalnızca **FoxFix'i aç** ve **DPI Ayarları** düğmeleri olur (Durdur yok); kapatınca DPI durur. DPI Ayarları, ana pencereyi DNS ve DPI sayfasında açar.
  - FoxFix'ten **FoxDPI'yı başlat**: her seferinde ortada "FoxDPI açıldı" açıklaması çıkar. Düğme sonra "Pencereyi göster" olur (durdurmaz).

- Başlatılacak ayar, DNS ve DPI sayfasında seçtiğin (ya da denemenin önerdiği) ayardır; FoxFix'i kapatıp açınca hatırlanır.
- Deneme bittikten sonra WinDivert sürücüsü bellekte kalmamalı: `sc query WinDivert` "hizmet yok" ya da STOPPED demeli. Kalmışsa DPI Sıfırlama onu da kaldırır.

### 8.6 DPI Sıfırlama, Durdur, Geri Al
**DNS ve DPI** → **DPI Sıfırlama.** Tek önizleme çıkar; değişiklik olan başlıklar listelenir: eski araçlar (hizmet, işlem, sürücü), FoxDPI, elle girilmiş DNS (otomatiğe alınır). Onaylamadan hiçbir şey olmamalı.
- Uygula sonrası: kendi GoodbyeDPI'ın dahil hiçbir GoodbyeDPI/WinDivert hizmeti ya da işlemi kalmamalı, FoxFix klasörü (`C:\ProgramData\FoxFix\GoodbyeDPI`) silinmeli (sürücü takılı kalırsa yeniden başlatınca sil), her bağlantının DNS'i otomatik olmalı (`Get-DnsClientServerAddress`).
- **Geri Al:** DNS ve eski hizmetler geri gelir. FoxFix'in kendi GoodbyeDPI'ı geri gelmez, GoodbyeDPI bölümünden yeniden kurulur.
- Elle girilmiş DNS yoksa DNS satırı hiç görünmemeli, hiçbir şey değişmiyorsa "değişecek bir şey yok" demeli.
- Hizmet kuruluyken **Durdur** (DNS ve DPI sayfası) yalnızca durdurur, kurulum yerinde kalır.

### 8.7 Kurulum başarısız olursa
Defender dosyayı silerse ya da hizmet başlamazsa: "Dosya doğrulaması tutmadı" ya da "Hizmet kurulamadı ya da başlamadı... geri alındı" yazmalı. Klasör, hizmet ve Geri Al kaydı **kalmamalı.**

### 8.8 Windows açılınca başlat (zamanlanmış görev)
Bu adım Windows'ta hiç denenmedi: görevi oluşturan `schtasks` çağrısı ancak yönetici izniyle çalışır. **Elle test edilmeli.**
1. **DNS ve DPI** sayfasında "FoxDPI'yı Windows açılınca başlat" kutusunu işaretle. Önizlemede `FoxFix\FoxDPI` adlı görev ve FoxFix.exe'nin yolu yazmalı. Onayla.
2. `schtasks /Query /TN "FoxFix\FoxDPI" /V /FO LIST` görevi göstermeli: "En yüksek ayrıcalıklarla çalıştır" açık, tetikleyici "Oturum açılışında".
3. Bilgisayardan çıkış yapıp gir. Yaklaşık 10 saniye sonra UAC sormadan FoxDPI penceresi ve tepsi simgesi gelmeli, FoxFix penceresi gelmemeli.
4. Kutuyu kaldır, onayla. Görev silinmeli (`schtasks /Query` bulamamalı). **Geri Al** görevi geri getirmeli.
5. FoxFix.exe'yi başka klasöre taşırsan görev eski yolu gösterir. Kutuyu kapatıp açmak yolu düzeltir.

### 8.9 Erişim testi
**Erişim testi** sayfası: siteleri yaz, **Testi başlat.** Normal bir site (microsoft.com) her zaman en üstte "kontrol" olarak gelir.
- FoxDPI kapalıyken engelli bir site "güvenli bağlantı kesildi" ya da "zaman aşımı" demeli, FoxDPI açıkken "açıldı" demeli. Sayfanın altındaki not FoxDPI'nın durumunu doğru söylemeli.
- İnternet kapalıyken kontrol sitesi de açılmamalı ve "Normal bir site bile açılmadı" yazmalı.
- Test sırasında **Durdur** testi bırakmalı ("Test durduruldu."). Hiçbir ayar değişmemeli.
| Kontrol | Sonuç |
|---|---|
| Yönetici izni ile yeniden açma, aynı sayfaya dönüyor | ☐ |
| Başka GoodbyeDPI varken deneme/kurulum engelleniyor | ☐ |
| Deneme satırları ve "önerilen" işareti | ☐ |
| Deneme bitince hiçbir şey kalmıyor | ☐ |
| Hizmet: kuruldu, açılışta başlıyor | ☐ |
| FoxFix kendi kurulumunu kalıntı saymıyor | ☐ |
| Beni Düzelt kendi kurulumunu silmiyor | ☐ |
| FoxDPI penceresi, bildirim alanı simgesi ve menüsü | ☐ |
| DPI Sıfırlama her şeyi siliyor, DNS otomatiğe dönüyor | ☐ |
| Windows açılınca başlat: görev oluşuyor, girişte FoxDPI geliyor, kapatınca siliniyor | ☐ |
| Erişim testi FoxDPI açık/kapalı farkını gösteriyor | ☐ |

## 9. Tarayıcı eklenti düğmeleri

1. DNS ve DPI sayfasındaki "Tarayıcıda reklam engelleme" bölümünde yalnızca **VM'de kurulu** tarayıcıların düğmeleri görünmeli (Edge Windows ile gelir; Chrome/Firefox'u kurup uygulamayı yeniden aç, düğme çıkmalı).
2. **Edge'de aç**'a bas. **Beklenen:** Edge, **uBlock Origin Lite** mağaza sayfasıyla açılır. Üst durumda "Edge açıldı. Eklentiyi ekle düğmesine kendin bas."
3. Mağaza sayfasında "Al/Ekle" düğmesine **basmadan** önce kontrol et: eklenti kurulu **değil** (`edge://extensions`).
4. Chrome: `chromewebstore.google.com/.../ublock-origin-lite/...`. Firefox: `addons.mozilla.org/addon/ublock-origin/`. Üç bağlantı da **sayfa gerçekten açılıyor** ve doğru eklentiyi gösteriyor mu? (Bağlantıları depo sayfalarından doğruladım, mağazaları kendim açıp görmedim. Bir mağaza kaldırılmış olabilir.)
5. Varsayılan tarayıcı **başka** bir tarayıcı olsa bile düğme, adı yazan tarayıcıyı açmalı.

| Kontrol | Sonuç |
|---|---|
| Yalnızca kurulu tarayıcılar listelendi | ☐ |
| Her düğme doğru tarayıcıyı doğru sayfayla açtı | ☐ |
| Sayfa açılınca hiçbir şey kurulmadı | ☐ |

---

## 9.1 Rapor, tema ve simge

1. **Rapor:** Sayfanın altındaki "Raporu kopyala" düğmesine bas, Not Defteri'ne yapıştır. **Beklenen:** Türkçe harfler düzgün, kullanıcı adın ve bilgisayar adın yok, MAC adresleri `XX-XX-XX-XX-XX-XX`, IP adresleri duruyor.
2. "Kişisel bilgilerimi de göster" kutusunu işaretleyip tekrar kopyala. **Beklenen:** Bu sefer adlar görünür ve raporun başında "kişisel bilgiler gizlenmedi" uyarısı çıkar.
3. "Raporu kaydet…": önerilen ad `FoxFix-rapor-YYYYAAGG-SSdd.txt`. **Beklenen:** Kaydettiğin dosya Not Defteri'nde düzgün açılır. İptal edersen hiçbir şey olmaz.
4. **Tema:** Windows'ta Ayarlar → Kişiselleştirme → Renkler → "Uygulama modunuzu seçin" ayarını açık/koyu arasında değiştir. **Beklenen:** FoxFix kapanmadan renk değiştirir. Koyu temada başlık çubuğu da koyulaşır.
5. **Simge:** Görev çubuğunda, pencere başlığında ve `FoxFix.exe` dosyasında tilki simgesi görünür.

| Kontrol | Sonuç |
|---|---|
| Rapor kopyalandı, adlar gizli | ☐ |
| Kutu işaretli rapor adları gösteriyor | ☐ |
| Kaydedilen dosyada Türkçe harfler doğru | ☐ |
| Tema canlı değişti | ☐ |
| Tilki simgesi her yerde | ☐ |

---

## 10. Zor durumlar

### 10.1 Sürücü bellekten inmiyor (STOP_PENDING)
Deneysel. **Snapshot:** `02-gdpi-kurulu`.

1. Yönetici komut isteminde, hizmet çalışırken `goodbyedpi.exe -5`'i **elle de** başlat (iki kopya aynı sürücüyü tutar).
2. FoxFix ile temizle. **Beklenen:** Sürücü `STOP_PENDING`'de kalabilir. Sonuç "Bitirmek için bilgisayarı yeniden başlat.", "Yeniden başlatma gerekiyor" kutusu çıkar. `sc query WinDivert` "STOP_PENDING" ya da silinmek üzere işaretli. Yeniden başlatma sonrası temiz.
3. Çıkmazsa (hemen inerse) bu test "tetiklenemedi" diye işaretlenir, sorun değil.

### 10.2 Bozuk geri alma dosyası
1. FoxFix'i kapat. `%LocalAppData%\FoxFix\undo.json` içeriğini `{ bozuk` ile değiştir.
2. FoxFix'i aç. **Beklenen:** Çökmez, Geri Al listesi boş, klasörde `undo.json.bad-...` yedek dosyası oluşur.

### 10.3 Eski kayıtla geri alma
Bölüm 3.3'ten sonra, FoxFix'i kapat/aç, kaydı yeniden yüklenmiş hâlde **Geri Al**. Çalışmalı (kalıcılık testi).

---

## 11. Bir şey ters giderse

1. **Panik yok:** VM'de anlık görüntüye dön (`01-temiz` ya da `02-gdpi-kurulu`).
2. Hatanın yeniden üretilmesi için yapılan adımları yaz.
3. Şunları topla (rapora ekle):
   - Pencerenin ekran görüntüsü ve mesaj metni
   - `%LocalAppData%\FoxFix\undo.json`
   - `sc query GoodbyeDPI`, `sc query WinDivert`, `sc qc ...` çıktıları
   - `ipconfig /all`, `netsh interface ip show dns`, `netsh dns show encryption`
   - Windows sürümü (`winver`) ve dil

---

## 12. Sonuç tablosu (doldur)

| Bölüm | Windows 11 | Windows 10 | Not |
|---|---|---|---|
| 1 Temel | ☐ | ☐ | |
| 3 Temizleme + UAC | ☐ | ☐ | |
| 4 Yeniden başlatma | ☐ | ☐ | |
| 5 Geri alma | ☐ | ☐ | |
| 6 Sınır testleri | ☐ | ☐ | |
| 7 DNS onarımı | ☐ | ☐ | |
| 8 GoodbyeDPI yöneticisi | ☐ | | |
| 9 Tarayıcı düğmeleri | ☐ | ☐ | |
| 10 Zor durumlar | ☐ | ☐ | |

## Bu rehberin ele aldığı "doğrulanmamış" noktalar (özet)

Otomatik testlerin **kapsamadığı**, burada denenmesi gerekenler:
- `sc.exe stop/delete/create/start` ile gerçek servis ve sürücü işlemleri, çıkış kodu eşlemesi (1060, 1062, 1072).
- `sc create` ile yeniden oluşturmanın eski ayarları ne kadar koruduğu.
- `netsh interface ip/ipv6 set/add dns` sözdizimi (adaptör adı tek parametre olarak, `index=` ile ek sunucu).
- `netsh dns add/delete encryption` (Windows 11) ve `autoupgrade=yes udpfallback=yes`.
- Kayıt defterinden eski DNS ayarını okuma (`NameServer`).
- `shutdown /r /t N /c ...` ve `shutdown /a` (iptal).
- UAC ile yeniden başlatma (`runas`).
- Tarayıcıları kayıt defterinden bulup açma (`App Paths`).
- Türkçe Windows'ta komut çıktısı/kodlama sorunu olmadığı (FoxFix çıkış kodlarını kullanır, metin ayrıştırmaz, ama test sırasında Türkçe VM'de de bir tur dene).

## 12. Sonradan eklenenler (elle bakılacaklar)
- **FoxDPI başlatma ekranı:** Genel bakıştan "FoxDPI'yı başlat" dediğinde FoxDPI logolu bir ekran çıkmalı, çubuk dolmalı, FoxDPI penceresi açılınca kaybolmalı. Başlamazsa ekran kaybolup hata mesajı çıkmalı (ekranda takılı kalmamalı).
- **Hız testi paylaşımı:** Ölçüm bitince "Sonucu paylaş" çıkmalı, tıklayınca "PNG kopyalandı" yazmalı. Bir sohbet ya da Paint'e yapıştırınca koyu bir kart gelmeli: dört değer ve altta `github.com/foxezol/FoxFix`.
- **Bilgi yanındaki Paylaş:** Tıklayınca GitHub adresi panoya gitmeli, düğme iki saniye "Bağlantı kopyalandı" yazmalı.
- **Erişim testi:** microsoft.com ve google.com "kontrol" olarak en üstte gelmeli. İkisi de açılmazsa "Normal sitelerin hiçbiri açılmadı" yazmalı, biri açılırsa yazmamalı.
- **Hata kodları:** Bilgi → "Hata kodları" tablonun GitHub sayfasını açmalı. Mesajlardaki `[FX-xxxx]` kodlarının tabloda karşılığı olmalı (`docs/HATA-KODLARI.md`).
- **Kurulum silme:** Setup ile kurup kaldırdıktan sonra `C:\ProgramData\FoxFix` ve `%LocalAppData%\FoxFix` klasörleri kalabilir. Kalanlar zararsızdır, DPI Sıfırlama sürücüyü kaldırır.