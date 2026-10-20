#!/bin/bash
# Build dist/toggle-internal-keyboard_<version>_all.deb
set -euo pipefail
cd "$(dirname "$0")"
PKG=toggle-internal-keyboard
ID=io.github.hafij_bge.ToggleInternalKeyboard
VERSION=$(sed -n 's/^VERSION = "\(.*\)"/\1/p' src/internal-keyboard)
STAGE=$(mktemp -d)
chmod 755 "$STAGE"
trap 'rm -rf "$STAGE"' EXIT

install -Dm755 src/internal-keyboard "$STAGE/usr/bin/internal-keyboard"
install -Dm755 src/helper "$STAGE/usr/libexec/$PKG/helper"
install -Dm755 src/watchdog "$STAGE/usr/libexec/$PKG/watchdog"
install -Dm644 data/$PKG-watchdog.service "$STAGE/usr/lib/systemd/system/$PKG-watchdog.service"
install -Dm644 data/$ID.desktop "$STAGE/usr/share/applications/$ID.desktop"
install -Dm644 data/$ID.policy "$STAGE/usr/share/polkit-1/actions/$ID.policy"
install -Dm644 data/icon.svg "$STAGE/usr/share/icons/hicolor/scalable/apps/$ID.svg"

mkdir -p "$STAGE/DEBIAN"
cat > "$STAGE/DEBIAN/control" <<CTL
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: all
Depends: python3, python3-gi, gir1.2-gtk-4.0, gir1.2-adw-1 (>= 1.5), pkexec | policykit-1
Recommends: libnotify-bin
Maintainer: Hafij-BGE
Installed-Size: $(du -sk "$STAGE/usr" | cut -f1)
Description: Turn the laptop's built-in keyboard on or off
 GTK4/libadwaita app with a one-click switch for the internal (i8042)
 keyboard. Finds the keyboard by its port, so it survives event number
 changes. Can turn the keyboard back on automatically after a delay, and
 a watchdog service resets the keyboard when Shift gets stuck.
CTL
cat > "$STAGE/DEBIAN/postinst" <<'SH'
#!/bin/sh
set -e
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
    # Enable the stuck-Shift watchdog on first install (or upgrade from before it existed);
    # otherwise keep the user's choice.
    if [ -z "$2" ] || dpkg --compare-versions "$2" lt 1.1.0; then
        systemctl enable --now toggle-internal-keyboard-watchdog.service || true
    elif systemctl -q is-enabled toggle-internal-keyboard-watchdog.service; then
        systemctl restart toggle-internal-keyboard-watchdog.service || true
    fi
fi
SH
cat > "$STAGE/DEBIAN/prerm" <<'SH'
#!/bin/sh
set -e
if [ "$1" = remove ] && [ -d /run/systemd/system ]; then
    systemctl disable --now toggle-internal-keyboard-watchdog.service || true
fi
SH
cat > "$STAGE/DEBIAN/postrm" <<'SH'
#!/bin/sh
set -e
[ -d /run/systemd/system ] && systemctl daemon-reload || true
SH
chmod 755 "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/prerm" "$STAGE/DEBIAN/postrm"

mkdir -p dist
dpkg-deb --root-owner-group --build "$STAGE" "dist/${PKG}_${VERSION}_all.deb"
