# LanBeam — Güvenlik ve Kod Kalitesi Denetim Raporu

> **✅ ÇÖZÜM DURUMU (v0.3.0, 2026-07-14):** Aşağıdaki 12 bulgunun **tamamı düzeltildi.**
> Özet düzeltmeler:
> - **#1** JsonChannel kademeli çerçeve sınırı: eşleştirme öncesi 2 MB (`MaxControlFrame`),
>   Offer için eşleştirme sonrası 64 MB'a yükseltiliyor.
> - **#2** Kabul öncesi disk boş-alan kontrolü + taşmasız offset kontrolü (`offset > Size - length`).
> - **#3** Eşzamanlı bağlantı sınırı (64), el sıkışma/ilk-mesaj zaman aşımı (10 sn), IP başına tek
>   bekleyen PIN penceresi + cooldown, eşleştirme yanıt zaman aşımı (2 dk).
> - **#4** Avatar sunumu ve otomatik çekme yalnızca güvenilir (eşleşmiş) cihazlarla.
> - **#5** Offer'da yinelenen `fileId` reddediliyor.
> - **#6** Resume'da diskteki parçalar xxHash3 ile yeniden doğrulanıyor (bozuksa yeniden indiriliyor).
> - **#7** Alıcıda "kimlik değişti" uyarısı PIN penceresinde gösteriliyor.
> - **#8** `FinalizeSuccess` tek girişli (Interlocked bayrağı).
> - **#9** `SettingsView` event aboneliği tek sefer + `Unloaded`'da çözülüyor.
> - **#10** Path çözümlemede ayrılmış aygıt adları / sondaki nokta-boşluk / geçersiz karakter reddi.
> - **#11** `FirewallHelper` exePath doğrulaması (komut ayracı içeren yolu reddet).
> - **#12** Named pipe yalnızca mevcut kullanıcı ACL'i + sınırlı okuma (64 KB).
>
> Regresyon: **35/35 test yeşil** (10 yeni düşmanca-girdi testi eklendi). Aşağıdaki rapor
> orijinal denetim bulgularını (düzeltme öncesi durum) belgeler.

---

**Denetim tarihi:** 2026-07-14
**Sürüm:** v0.2.0 (denetlenen) → v0.3.0 (düzeltilmiş)
**Yöntem:** Statik kod incelemesi (tüm ★/★★ dosyalar satır satır) + `dotnet list package --vulnerable`
+ `dotnet test`. Tehdit modeli: eşleşmemiş LAN saldırganı **ve** kötü niyetli eşleşmiş cihaz.

**Özet tablo:**

| # | Önem | Başlık | Konum |
|---|------|--------|-------|
| 1 | 🔴 Yüksek | Kimlik doğrulamasız 256 MB çerçeve ön-tahsisi (bellek-DoS) | `JsonChannel.cs:52-56` |
| 2 | 🟠 Orta | Gönderen kontrollü boyutla disk ön-tahsisi; boş alan kontrolü yok | `ReceiveSession.cs:211-212` |
| 3 | 🟠 Orta | Sınırsız bağlantı + okuma zaman aşımı yok + PIN penceresi seli | `TransferListener.cs:70,170` |
| 4 | 🟠 Orta | Avatar eşleştirme öncesi herkese açık + otomatik güvenilmeyen görüntü çözme | `TransferListener.cs:105-109`, `AvatarCacheService.cs:57-85` |
| 5 | 🟡 Düşük | Offer'da `fileId` çakışması → sessiz veri kaybı | `ReceiveSession.cs:89` |
| 6 | 🟡 Düşük | Resume'da diskteki parçalar yeniden hash'lenmiyor | `ReceiveSession.cs:75-84` |
| 7 | 🟡 Düşük | Alıcı tarafında pinning asimetrisi (bypass yok, zayıf sinyal) | `TransferListener.cs:132` |
| 8 | 🟡 Düşük | Sentinel/resend yarışı + çift `FinalizeSuccess` | `ReceiveSession.cs:196,240` |
| 9 | 🟡 Düşük | `SettingsView` event aboneliği sızıntısı | `SettingsView.xaml.cs` |
| 10 | 🟡 Düşük | Path çözümlemede ayrılmış aygıt adları / normalizasyon | `FileTreeScanner.cs:99` |
| 11 | 🟡 Düşük | `FirewallHelper` komut string interpolasyonu | `FirewallHelper.cs` |
| 12 | 🟡 Düşük | Named pipe varsayılan ACL (doğrulanmalı) | `SingleInstance.cs:44` |

