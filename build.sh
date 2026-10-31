#!/usr/bin/env bash
# Builds the launcher (IroniteLauncher.exe) and its installer
# (dist/IroniteLauncher-Setup-<version>.exe). Needs Go 1.24+ and NSIS 3
# (makensis on the PATH, or NSISDIR pointing at an NSIS folder).
# The version comes from the VERSION file - bump it and add a CHANGELOG.md entry
# for every release. Games are not built in: the launcher downloads them from
# their own GitHub releases.
set -euo pipefail
cd "$(dirname "$0")"
ROOT=$PWD
VERSION=$(tr -d ' \r\n' < VERSION)
BUILD=$(date -u +%Y%m%d.%H%M)
OUT=$ROOT/dist
STAGE=$OUT/stage
rm -rf "$OUT" && mkdir -p "$STAGE"

# Check the changelog has an entry for this version.
grep -q "^## \[$VERSION\]" CHANGELOG.md || { echo "CHANGELOG.md has no entry for $VERSION"; exit 1; }

echo "== Resources ($VERSION)"
( cd tools/mkres && GOFLAGS=-mod=vendor go build -o "$OUT/mkres" . )
"$OUT/mkres" -version "$VERSION" -icon assets/icon-1024.png -out launcher/rsrc_windows_amd64.syso
cp CHANGELOG.md launcher/CHANGELOG.md

echo "== Launcher"
( cd launcher && GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath \
    -ldflags "-H windowsgui -s -w -X main.version=$VERSION -X main.build=$BUILD" \
    -o "$STAGE/IroniteLauncher.exe" . )

echo "== Installer"
if [ -n "${NSISDIR:-}" ] && [ -x "$NSISDIR/linux/makensis" ]; then
  MAKENSIS=("$NSISDIR/linux/makensis"); export NSISDIR
else
  MAKENSIS=(makensis)
fi
cp assets/icon.ico assets/wizard.bmp assets/header.bmp "$STAGE/"
sed 's/$/\r/' CHANGELOG.md > "$STAGE/CHANGELOG.txt"
sed "s/@VERSION@/$VERSION/g; s/\$/\r/" installer/README.txt > "$STAGE/README.txt"
SIZE_KB=$(( $(stat -c %s "$STAGE/IroniteLauncher.exe") / 1024 + 64 ))
"${MAKENSIS[@]}" -V2 \
  -DVERSION="$VERSION" -DSTAGE="$STAGE" -DSIZE_KB=$SIZE_KB -DPLUGINS="$ROOT/installer/plugins" \
  -DOUTFILE="$OUT/IroniteLauncher-Setup-$VERSION.exe" installer/setup.nsi

echo "== Done"
ls -la "$OUT"/*.exe "$STAGE/IroniteLauncher.exe"
