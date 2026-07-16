#!/usr/bin/env bash
# LanBeam macOS .app paketleyici. macOS'ta .NET 8 SDK ile çalıştırın.
#
#   ./build-macos.sh              # Apple Silicon (arm64)
#   ./build-macos.sh osx-x64      # Intel Mac
#
# Çıktı: bin/macos/LanBeam.app  (Applications'a sürükleyin)
set -euo pipefail

RID="${1:-osx-arm64}"
CONFIG=Release
PROJ_DIR="$(cd "$(dirname "$0")/.." && pwd)"      # LanBeam.Avalonia klasörü
OUT="$PROJ_DIR/bin/macos"
APPDIR="$OUT/LanBeam.app"

echo "==> Yayınlanıyor ($RID) ..."
rm -rf "$OUT"
mkdir -p "$OUT/publish"
dotnet publish "$PROJ_DIR/LanBeam.Avalonia.csproj" -c "$CONFIG" -r "$RID" \
  --self-contained true -p:PublishSingleFile=false -o "$OUT/publish"

echo "==> .app iskeleti oluşturuluyor ..."
mkdir -p "$APPDIR/Contents/MacOS" "$APPDIR/Contents/Resources"
cp -R "$OUT/publish/." "$APPDIR/Contents/MacOS/"
cp "$PROJ_DIR/macos/Info.plist" "$APPDIR/Contents/Info.plist"
chmod +x "$APPDIR/Contents/MacOS/LanBeam"

echo "==> İkon (.icns) üretiliyor ..."
ICONSET="$OUT/lanbeam.iconset"
mkdir -p "$ICONSET"
PNG="$PROJ_DIR/Assets/lanbeam.png"
for s in 16 32 128 256 512; do
  sips -z $s $s "$PNG" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$PNG" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APPDIR/Contents/Resources/lanbeam.icns"
rm -rf "$ICONSET"

echo "==> Ad-hoc imzalanıyor (Gatekeeper için) ..."
codesign --force --deep --sign - "$APPDIR" || echo "(codesign atlandı)"

echo ""
echo "Tamam: $APPDIR"
echo "İlk açılışta: Finder'da sağ tık > Aç (imzasız uygulama uyarısını geçmek için)."
echo "İsterseniz DMG: hdiutil create -volname LanBeam -srcfolder \"$APPDIR\" -ov -format UDZO \"$OUT/LanBeam.dmg\""
