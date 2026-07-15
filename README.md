# LanBeam

**Fast, encrypted file transfer over your local network — for Windows.**
Like Steam's "transfer over local network", but for **any file or folder**. Two PCs discover
each other automatically; files move over an encrypted connection at gigabit speed (~110+ MB/s).

*English below · [Türkçe için tıklayın](#türkçe)*

---

## Features

- **Automatic discovery** — devices running LanBeam on the same network find each other via UDP multicast.
- **Maximum speed** — files are split into 16 MB chunks and sent over **4 parallel TLS streams**
  (1–8, configurable). A single stream already saturates wired gigabit; parallel streams help on
  Wi-Fi and 2.5G+.
- **End-to-end encryption** — all traffic is inside TLS 1.2/1.3. Hardware-accelerated AES means
  encryption costs no speed.
- **PIN pairing (MITM protection)** — on the first transfer the receiver shows a 6-digit code that
  the sender enters. A man-in-the-middle can't pass the check without the PIN. Paired devices are
  remembered and verified by certificate pinning afterwards.
- **Accept / reject + destination** — the receiver sees who wants to send what (name, size, file
  count) and chooses where to save.
- **Resume** — an interrupted transfer restarted to the same folder skips already-completed chunks
  (verified on disk with xxHash3).
- **Integrity** — every chunk is verified; corrupted chunks are re-requested automatically.
- **Right-click menu** — right-click a file/folder → "Send with LanBeam".
- **Runs in the tray** — keeps receiving in the background after the window is closed.
- **Drag & drop**, **custom avatars**, **English / Turkish UI**.

## Install

Download `LanBeam.App.exe` from the [Releases](../../releases), run it, then go to
**Settings → System → "Install to a fixed location + desktop shortcut"**. This copies the app under
`%LOCALAPPDATA%\LanBeam`, creates a desktop shortcut, and points the right-click menu / auto-start
there — so you can delete the downloaded copy and everything keeps working.

> Requires Windows 10/11 (x64). No .NET installation needed — the exe is self-contained.

## Usage

1. Open LanBeam on **both** computers (same network).
2. Approve the Windows Firewall prompt on first run (needed to receive files).
3. On the Devices tab, when the other PC appears, click **Send Files / Folder** (or drag files
   onto its card).
4. First time only: enter the 6-digit code shown on the receiver into the sender (one-time pairing).
5. The receiver accepts and picks a folder. Speed and time remaining show on the Transfers tab.

## Build from source

```bash
dotnet build                 # build everything
dotnet test                  # 35 tests: unit + loopback E2E + resume + discovery + security
dotnet run --project src/LanBeam.App

# Two copies on one machine for local testing:
powershell -ExecutionPolicy Bypass -File scripts/dev-iki-kopya.ps1

# Single-file release build (no .NET required to run) -> publish/LanBeam.App.exe:
dotnet publish src/LanBeam.App -p:PublishProfile=win-x64
```

Requires the **.NET 8 SDK**.

## Architecture

```
src/LanBeam.Core   UI-less core: discovery, security, protocol, transfer engine
src/LanBeam.App    WPF UI (WPF-UI / Fluent, tray, dialogs, Explorer integration, i18n)
tests/LanBeam.Tests
```

| Layer | How it works |
|---|---|
| Discovery | UDP multicast `239.255.42.99:45654` — announce every 5 s + unicast reply, drop after 15 s |
| Identity | Per-device persistent self-signed ECDSA P-256 cert (stored encrypted via DPAPI); identity = SHA-256 fingerprint |
| Pairing | 6-digit PIN + HMAC-SHA256 mutual proof over fingerprints + nonces, 3-attempt limit |
| Control channel | TLS over TCP 45655 with length-prefixed JSON (offer, accept/reject, progress, cancel) |
| Data channels | N parallel TLS connections; 16 MB chunks from one work queue; xxHash3 integrity; `RandomAccess.Write` to offset |
| Resume | `.lanbeam-partial.json` (relative path → completed chunk hashes); chunks re-verified on disk before skipping |

## Security

LanBeam has been through an independent security review; all findings were fixed (see
[GUVENLIK-DENETIM-RAPORU.md](GUVENLIK-DENETIM-RAPORU.md), in Turkish). Highlights: tiered frame
limits against pre-auth memory DoS, connection caps + timeouts, disk-space checks, avatar exposure
limited to paired devices, and resume integrity re-verification.

Suitable for trusted home/office LANs. As with any peer-to-peer app, use it on networks you trust.

## Roadmap / not yet

- Windows 11 top-level right-click menu (MSIX + IExplorerCommand)
- macOS / Linux (the core is already UI-agnostic)
- Optional LZ4 compression

---

## Türkçe

**LAN üzerinde hızlı ve şifreli dosya transferi — Windows için.**
Steam'in "yerel ağ üzerinden aktarım" özelliği gibi, ama **her tür dosya ve klasör** için. İki PC
birbirini otomatik bulur; dosyalar şifreli bağlantı üzerinden gigabit hızında (~110+ MB/s) aktarılır.

### Özellikler

- **Otomatik keşif** — aynı ağdaki LanBeam açık cihazlar birbirini UDP multicast ile bulur.
- **Maksimum hız** — dosyalar 16 MB parçalara bölünüp **4 paralel TLS akışı** üzerinden gönderilir
  (1–8 ayarlanabilir). Kablolu gigabit'i tek akış bile doyurur; paralel akışlar Wi-Fi ve 2.5G+'da kazandırır.
- **Uçtan uca şifreleme** — tüm trafik TLS 1.2/1.3 içinde; donanım hızlandırmalı AES sayesinde hız kaybı yok.
- **PIN eşleştirme (MITM koruması)** — ilk transferde alıcının ekranındaki 6 haneli kodu gönderen girer;
  eşleşen cihazlar sertifika sabitleme ile hatırlanır.
- **Kabul/ret + konum seçimi**, **devam ettirme (resume)**, **xxHash3 bütünlük doğrulaması**.
- **Sağ tık menüsü**, **tray'de arka planda çalışma**, **sürükle-bırak**, **avatarlar**, **Türkçe/İngilizce arayüz**.

### Kurulum

[Releases](../../releases) bölümünden `LanBeam.App.exe`'yi indirip çalıştırın, sonra
**Ayarlar → Sistem → "Sabit konuma kur + masaüstü kısayolu"** deyin. Bu, uygulamayı
`%LOCALAPPDATA%\LanBeam` altına kopyalar, masaüstü kısayolu oluşturur ve sağ tık menüsü / otomatik
başlatmayı oraya yönlendirir — indirdiğiniz kopyayı silebilirsiniz. (Windows 10/11 x64; .NET kurulumu gerekmez.)

### Kullanım

1. Uygulamayı **iki bilgisayarda da** açın (aynı ağda).
2. İlk çalıştırmada güvenlik duvarı iznini onaylayın.
3. Cihazlar sekmesinde karşı PC göründüğünde **Dosya Gönder / Klasör** deyin (ya da sürükleyin).
4. İlk seferde alıcı ekranındaki 6 haneli kodu gönderende girin (tek seferlik eşleştirme).
5. Alıcı kabul edip klasör seçer; hız ve kalan süre Transferler sekmesinde görünür.

### Geliştirme

```bash
dotnet build      # tümünü derle
dotnet test       # 35 test (birim + loopback E2E + resume + keşif + güvenlik)
dotnet publish src/LanBeam.App -p:PublishProfile=win-x64   # tek dosya exe
```
**.NET 8 SDK** gerekir. Mimari ve güvenlik ayrıntıları için yukarıdaki İngilizce bölüme bakın.

---

*Built with .NET 8 + WPF. 🤖 Developed with the help of [Claude Code](https://claude.com/claude-code).*
