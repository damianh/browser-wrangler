#!/usr/bin/env bash
# Builds the Linux release artifacts: a Native AOT tarball and (optionally) an AppImage.
#
#   installer/linux/build.sh [--appimage]
#
# Environment:
#   ARCH            x64 (default) or arm64; must match the build machine (no cross-compiling)
#   VERSION         version to stamp (default 1.0.0)
#   CHANNEL         stable (default) or dev
#   ARTIFACT_PREFIX file name prefix (default BrowserWrangler)
#   OUT_DIR         output directory (default ./artifacts)
#   APPIMAGETOOL    path to appimagetool (downloaded when not set)
set -euo pipefail

ARCH="${ARCH:-x64}"
VERSION="${VERSION:-1.0.0}"
CHANNEL="${CHANNEL:-stable}"
ARTIFACT_PREFIX="${ARTIFACT_PREFIX:-BrowserWrangler}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$(mkdir -p "${OUT_DIR:-$ROOT/artifacts}" && cd "${OUT_DIR:-$ROOT/artifacts}" && pwd)"
HERE="$ROOT/installer/linux"

if [[ "$CHANNEL" == "dev" ]]; then
    APP_ID="browser-wrangler-dev"
    PROTOCOL="x-bw-dev"
    APP_NAME="Browser Wrangler (Dev Channel)"
else
    APP_ID="browser-wrangler"
    PROTOCOL="x-bw"
    APP_NAME="Browser Wrangler"
fi

case "$ARCH" in
    x64) APPIMAGE_ARCH="x86_64" ;;
    arm64) APPIMAGE_ARCH="aarch64" ;;
    *) echo "error: unsupported ARCH '$ARCH'" >&2; exit 1 ;;
esac

NAME="browser-wrangler-linux-$ARCH"
STAGE="$OUT_DIR/stage/$NAME"
rm -rf "$STAGE"

dotnet publish "$ROOT/src/BrowserWrangler.Linux" -c Release \
    -r "linux-$ARCH" \
    -p:PublishAot=true \
    --self-contained \
    -p:Version="$VERSION" \
    -p:ReleaseChannel="$CHANNEL" \
    -o "$STAGE"

# Debug symbols and the static SQLite archive are not needed at runtime.
rm -f "$STAGE"/*.dbg "$STAGE"/*.pdb "$STAGE"/*.a
cp "$HERE/install.sh" "$ROOT/LICENSE" "$ROOT/NOTICE" "$STAGE/"
chmod +x "$STAGE/install.sh" "$STAGE/browser-wrangler"

TARBALL="$OUT_DIR/$ARTIFACT_PREFIX-$VERSION-linux-$ARCH.tar.gz"
tar -C "$OUT_DIR/stage" -czf "$TARBALL" "$NAME"
echo "Created $TARBALL"

if [[ "${1:-}" != "--appimage" ]]; then
    exit 0
fi

APPDIR="$OUT_DIR/stage/AppDir-$ARCH"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp -a "$STAGE"/. "$APPDIR/usr/bin/"
rm -f "$APPDIR/usr/bin/install.sh"

sed -e "s/^Name=.*/Name=$APP_NAME/" \
    -e "s/^Icon=.*/Icon=$APP_ID/" \
    -e "s/^StartupWMClass=.*/StartupWMClass=$APP_ID/" \
    -e "s/x-scheme-handler\/x-bw;/x-scheme-handler\/$PROTOCOL;/" \
    "$HERE/browser-wrangler.desktop" > "$APPDIR/$APP_ID.desktop"
cp "$STAGE/logo-256.png" "$APPDIR/$APP_ID.png"
cp "$STAGE/logo-256.png" "$APPDIR/.DirIcon"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/browser-wrangler" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

if [[ -z "${APPIMAGETOOL:-}" ]]; then
    APPIMAGETOOL="$OUT_DIR/stage/appimagetool-$APPIMAGE_ARCH.AppImage"
    if [[ ! -x "$APPIMAGETOOL" ]]; then
        curl -fsSL -o "$APPIMAGETOOL" \
            "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$APPIMAGE_ARCH.AppImage"
        chmod +x "$APPIMAGETOOL"
    fi
fi

APPIMAGE="$OUT_DIR/$ARTIFACT_PREFIX-$VERSION-linux-$APPIMAGE_ARCH.AppImage"
ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --no-appstream "$APPDIR" "$APPIMAGE"
echo "Created $APPIMAGE"
