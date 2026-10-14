#!/usr/bin/env bash
# Installs Browser Wrangler from an extracted release tarball, per user (no root needed).
#
#   ./install.sh               install into ~/.local/opt/browser-wrangler and register
#   ./install.sh --uninstall   unregister and remove
#
# Set PREFIX to install somewhere other than ~/.local.
set -euo pipefail

PREFIX="${PREFIX:-$HOME/.local}"
DEST="$PREFIX/opt/browser-wrangler"
BIN_DIR="$PREFIX/bin"
LINK="$BIN_DIR/browser-wrangler"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--uninstall" ]]; then
    if [[ -x "$DEST/browser-wrangler" ]]; then
        "$DEST/browser-wrangler" --unregister || true
    fi
    if [[ -L "$LINK" ]]; then
        rm -f "$LINK"
    fi
    rm -rf "$DEST"
    echo "Browser Wrangler removed. Your settings in ${XDG_CONFIG_HOME:-$HOME/.config} were kept."
    exit 0
fi

if [[ ! -x "$HERE/browser-wrangler" ]]; then
    echo "error: run this script from the extracted Browser Wrangler tarball." >&2
    exit 1
fi

if [[ "$HERE" != "$DEST" ]]; then
    rm -rf "$DEST"
    mkdir -p "$DEST"
    cp -a "$HERE"/. "$DEST"/
fi

mkdir -p "$BIN_DIR"
ln -sf "$DEST/browser-wrangler" "$LINK"

"$DEST/browser-wrangler" --register

echo "Browser Wrangler installed to $DEST and registered as a browser."
echo "Run 'browser-wrangler --make-default' (or use the Health page) to make it your default browser."
case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *) echo "note: $BIN_DIR is not on your PATH." ;;
esac
