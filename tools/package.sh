#!/usr/bin/env bash
# Збирає переносиме видання: dist/Varta/, dist/Varta-portable.zip, dist/Varta-update.zip і архів вихідного коду dist/Varta-source-<версія>.zip
# Потрібні: .NET SDK 8+ (збірка таргетить .NET Framework 4.8 через reference assemblies), zip.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/src/Varta/bin/Release/net48"
OUT="$ROOT/dist/Varta"
VER="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Varta/Varta.csproj" | head -1)"

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

# Вихідний код (без bin/obj/dist): у git-репозиторії — файли, що не ігноруються; інакше — уся папка з винятками
SRC_TMP="$ROOT/dist/_source"
rm -rf "$SRC_TMP" && mkdir -p "$SRC_TMP/Varta-source"
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  ( cd "$ROOT" && git ls-files -co --exclude-standard -z | { grep -zv '^dist/' || true; } | tar --null -T - -cf - ) | tar -xf - -C "$SRC_TMP/Varta-source"
else
  ( cd "$ROOT" && tar --exclude='./dist' --exclude='./.git' --exclude='./.vs' --exclude='*/bin' --exclude='*/obj' --exclude='*/TestResults' --exclude='*.user' -cf - . ) | tar -xf - -C "$SRC_TMP/Varta-source"
fi
( cd "$SRC_TMP" && rm -f "../Varta-source-$VER.zip" && zip -qr -X "../Varta-source-$VER.zip" Varta-source )
rm -rf "$SRC_TMP"

echo "Готово ($VER): $ROOT/dist/Varta-portable.zip, $ROOT/dist/Varta-update.zip, $ROOT/dist/Varta-source-$VER.zip"