**Olumlu bulgular (sağlam yapılmış):** Eşleştirme kriptografisi (HMAC-SHA256, `FixedTimeEquals`,
CSPRNG PIN, yön asimetrisi); TLS kimlik bağlama; path traversal katmanlı savunması; DPAPI ile
anahtar saklama; veri kanalının token+fingerprint'e bağlanması; **0 güvenlik açıklı bağımlılık;
25/25 test yeşil.**

---

## 🔴 YÜKSEK

### [YÜKSEK] Kimlik doğrulamasız 256 MB çerçeve ön-tahsisi ile bellek tükenmesi (DoS)
**Konum:** `src/LanBeam.Core/Protocol/JsonChannel.cs:52-56`, `ProtocolConstants.cs:15`,
tetikleme yolu `TransferListener.cs:84`
**Kategori:** Bellek-DoS / kimlik doğrulamasız
**Kanıt:**
```csharp
// JsonChannel.cs
int length = BinaryPrimitives.ReadInt32LittleEndian(header);   // saldırgan kontrolünde
if (length <= 0 || length > ProtocolConstants.MaxJsonFrame)     // MaxJsonFrame = 256 MB
    throw new InvalidDataException(...);
byte[] payload = new byte[length];                              // gövde gelmeden HEMEN ayrılır
```
`TlsHelper` her sertifikayı kabul ettiğinden (self-signed + pinning tasarımı) **herhangi bir LAN
cihazı** TLS el sıkışmasını tamamlayıp JSON katmanına ulaşır. `HandleConnectionAsync` ilk Hello'yu
`channel.ReceiveAsync` ile **eşleştirmeden önce** okur (satır 84). Saldırgan 4 baytlık ön ekte
256 MB bildirip gövdeyi hiç göndermese bile sunucu anında 256 MB (LOH) ayırır.
**Saldırı senaryosu:** Saldırgan aynı ağdan onlarca eşzamanlı bağlantı açar, her biri 256 MB
bildirir → birkaç GB anlık tahsis → `OutOfMemoryException` / süreç çökmesi. `TransferListener.cs:70`
her bağlantı için sınırsız `Task.Run` yaptığından bağlantı sayısı sınırı da yok.
**Önerilen düzeltme:** Mesaj tipine göre kademeli çerçeve sınırı — Hello/pairing için birkaç KB,
yalnızca `Offer` için büyük sınır (o da eşleştirme sonrası). Ayrıca eşzamanlı bağlantı
sayısı/hız sınırı (`SemaphoreSlim`) ve büyük tamponu akışla (streaming) okuyup üst sınırı
kademeli doğrulama. Alternatif: `MaxJsonFrame`'i eşleştirme öncesi çok küçük tutup Offer'a özel
büyük sınıra eşleştirme sonrası geç.
**Güven:** Kesin

---

## 🟠 ORTA

