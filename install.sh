#!/bin/sh
# Installs Lorestead on Linux for the current user: the latest release's
# AppImage goes to ~/.local/share/Lorestead, plus an app menu entry and icon.
#
#   curl -fsSL https://raw.githubusercontent.com/rthomasv3/Lorestead/main/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/rthomasv3/Lorestead/main/install.sh | sh -s -- --uninstall
#
# No root, on purpose: Velopack updates the AppImage by replacing the file in
# place, so it must stay owned by the user who runs it. Re-running installs the
# latest release over the top. Everything runs inside main(), called on the last
# line, so a download cut off mid-transfer can't execute a partial script.
set -eu

APP_NAME="Lorestead"
APP_ID="io.github.rthomasv3.Lorestead"
DOWNLOAD_URL="https://github.com/rthomasv3/Lorestead/releases/latest/download/Lorestead-linux-x64.AppImage"

# The AppImage shares ~/.local/share/Lorestead with the webview's storage, so
# uninstall removes only the files this script creates - never the directory,
# and never the database in ~/.lorestead.
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/$APP_NAME"
APPIMAGE_PATH="$INSTALL_DIR/$APP_NAME.AppImage"
DESKTOP_FILE="$DATA_HOME/applications/$APP_ID.desktop"
ICON_FILE="$DATA_HOME/icons/hicolor/256x256/apps/$APP_ID.png"
# Velopack's downloaded update packages; Velopack hardcodes /var/tmp.
UPDATE_CACHE_DIR="/var/tmp/velopack/$APP_NAME"

TMP_DIR=""
PART_FILE=""

say() {
    printf '%s\n' "$*"
}

fail() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

cleanup() {
    if [ -n "$PART_FILE" ]; then
        rm -f "$PART_FILE"
    fi
    if [ -n "$TMP_DIR" ]; then
        rm -rf "$TMP_DIR"
    fi
}

download() {
    if command -v curl >/dev/null 2>&1; then
        curl -fL --progress-bar -o "$2" "$1"
    elif command -v wget >/dev/null 2>&1; then
        wget -O "$2" "$1"
    else
        fail "curl or wget is required to download Lorestead."
    fi
}

check_system() {
    [ "$(uname -s)" = "Linux" ] || fail "this installer is for Linux only."
    [ "$(uname -m)" = "x86_64" ] || fail "Lorestead is only built for x86_64 Linux (this is $(uname -m))."
    # Under sudo, $HOME is often root's and every file would be root-owned,
    # which breaks in-place updates.
    [ "$(id -u)" -ne 0 ] || fail "run this as your normal user, not root or sudo."
}

install_app() {
    check_system

    say "Installing $APP_NAME to $APPIMAGE_PATH"
    mkdir -p "$INSTALL_DIR" "$(dirname "$DESKTOP_FILE")" "$(dirname "$ICON_FILE")"

    # Download beside the target, then rename: the swap is atomic, and safe
    # even while an older copy is running.
    PART_FILE="$INSTALL_DIR/.$APP_NAME.AppImage.part"
    download "$DOWNLOAD_URL" "$PART_FILE"
    chmod +x "$PART_FILE"

    # The AppImage runtime extracts its own icon - no FUSE or extra tools needed.
    TMP_DIR="$(mktemp -d)"
    if (cd "$TMP_DIR" && "$PART_FILE" --appimage-extract .DirIcon >/dev/null 2>&1) \
        && [ -f "$TMP_DIR/squashfs-root/.DirIcon" ]; then
        cp "$TMP_DIR/squashfs-root/.DirIcon" "$ICON_FILE"
    else
        say "Warning: couldn't extract the app icon; the menu entry will use a generic one."
    fi

    mv -f "$PART_FILE" "$APPIMAGE_PATH"
    PART_FILE=""

    cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
GenericName=Notes and Tasks
Comment=Self-hostable notes and tasks, built for AI agents
Exec="$APPIMAGE_PATH"
TryExec=$APPIMAGE_PATH
Icon=$APP_ID
Terminal=false
Categories=Office;Utility;
Keywords=notes;tasks;markdown;sync;
StartupNotify=true
StartupWMClass=$APP_NAME
EOF

    refresh_menus

    if ! command -v fusermount3 >/dev/null 2>&1 && ! command -v fusermount >/dev/null 2>&1; then
        say ""
        say "Warning: no fusermount found. AppImages need FUSE to run - install your"
        say "distro's fuse3 package (e.g. 'sudo apt install fuse3')."
    fi

    say ""
    say "$APP_NAME is installed and will appear in your app menu."
    say "It updates itself in place - from Settings, or with auto-update on."
    say "For local AI agents (MCP), point the agent config at:"
    say "  $APPIMAGE_PATH --mcp"
}

uninstall_app() {
    rm -f "$APPIMAGE_PATH" "$DESKTOP_FILE" "$ICON_FILE"
    rm -rf "$UPDATE_CACHE_DIR"
    refresh_menus
    say "$APP_NAME has been uninstalled. Your notes and settings in ~/.lorestead were kept."
}

refresh_menus() {
    # Desktops watch the applications directory on their own; this only nudges
    # icon caches. No update-desktop-database: the entry declares no MimeType,
    # and a user-level mimeinfo.cache would go stale as other apps add entries.
    touch "$DATA_HOME/icons/hicolor" 2>/dev/null || true
}

main() {
    trap cleanup EXIT
    trap 'exit 1' INT TERM

    case "${1:-}" in
        "")
            install_app
            ;;
        --uninstall)
            uninstall_app
            ;;
        *)
            fail "unknown option '$1' (the only option is --uninstall)."
            ;;
    esac
}

main "$@"
