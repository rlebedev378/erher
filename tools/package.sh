#!/usr/bin/env bash
# Збирає переносиме видання: dist/Varta/ і dist/Varta-portable.zip
# Потрібні: .NET SDK 8+ (збірка таргетить .NET Framework 4.8 через reference assemblies), zip.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/src/Varta/bin/Release/net48"
OUT="$ROOT/dist/Varta"

dotnet build -c Release "$ROOT/src/Varta/Varta.csproj" --nologo

rm -rf "$OUT"
mkdir -p "$OUT/sounds"
cp "$BIN/Varta.exe" "$BIN/Varta.exe.config" "$OUT/"
cp "$ROOT"/src/Varta/Assets/default_sounds/*.mp3 "$OUT/sounds/"

# Блокнот Windows: UTF-8 з BOM і CRLF
for doc in README_UA.txt THIRD_PARTY_NOTICES.txt; do
  { printf '\xEF\xBB\xBF'; sed 's/\r$//; s/$/\r/' "$ROOT/docs/$doc"; } > "$OUT/$doc"
done

( cd "$ROOT/dist" && rm -f Varta-portable.zip && zip -qr -X Varta-portable.zip Varta )

# Оновлення наявної установки: лише програма (налаштування й звуки не зачіпаються)
UPD="$ROOT/dist/_update"
rm -rf "$UPD" && mkdir -p "$UPD"
cp "$OUT/Varta.exe" "$OUT/Varta.exe.config" "$OUT/THIRD_PARTY_NOTICES.txt" "$UPD/"
( cd "$UPD" && rm -f ../Varta-update.zip && zip -qr -X ../Varta-update.zip . )
rm -rf "$UPD"
echo "Готово: $ROOT/dist/Varta-portable.zip, $ROOT/dist/Varta-update.zip"
