# LanBeam — macOS build

The macOS app is built from `src/LanBeam.Avalonia` (Avalonia UI), which reuses the same
`LanBeam.Core` engine as the Windows (WPF) app — so **macOS ↔ Windows transfers work out of the box.**

## Requirements
- macOS 11+ and the **.NET 8 SDK** (`brew install dotnet-sdk` or from dotnet.microsoft.com).

## Build the `.app`
```bash
cd src/LanBeam.Avalonia/macos
chmod +x build-macos.sh

./build-macos.sh            # Apple Silicon (M1/M2/M3…)
# or
./build-macos.sh osx-x64    # Intel Mac
```
Output: `src/LanBeam.Avalonia/bin/macos/LanBeam.app` — drag it into **Applications**.

First launch (unsigned app): **right-click → Open** to get past Gatekeeper, then confirm.

Optional `.dmg`:
```bash
hdiutil create -volname LanBeam -srcfolder bin/macos/LanBeam.app -ov -format UDZO bin/macos/LanBeam.dmg
```

## Notes
- On first run macOS asks to **allow incoming connections** (firewall) and, on macOS 15+, for
  **local network** access — both are needed for discovery and transfers. Allow them.
- No right-click Finder menu on macOS (by design): open the app, pick a device on the **Devices**
  tab, then **Send Files / Folder** (or drag files onto a device card).
- The identity certificate's private key is stored in the app data folder with owner-only
  permissions (chmod 600) instead of Windows DPAPI.

## Dev (run without bundling)
```bash
dotnet run --project src/LanBeam.Avalonia
```
This also runs on Windows/Linux, which is handy for development.
