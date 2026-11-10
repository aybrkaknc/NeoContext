# Changelog / Değişiklik günlüğü

## 1.2.0

English | [Türkçe](#türkçe)

The earlier 0.1.0 and 0.2.0 downloads were withdrawn: the packaged app closed right after start-up. This
release fixes that and adds a round of usability work.

### Fixed

- **The portable zip now starts.** `dotnet publish` left out the compiled XAML (`App.xbf`) and the resource
  index (`NeoContext.pri`), so the app closed at once. The publish script now includes both and can start the
  packaged app as a check (`Publish.ps1 -SmokeTest`).
- **Toggling one entry no longer redraws every switch.** The list is not reloaded after a successful toggle.
- **Search no longer crashes** on some inputs. Highlighting used culture-aware matching, which could return a
  different length than the query.

### Easier to use

- **On / Off** is written next to every switch. The "All users" / "Current user" tags were removed.
- **Shield icon** marks entries NeoContext protects (such as the *Open* verb).
- **"Shift + right-click"** replaces "Shift only", with a tooltip that explains it.
- **One-page introduction guide** on first launch, and a *Show* button in Settings to open it again.
- **Grouped navigation:** *Locations*, *Apps and menus*, *File types* and *Other rules*. Only the first group
  starts expanded.

### Page header

- The header fits the room it has: text buttons when wide, icon buttons when narrower, and the search box on
  its own row when narrow. The page description has its own full-width row, and the title keeps a minimum width.
- **New item**, **Take ownership** and a **Restart Explorer** icon button sit in the header. *Choose a registry
  key* and *Restore the default menu* are in a **More** menu on the pages that use them.
- The window cannot be made smaller than 720 × 480.

### Permissions

- **Take ownership of protected entries** works for a whole page with a single confirmation that lists every
  key. The button only shows on pages that list a protected key. Every change is backed up and can be restored
  in Settings.
- **Optional, off by default:** a Settings switch that takes ownership without asking while NeoContext runs as
  administrator. It warns before you turn it on.

### Names

- Nilesoft Shell's two shell extensions are named in the dictionary.
- A shell extension without a known name now shows the description stored in its DLL before falling back to
  the raw GUID.

### Install

Download `NeoContext-1.2.0-win-x64.zip` (or `-win-arm64.zip`), extract it and run `NeoContext.exe`. Check the
download against `SHA256SUMS.txt`. Releases are not code-signed yet, so Windows SmartScreen may warn on first
launch. The ARM64 zip is built the same way but has not been run on an ARM device.

---

<a id="türkçe"></a>

## 1.2.0 (Türkçe)

Önceki 0.1.0 ve 0.2.0 indirmeleri geri çekildi: paketlenmiş uygulama açılır açılmaz kapanıyordu. Bu sürüm bunu
düzeltiyor ve kullanım kolaylığı için bir dizi iyileştirme getiriyor.

### Düzeltmeler

- **Taşınabilir zip artık açılıyor.** `dotnet publish`, derlenmiş XAML'i (`App.xbf`) ve kaynak dizinini
  (`NeoContext.pri`) pakete koymuyordu, bu yüzden uygulama hemen kapanıyordu. Yayın betiği artık ikisini de
  ekliyor ve paketlenmiş uygulamayı denetim için başlatabiliyor (`Publish.ps1 -SmokeTest`).
- **Bir girdiyi açıp kapatmak artık bütün anahtarları yeniden çizmiyor.** Başarılı bir değişiklikten sonra liste
  yeniden yüklenmiyor.
- **Arama bazı girdilerde çökmüyor.** Vurgulama kültüre duyarlı eşleştirme kullanıyordu ve sorgudan farklı
  uzunlukta sonuç verebiliyordu.

### Kullanım kolaylığı

- Her anahtarın yanında **Açık / Kapalı** yazıyor. "All users" / "Current user" etiketleri kaldırıldı.
- **Kalkan simgesi**, NeoContext'in koruduğu girdileri (örneğin *Aç* fiilini) gösteriyor.
- **"Shift + sağ tık"** rozeti, "Yalnızca Shift"in yerine geçti ve ne anlama geldiğini açıklayan bir ipucu var.
- İlk açılışta **tek sayfalık tanıtım rehberi** gösteriliyor; Ayarlar'daki *Göster* düğmesiyle yeniden açılabiliyor.
- **Gruplu gezinme:** *Konumlar*, *Uygulamalar ve menüler*, *Dosya türleri* ve *Diğer kurallar*. Yalnızca ilk grup
  açık başlıyor.

### Sayfa başlığı

- Başlık, sahip olduğu alana uyum sağlıyor: geniş olduğunda metinli butonlar, daraldığında simgeli butonlar, çok
  dar olduğunda arama kutusu kendi satırında. Sayfa açıklaması tam genişlikte kendi satırında, başlık en az
  belli bir genişliği koruyor.
- **Yeni öğe**, **Sahipliği al** ve **Explorer'ı yeniden başlat** simge butonu başlıkta. *Kayıt defteri anahtarı
  seç* ve *Varsayılan menüyü geri yükle*, ilgili sayfalarda **Diğer** menüsünde.
- Pencere 720 × 480'den küçük yapılamıyor.

### İzinler

- **Korumalı girdilerin sahipliğini al**, bütün sayfa için tek onayla çalışıyor; onay penceresi her anahtarı
  listeliyor. Buton yalnızca korumalı anahtar içeren sayfalarda görünüyor. Her değişiklik yedekleniyor ve
  Ayarlar'dan geri alınabiliyor.
- **İsteğe bağlı, varsayılan kapalı:** NeoContext yönetici olarak çalışırken sahipliği sormadan alan bir Ayarlar
  anahtarı. Açmadan önce uyarı gösteriyor.

### Adlar

- Nilesoft Shell'in iki kabuk eklentisi sözlükte adlandırıldı.
- Adı bilinmeyen bir kabuk eklentisi, ham GUID yerine önce DLL'inde kayıtlı açıklamayı gösteriyor.

### Kurulum

`NeoContext-1.2.0-win-x64.zip` (veya `-win-arm64.zip`) dosyasını indirin, çıkarın ve `NeoContext.exe`'yi
çalıştırın. İndirmeyi `SHA256SUMS.txt` ile doğrulayın. Sürümler henüz kod imzalı değil, bu yüzden Windows
SmartScreen ilk açılışta uyarı verebilir. ARM64 zip'i aynı şekilde üretiliyor ama bir ARM cihazda çalıştırılmadı.
