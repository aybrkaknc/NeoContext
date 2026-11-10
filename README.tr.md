<p align="center">
  <img src="src/NeoContext.App/Assets/Square150x150Logo.png" width="96" alt="NeoContext logosu" />
</p>

<h1 align="center">NeoContext</h1>

<p align="center">
  Windows sağ tık menüsü için modern ve güvenli bir yönetici.<br/>
  C#, .NET 10 ve WinUI 3 ile geliştirildi.
</p>

---

[English](README.md) | Türkçe

NeoContext, Explorer'ın sağ tık menülerine eklediği her girdiyi gösterir; her birini açıp kapatmanıza,
yeniden adlandırmanıza, düzenlemenize, taşımanıza, kaldırmanıza veya yenisini eklemenize izin verir.
[ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager) projesinin (BluePointLilac) fikirlerinden
yola çıkılarak yeniden yazıldı; güvenlik, net bir yapı ve tamamen yerelleştirilebilir, İngilizce öncelikli bir arayüz
için tasarlandı.

## Özellikler

| Sayfa | Neyi yönetir | NeoContext nasıl değiştirir |
| --- | --- | --- |
| Dosyalar, Klasör, Dizin, Klasör arka planı, Masaüstü, Sürücüler, Dosyalar ve klasörler, Bu Bilgisayar, Geri Dönüşüm Kutusu, Kütüphaneler | Sabit komutlar (`shell` fiilleri) ve COM kabuk uzantıları (`shellex` işleyicileri) | Fiiller: belgelenmiş `LegacyDisable` değeri; işleyiciler: `-ContextMenuHandlers` altına taşınır. Fiiller ayrıca düzenlenebilir (komut, simge, konum, bayraklar) |
| Yeni menüsü | Explorer'ın **Yeni** alt menüsündeki dosya türleri | `ShellNew` anahtarı `-ShellNew` olarak yeniden adlandırılır |
| Gönder | Gönder klasöründeki dosyalar | Gizli özniteliği; adlar `desktop.ini`'den; silme Geri Dönüşüm Kutusu'na gider; yeni kısayollar |
| Birlikte aç | "Birlikte aç" için kayıtlı programlar | Ad için `NoOpenWith` değeri ve `FriendlyAppName`; yeni programlar |
| Win+X menüsü | Win+X / Başlat düğmesi menüsündeki kısayollar | Gizli özniteliği; her değişiklikten sonra Win+X hash'i yeniden yazılır; yukarı veya aşağı taşıma, ekleme, varsayılan menüyü geri yükleme |
| Dosya türleri | Seçilen dosya uzantılarına ait girdiler, örneğin `.mp4` veya `.zip` | Diğer sayfalarla aynı işlemler; grup seçin veya uzantı yazın |
| Mağaza uygulamaları | Yüklü MSIX / Microsoft Store uygulamalarının bildirdiği bağlam menüsü fiilleri | Kullanıcıya özel `Shell Extensions\Blocked` listesi |

Her sayfada arama vardır ve her girdi Kayıt Defteri Düzenleyicisi veya Explorer'da açılabilir. Fiiller
düzenlenebilir, yeni öğeler de bunları kabul eden sayfalarda oluşturulabilir. Simge seçici, bir .exe, .dll
veya .ico dosyasındaki tüm simgeleri gösterir.

### Önce güvenlik

