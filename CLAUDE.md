# FoxFix

Windows ağ/DNS teşhis ve onarım aracı. Tam plan: `FOXFIX-PLAN-v2.md` (Bölüm 16 ve 17 önceliklidir). Arayüz Türkçe, kod İngilizce.

## Komutlar
- Derle: `dotnet build FoxFix.sln`
- Test: `dotnet test FoxFix.sln`
- .NET 10 SDK gerekir.

## Kurallar
- Önce `FoxFix.Core`, arayüz (`FoxFix.App`) yalnızca Core'u çağırır.
- Sistem komutları yalnızca `ICommandRunner` ile; mümkünse native API.
- Kullanıcıya görünen metin `src/FoxFix.Core/Resources/Strings.resx` içinde, `Strings.cs`'e anahtar eklenir.
- Her `IFix`: önizleme, idempotent, değiştirmeden önce `UndoJournal`'a yaz, birim testi.
- Silme/sürücü işlemleri yalnızca kesin ad + yol allowlist'iyle. Geniş regex ile asla silme.
- Hata yutma yok: logla, kullanıcıya sade mesaj göster.
- Windows'ta denenmemiş her riskli adım için "elle test edilmeli" yaz, "çalışıyor" deme.
- Servis/sürücü silme testleri yalnızca VM anlık görüntüsünde.