### [ORTA] Gönderen kontrollü dosya boyutuyla disk ön-tahsisi; boş alan kontrolü yok
**Konum:** `src/LanBeam.Core/Transfer/ReceiveSession.cs:211-212` (ayrıca sınır kontrolü `:146`)
**Kategori:** Disk-DoS / kaynak istismarı (kötü niyetli eşleşmiş cihaz)
**Kanıt:**
```csharp
if (RandomAccess.GetLength(handle) != slot.Entry.Size)
    RandomAccess.SetLength(handle, slot.Entry.Size);   // Entry.Size gönderenden geliyor
```
`Entry.Size` teklifteki (Offer) gönderen kontrollü alandır. Alıcı kabul ettiğinde ilk parça
gelince dosya bu boyuta ayarlanır. **Kabul öncesinde disk boş alanı ile karşılaştırma yok.**
Kullanıcı onay penceresinde `TotalBytes`'ı görüyor (kısmi hafifletme), ama: (a) boş alandan büyük
ama makul görünen bir transfer kabul edilirse disk dolabilir; (b) offset doğrulaması
`offset + length > slot.Entry.Size` (satır 146) `long` aritmetiğinde teorik taşmaya (overflow)
karşı yalnızca dolaylı korunuyor — asıl emniyet devasa `SetLength`'in `ENOSPC` ile geç başarısız
olması.
**Saldırı senaryosu:** Kötü niyetli eşleşmiş cihaz, boş alana yakın toplam boyut bildirir → alıcı
kabul eder → disk dolar; ya da çok sayıda dosyayı büyük boyutlarla bildirip fragmentasyon/dolum
yaratır.
**Önerilen düzeltme:** Kabul akışında `DriveInfo(destination).AvailableFreeSpace` ile
`offer.TotalBytes` karşılaştırması; yetersizse uyar/engelle. `offset + length` için `checked`
aritmetiği veya açık `offset <= Entry.Size - length` biçiminde taşmasız kontrol. Makul bir
üst sınır (örn. tek dosya için) düşünülebilir.
**Güven:** Kesin

### [ORTA] Sınırsız bağlantı + okuma zaman aşımı yok + eşleşmemiş PIN penceresi seli
**Konum:** `src/LanBeam.Core/Transfer/TransferListener.cs:70` (sınırsız `Task.Run`), `:170`
(`PairingStarted` her bağlantıda), tetiklenen UI `App.xaml.cs` → `PairingPinWindow`
**Kategori:** DoS / UI tacizi (kimlik doğrulamasız)
**Kanıt:** `AcceptLoopAsync` her kabul edilen soket için `_ = Task.Run(HandleConnectionAsync...)`
— eşzamanlılık/hız sınırı yok. `HandleConnectionAsync` ilk `ReceiveAsync`'te **okuma zaman aşımı
yok**: TLS'i tamamlayıp hiç veri göndermeyen bir istemci Task'ı ve soketi süresiz meşgul eder
(slowloris). Ayrıca her eşleşmemiş Control bağlantısı `RunReceiverPairingAsync` → `PairingStarted`
→ alıcı ekranında bir **PIN penceresi** açar.
**Saldırı senaryosu:** (1) Saldırgan yüzlerce yarı-açık bağlantı bırakır → Task/soket tükenmesi.
(2) Saldırgan saniyede onlarca Control bağlantısı açar → alıcının ekranı PIN pencereleriyle dolar
(kullanılamaz hale gelir). PIN her bağlantıda yeniden rastgele üretildiğinden brute-force avantajı
**yok** (bu kısım doğru tasarlanmış), ama pencere seli gerçek bir taciz-DoS'tur.
**Önerilen düzeltme:** El sıkışma + ilk mesaj için zaman aşımı (`CancellationTokenSource` ~10 sn);
eşzamanlı bağlantı üst sınırı (`SemaphoreSlim`); aynı IP/fingerprint için bekleyen tek PIN
penceresi + kısa cooldown; kabaca hız sınırı.
**Güven:** Kesin