- **Sessiz izin değişikliği yok.** Okuma işlemi izinlere dokunmaz. Windows bir anahtarı koruyorsa (örneğin
  TrustedInstaller'a ait), NeoContext sahipliği almadan önce sorar, önceki sahibi ve izinleri kaydeder ve
  *Ayarlar → İzinler* bölümünden geri yükleyebilir.
- **Silmeden önce yedek.** Kayıt defteri girdisi silinmeden önce bir `.reg` dosyasına aktarılır; aktarma başarısızsa
  hiçbir şey silinmez. Win+X kısayolları Geri Dönüşüm Kutusu'na gider.
- **Gerekli girdiler korunur.** Explorer'ın ihtiyaç duyduğu girdiler (örneğin *Aç*) koruma kapatılmadıkça
  devre dışı bırakılamaz veya silinemez.
- **Önce kullanıcı.** Kullanıcıya özel ve makine geneli girdiler ayrı okunur ve etiketlenir; çoğu değişiklik
  yönetici hakkı olmadan çalışır. Yükseltme gerektiğinde NeoContext her zaman yönetici olarak çalışmak yerine
  yönetici olarak yeniden başlatmayı önerir.
- **Doğrulanmış güncellemeler.** NeoContext yeni sürümler hakkında yalnızca bilgi verir. Bir sürümü indirmeye karar
  verirseniz, dosya GitHub'da yayımlanan SHA-256 özetiyle eşleşmelidir; eşleşmezse indirme atılır.
  Sürümler henüz kod imzalı değil, bu yüzden SmartScreen ilk açılışta uyarabilir.

## Gereksinimler

- Windows 10 sürüm 1809 veya sonrası, ya da Windows 11 (x64 veya ARM64)
- Derlemek için: [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio gerekmez.

## Kurulum

[Releases](https://github.com/aybrkaknc/NeoContext/releases) sayfasından en son sürümü indirin:

- **`NeoContext-<sürüm>-win-x64.zip`** - taşınabilir. Çıkarın ve `NeoContext.exe` dosyasını çalıştırın. Ayarları ve
  yedekleri `%LOCALAPPDATA%` yerine orada tutmak için çalıştırılabilir dosyanın yanına boş bir `data` klasörü
  oluşturun.

## Kaynaktan derleme

```powershell
git clone https://github.com/aybrkaknc/NeoContext.git
cd NeoContext
dotnet test                                   # birim ve tümleşik testler
dotnet build src/NeoContext.App -p:Platform=x64 -p:WindowsPackageType=None
src/NeoContext.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/NeoContext.exe
```

Sürüm paketleri, sürüm iş akışının çalıştırdığı [`build/Publish.ps1`](build/Publish.ps1) ile üretilir.

> Derlemeyi varsayılan çıktı klasörüne yapın. `dotnet build`'e `-o <dizin>` verilmesi, derlenmiş XAML'ın kaynak
> dizinindeki yollarını değiştirir ve uygulama "XAML parsing failed" hatasıyla açılmaz.

## Mimari

```
src/
  NeoContext.Core       Alan modeli, kurallar ve servisler. Windows API'si yok; birim testi yazılabilir.
  NeoContext.Registry   Kayıt defteri yolları ve erişimi, fiil / kabuk uzantısı / ShellNew sağlayıcıları,
                        anahtar sahipliği (alma ve geri yükleme), .reg dışa aktarma, Shell Extensions\Blocked.
  NeoContext.Shell      .lnk ve özellik deposu iletişimi, Win+X hash'i ve sağlayıcısı, Gönder, desktop.ini.
  NeoContext.Uwp        Yüklü paketlerin AppxManifest.xml dosyalarını okur, bağlam menüsü fiillerini bulur.
  NeoContext.Updates    GitHub sürüm sorgusu, sürüm karşılaştırma, doğrulanmış indirmeler.
  NeoContext.App        WinUI 3 görünümleri ve görünüm modelleri (MVVM), kompozisyon kökü (Services/AppHost.cs).
tests/
  NeoContext.Tests      xUnit testleri; kayıt defteri testleri HKCU altındaki geçici bir anahtarda çalışır.
```

Her menü girdisi türü bir `IEntryProvider`'dır; girdileri düzenleyebilen veya sıralayabilen sağlayıcılar ayrıca
`IEntryEditor` veya `IEntryMover` uygular. `ContextMenuService` tüm sağlayıcıları yükler, koruma ve adlandırma
kurallarını uygular ve her değişikliği doğru sağlayıcıya yönlendirir. Sağlayıcılar arayüz göstermez, beklenen hatalar
için istisna fırlatmaz; bunun yerine bir `Result` döndürür ve arayüz bunu bir sonraki adımı da gösteren bir
mesaja çevirir (yönetici olarak yeniden başlat, sahipliği al, Explorer'ı yeniden başlat).

### ContextMenuManager'dan farklar

| Alan | ContextMenuManager | NeoContext |
| --- | --- | --- |
| Korunan anahtarlar | Her okumada, özyinelemeli olarak sahiplik alır, hiç geri yüklemez | Yalnızca yazma hatasından sonra, onayla, tek anahtar, geri yüklenebilir |
| Güncellemeler | Doğrulamadan bir .exe indirir ve çalıştırır | Yalnızca bildirim; indirme tutulmadan önce SHA-256 + Authenticode doğrulaması |
| Hatalar | Boş catch blokları, yok sayılan HRESULT'ler | Tipli `Result` değerleri, günlüğe yazılır, önerilen çözümle gösterilir |
| Yapı | WinForms denetimleri içinde kayıt defteri kodu | Ayrı alan modeli, sağlayıcılar ve MVVM arayüzü |
| Fiil etkinleştirme | `CommandFlags` değerini siliyordu | Yalnızca `LegacyDisable` / `ProgrammaticAccessOnly` kaldırılır |
| Win+X hash'i | Sıraya bağlı yol değiştirme, kontrol edilmeyen HRESULT'ler | En uzun önek eşleşmesi, kontrol edilmiş; Windows'un kendi kısayollarına karşı test edildi |
| Mağaza uygulamaları | Paket adlarında önek eşleşmesi | Her paketin kendi manifestinden tam eşleşme |
| Sınıf kökü | Birleşik `HKEY_CLASSES_ROOT` üzerine yazıyordu | HKCU ve HKLM ayrı okunur ve kapsam etiketlenir |

## Bilinen sınırlamalar

- Windows 11, kayıt defterine dayalı girdileri **Diğer seçenekleri göster** altında gösterir; NeoContext onları orada yönetir.
  Windows 11'in yeni menüsündeki girdiler paketli uygulamalardan gelir ve **Mağaza uygulamaları** sayfasındadır.
- Yeni menüsü (ShellNew), Win+X ve Mağaza uygulaması değişiklikleri Explorer yeniden başladıktan sonra görünür;
  NeoContext bunu yapmayı önerir.
- Yeni menüsü öğeleri yeniden adlandırılamaz; bu, dosya türünün adını Windows'un her yerinde değiştirirdi.
- Kabuk uzantısı yeniden adlandırılamaz; adı COM sınıfından gelir.
- Yalnızca fiiller düzenlenebilir. Kabuk uzantıları, yeni menüsü öğeleri ve Mağaza uygulamaları düzenlenemez.
- Win+X grup klasörleri yeniden adlandırılamaz veya kaldırılamaz; varsayılan menü bütün olarak geri yüklenebilir.
- Başka programlar tarafından oluşturulan alt menüler gösterilir, ancak öğeleri henüz NeoContext'ten düzenlenemez.

## Yerelleştirme

Arayüz metinleri `src/NeoContext.App/Strings/<dil>/Resources.resw` dosyalarında tutulur. Varsayılan dil İngilizce
(`en-US`), Türkçe (`tr-TR`) de dahildir. Yeni dil eklemek için `en-US` klasörünü kopyalayın, değerleri çevirin ve dili
`SettingsViewModel.Languages` listesine ekleyin.

## Lisans

NeoContext, [GNU General Public License v3.0](LICENSE) ile lisanslanmış özgür bir yazılımdır.

[ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager) © BluePointLilac projesinin fikirlerine ve
Windows araştırmasına dayanır; o da GPL-3.0 ile lisanslıdır. Win+X hash algoritması, Rafael Rivera'nın
[hashlnk](https://github.com/riverar/hashlnk) projesini izler.