### [ORTA] Avatar eşleştirme öncesi herkese sunuluyor + güvenilmeyen görüntü otomatik indirilip çözülüyor
**Konum:** `TransferListener.cs:105-109` (herkese avatar), `AvatarCacheService.cs:57-85`
(otomatik indirme) + `:75,90` (`LoadFrozen` = WIC çözme), tetikleme `DiscoveryService.cs:220` →
`MainViewModel.UpsertDevice` → `EnsureFetched`
**Kategori:** Bilgi sızıntısı + güvenilmeyen içerik işleme (kimlik doğrulamasız)
**Kanıt:**
```csharp
// TransferListener.cs — eşleştirme şartı YOK
case ConnectionPurpose.Avatar:
    (string tag, byte[]? png) = AvatarProvider?.Invoke() ?? ("", null);
    await channel.SendAsync(MessageTypes.Avatar, new AvatarMessage(tag, ...png...), ct);
```
```csharp
// AvatarCacheService.cs — keşifte img: etiketi görülünce otomatik
byte[]? png = await _node.FetchAvatarAsync(snapshot, timeout.Token);   // saldırgan IP:port'a bağlan
if (png is null || AvatarTags.ForImageBytes(png) != tag) return;       // saldırgan tag+png'yi ikisini de kontrol eder
ImageSource image = LoadFrozen(png);                                    // WIC/WPF ile çöz (güvenilmeyen)
```
**Saldırı senaryosu:** (1) Eşleşmemiş herhangi bir cihaz `Purpose=avatar` ile bağlanıp kurbanın
**özel avatar fotoğrafını** çekebilir (gizlilik sızıntısı). (2) Saldırgan sahte bir cihazı `img:`
etiketiyle duyurur → kurban **otomatik olarak** saldırganın IP:port'una bağlanır, ≤512 KB PNG
indirir ve **WIC görüntü ayrıştırıcısıyla çözer** — hiçbir kullanıcı etkileşimi/eşleştirme yok.
Bütünlük kontrolü (`ForImageBytes(png) == tag`) yalnızca baytların ilan edilen etikete uymasını
sağlar; saldırgan ikisini de belirlediği için kötücül görüntüyü engellemez. Bu, hem zorlanmış
giden bağlantı (LAN tarama/teyit) hem de görüntü ayrıştırıcı saldırı yüzeyi demektir.
**Önerilen düzeltme:** Avatar sunumunu ve otomatik çekmeyi **yalnızca eşleşmiş (güvenilir)
cihazlarla** yap. Alternatif: özel fotoğraf çekmeyi kullanıcı o cihazla ilk kez etkileşene kadar
ertele; presetleri (yalnızca etiket) eşleştirmeden göster.
**Güven:** Kesin

---

## 🟡 DÜŞÜK

### [DÜŞÜK] Offer'da yinelenen `fileId` sessiz veri kaybına yol açar
**Konum:** `ReceiveSession.cs:89`
**Kanıt:** `_slots[file.Id] = slot;` — teklif iki `FileEntry`'yi aynı `Id` ile içerirse önceki
slot ezilir; o dosya hiç alınmaz ama transfer "tamamlandı" görünebilir (`_remainingFiles`
dedupe sonrası sayılıyor).
**Saldırı senaryosu:** Kötü niyetli/hatalı gönderen çakışan id'lerle bazı dosyaların sessizce
kaybolmasına yol açar (bütünlük/sağlamlık).
**Önerilen düzeltme:** Offer alımında `Files` içinde id benzersizliğini doğrula; çakışma varsa
teklifi reddet.
**Güven:** Kesin

### [DÜŞÜK] Resume'da diskteki parçalar yeniden hash'lenmeden atlanıyor
**Konum:** `ReceiveSession.cs:75-84`, `TransferSender.cs:109-111`
**Kanıt:** `PartialState`'ten okunan "tamamlanmış" chunk indeksleri `Done`'a ekleniyor ve gönderen
bunları hiç göndermiyor; diskteki baytlar doğrulanmıyor. TLS+xxHash yalnızca **aktarılan** baytları
korur.
**Saldırı senaryosu:** Yerel olarak bozulmuş/kurcalanmış kısmi dosya veya `.lanbeam-partial.json`
→ sonuç sessizce bozuk. Yerel tehdit; düşük.
**Önerilen düzeltme:** Resume'da tamamlanmış parçaları diskten okuyup xxHash ile doğrula; uyuşmazsa
yeniden iste. En azından imzaya dosya son-değişiklik zamanını da kat.
**Güven:** Kesin

### [DÜŞÜK] Alıcı tarafında pinning asimetrisi (bypass yok, zayıf uyarı sinyali)
**Konum:** `TransferListener.cs:132` (`_trust.IsTrusted(peerFp)`) vs `TransferSender.cs:58-65`
**Kanıt:** Gönderen, bilinen bir `DeviceId`'nin fingerprint'i değişmişse transferi engelleyip
kullanıcıyı uyarır. Alıcıda karşılığı yok: alıcı yalnızca fingerprint'e göre `IsTrusted` bakar;
bilinen bir DeviceId'nin farklı fingerprint ile gelmesi **bypass değildir** (eşleşme yine PIN
ister) ama "bu cihazın kimliği değişmiş" uyarısı üretilmez — sadece yeni bir cihaz gibi görünür.
**Saldırı senaryosu:** Saldırgan bilinen bir cihazın DeviceId+adını taklit eder; alıcı fingerprint
farklı olduğundan güvenmez ve PIN ister. Fingerprint TLS'e bağlı olduğundan gerçek bypass yok;
risk sosyal mühendislik sinyalinin eksikliği (kullanıcı tanıdık ad görüp PIN girmeye ikna olabilir).
**Önerilen düzeltme:** Alıcıda da DeviceId→beklenen fingerprint eşlemesi tutup uyuşmazlıkta belirgin
"kimlik değişti" uyarısı göster.
**Güven:** Olası

### [DÜŞÜK] Sentinel/resend dalga yarışı ve çift `FinalizeSuccess`
**Konum:** `ReceiveSession.cs:196` (WriteChunk'ta finalize) ve `:240` (OnSentinel'de finalize),
dalga mantığı `:227-254`
**Kanıt:** Son parça ile `FinalizeSuccess` ve eksik-yok durumunda `OnSentinel` içinden
`FinalizeSuccess` eşzamanlı çağrılabilir. `_outcome.TrySetResult` idempotent olsa da
`FinalizeSuccess` gövdesi (sıfır-bayt dosya oluşturma, `PartialState.Delete`) iki kez koşabilir.
Ayrıca yavaş bir akışın parçası, N. sentinel işlendiğinde henüz `Done` işaretlenmemişse gereksiz
resend tetiklenir (kendini onarır ama verimsiz).
**Saldırı senaryosu:** Güvenlik değil; nadir yük/zamanlama koşullarında gereksiz yeniden gönderim
ve çift finalize (pratikte zararsız gözlemlendi, ama kırılgan).
**Önerilen düzeltme:** `FinalizeSuccess`'i tek girişli kıl (örn. `Interlocked.Exchange` bayrağı
ile ilk çağırana kilitle). Dalga sayaç geçişini tek kilit altında yap.
**Güven:** Olası

### [DÜŞÜK] `SettingsView` event aboneliği çözülmüyor (sızıntı)
**Konum:** `src/LanBeam.App/Views/SettingsView.xaml.cs` — `LoadFromSettings` içinde
`App.Node.TrustedDevices.Changed += ...` (her `Loaded`'da, `-=` yok)
**Kanıt:** Görünüm yeniden yüklenirse (unload/reload) handler birikir → `RefreshTrustedList`
çoklu tetiklenir. Uygulama ömrü boyu yaşayan `Node`'a bağlı olduğundan pratikte küçük.
**Önerilen düzeltme:** `Unloaded`'da abonelikten çık ya da ctor'da bir kez abone ol.
**Güven:** Kesin

### [DÜŞÜK] Path çözümlemede ayrılmış aygıt adları / normalizasyon sertleştirmesi
**Konum:** `FileTreeScanner.cs:99-105`
**Kanıt:** Path traversal savunması **sağlam** (`..`, rooted, `:` reddi + `GetFullPath` sonrası
`StartsWith(rootFull)`). Ancak ayrılmış aygıt adları (`CON`, `PRN`, `NUL`, `AUX`, `COM1`…) hedef
kök altında kalsa da Windows'ta özel yorumlanabilir; ayrıca sondaki nokta/boşluk `GetFullPath`
tarafından normalize edilerek farklı dosyaya yazım olabilir.
**Saldırı senaryosu:** Kötü niyetli gönderen `RelativePath` = `.../CON` göndererek yazımı bir
aygıta yönlendirebilir veya istisna oluşturabilir (küçük olasılık).
**Önerilen düzeltme:** Yol bileşenlerini ayrılmış adlara ve sondaki nokta/boşluğa karşı da doğrula.
**Güven:** Olası

### [DÜŞÜK] `FirewallHelper` komut satırı string interpolasyonu
**Konum:** `src/LanBeam.App/Services/FirewallHelper.cs`
**Kanıt:** `cmd.exe /c netsh ... program="{exePath}"` biçiminde interpolasyon + `Verb=runas`.
`exePath = Environment.ProcessPath` (uzaktaki saldırgan kontrolünde **değil**), dolayısıyla uzaktan
enjeksiyon yok; ama kurulum yolu özel karakter içerirse komut bozulabilir. Temiz-kod/sertleştirme.
**Önerilen düzeltme:** Mümkünse `ProcessStartInfo.ArgumentList` kullan; ya da yolu titizce escape et.
**Güven:** Olası

### [DÜŞÜK] Named pipe varsayılan ACL — doğrulanmalı
**Konum:** `src/LanBeam.App/Services/SingleInstance.cs:44`
**Kanıt:** `NamedPipeServerStream(..., PipeDirection.In, 1, ...)` açık `PipeSecurity` olmadan
oluşturuluyor. Varsayılan DACL Windows'ta aynı kullanıcıyı yetkilendirir; başka bir kullanıcı
yazamamalı. Aynı kullanıcı olarak koşan başka bir süreç pipe'a bağlanıp `--send` penceresi
tetikleyebilir (aynı güven sınırı → düşük).
**Saldırı senaryosu:** Aynı oturumda düşük güvenli bir süreç, kurbanın oturumunda dosya seçim
penceresi açtırabilir. Yerel, düşük.
**Önerilen düzeltme:** Açık `PipeSecurity` ile yalnızca mevcut kullanıcıya izin ver; gelen mesaj
uzunluğunu sınırla (şu an `ReadToEnd` sınırsız). Çapraz-kullanıcı erişiminin gerçekten engellendiğini
test et.
**Güven:** Olası

---

## Doğrulama / İnceleme Gerektiren (kesinleştirilemedi)

- **Sentinel/resend yük altında (paket kaybı simülasyonu):** Bulgu 8'in gerçek bir asılma
  (hang) üretip üretmediği ancak yapay chunk-drop + yavaş akış testiyle kesinleşir. Öneri:
  `ReceiveSession`'a doğrudan bozuk/eksik chunk besleyen birim testi.
- **Named pipe çapraz-kullanıcı erişimi (Bulgu 12):** Varsayılan ACL'in gerçekten diğer
  kullanıcıları engellediği ayrı bir kullanıcı hesabıyla doğrulanmalı.

## Test Kapsamı Boşlukları (plan Bölüm D)

Mevcut 25 test sağlam ama **düşmanca girdi testleri yok**:
- Bozuk/aşırı büyük JSON çerçevesi (Bulgu 1).
- Geçersiz veri başlığı (negatif/taşan `offset`/`length`, bilinmeyen `fileId`), devasa
  `Entry.Size` (Bulgu 2).
- Yinelenen `fileId` (Bulgu 5).
- Path traversal için genişletilmiş vektörler (UNC, aygıt adları, sondaki nokta/boşluk — Bulgu 10).
- Eşleşmemiş cihazın Data/Avatar bağlantısı denemeleri (Bulgu 3, 4).
- Fingerprint değişince transferin engellenmesi (gönderen pinning — pozitif yolun testi).

## Sonuç

Mimari ve kriptografik temel **sağlam**: eşleştirme protokolü doğru kurgulanmış, TLS kimliği
bağlıyor, path traversal katmanlı savunuluyor, bağımlılıklar temiz. Ana açıklar **kaynak
tüketimi (DoS)** ekseninde: kimlik doğrulamasız 256 MB tahsis (Yüksek) ve sınırsız
bağlantı/zaman aşımı eksikliği (Orta) birlikte, aynı ağdaki bir saldırganın uygulamayı
düşürmesine izin verir. Avatar yüzeyi (Orta) eşleştirme öncesi güvenilmeyen içerik işliyor.
Bunlar giderilmeden internete/güvenilmeyen ağa açık kullanım önerilmez; yalnızca güvenilen ev/ofis
LAN'ında risk kabul edilebilir. Düşük bulgular çoğunlukla sağlamlık ve temiz-kod niteliğinde.
