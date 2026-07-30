#!/usr/bin/env bash
# Builds the Lorestead Linux AppDir and packages it as an AppImage via Velopack
# (vpk pack), with webkit2gtk bundled so it runs on distros that don't ship
# GTK/WebKit. The custom AppRun adds a --mcp branch that execs the bundled MCP
# server, so the AppImage file itself is the stable path agent configs point at
# (decisions.md 2026-07-29).
#
# Usage: ./build-velopack.sh --version <semver>
#
# Must run in the jammy build environment (libwebkit2gtk-4.1-dev, .NET 10,
# node 24, vpk): the GitHub workflow uses ubuntu-22.04, docker-build.sh mirrors
# it locally. Paths are Ubuntu-only on purpose -- the harvested webkit binaries
# can only contain jammy's hardcoded paths, so other layouts would be dead code.
set -euo pipefail

PACK_VERSION=""
while [ $# -gt 0 ]; do
    case "$1" in
        --version)
            PACK_VERSION="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Usage: $0 --version <semver>" >&2
            exit 1
            ;;
    esac
done
if [ -z "$PACK_VERSION" ]; then
    echo "Error: --version <semver> is required (e.g. --version 0.1.0)" >&2
    exit 1
fi

APP_NAME="Lorestead"
MCP_NAME="Lorestead.Mcp"
APP_ID="io.github.rthomasv3.Lorestead"
# Substituted into webkit binaries in place of /usr paths -- the same-length
# guard below enforces the byte-for-byte length match binary patching requires.
WEBKIT_TMP="/tmp/lorestead-wbkt"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CLIENT_CSPROJ="$PROJECT_ROOT/src/Lorestead.Client/Lorestead.Client.csproj"
MCP_CSPROJ="$PROJECT_ROOT/src/Lorestead.Mcp/Lorestead.Mcp.csproj"
FRONTEND_DIR="$PROJECT_ROOT/src/Lorestead.Client/FrontEnd"
ICON_DIR="$PROJECT_ROOT/icon"
BUILD_DIR="$SCRIPT_DIR/tmp"
PUBLISH_DIR="$BUILD_DIR/publish"
MCP_PUBLISH_DIR="$BUILD_DIR/publish-mcp"
APPDIR="$BUILD_DIR/AppDir"
OUTPUT_DIR="$SCRIPT_DIR/output"

SYSTEM_LIB_DIR="/usr/lib/x86_64-linux-gnu"
WEBKIT_PKG_DIR="$SYSTEM_LIB_DIR/webkit2gtk-4.1"

WEBKIT_OLD_PATH="$SYSTEM_LIB_DIR/webkit2gtk-4.1"
WEBKIT_NEW_PATH="$WEBKIT_TMP/lib/x86_64-linux-gnu"
if [ "${#WEBKIT_OLD_PATH}" -ne "${#WEBKIT_NEW_PATH}" ]; then
    echo "ERROR: webkit patch paths differ in length (${#WEBKIT_OLD_PATH} vs ${#WEBKIT_NEW_PATH})" >&2
    exit 1
fi

echo "=== Building Lorestead AppImage ==="
echo "Project root: $PROJECT_ROOT"
echo "Output dir:   $OUTPUT_DIR"
echo "Version:      $PACK_VERSION"

mkdir -p "$OUTPUT_DIR"
rm -rf "$APPDIR" "$PUBLISH_DIR" "$MCP_PUBLISH_DIR"

# ============================================================================
echo "[1/8] Installing frontend dependencies..."
# ============================================================================
# Unconditional npm install: when the mounted repo was last used on Windows,
# node_modules lacks the linux-native optional packages (esbuild, rollup) and
# the csproj's conditional install would skip past the gap.
(cd "$FRONTEND_DIR" && npm install)

# ============================================================================
echo "[2/8] Publishing client (AOT)..."
# ============================================================================
# MinVerVersionOverride keeps the binary's stamped version identical to the
# pack version even when the working tree isn't at the release tag.
# ArtifactsPath keeps intermediate output out of src/**/obj: a mounted repo's
# obj carries the Windows host's restore state (VS fallback-folder paths that
# don't exist here), which fails ResolvePackageAssets.
dotnet publish "$CLIENT_CSPROJ" \
    -c Release -r linux-x64 \
    -p:MinVerVersionOverride="$PACK_VERSION" \
    -p:NativeDebugSymbols=false \
    -p:ArtifactsPath="$BUILD_DIR/artifacts" \
    -o "$PUBLISH_DIR"

# ============================================================================
echo "[3/8] Publishing MCP server (AOT)..."
# ============================================================================
dotnet publish "$MCP_CSPROJ" \
    -c Release -r linux-x64 \
    -p:MinVerVersionOverride="$PACK_VERSION" \
    -p:NativeDebugSymbols=false \
    -p:ArtifactsPath="$BUILD_DIR/artifacts" \
    -o "$MCP_PUBLISH_DIR"

# ============================================================================
echo "[4/8] Assembling AppDir..."
# ============================================================================
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/lib" \
         "$APPDIR/usr/libexec/webkit2gtk-4.1" \
         "$APPDIR/usr/lib64/webkit2gtk-4.1/injected-bundle"

cp "$PUBLISH_DIR/$APP_NAME" "$APPDIR/usr/bin/"
cp -r "$PUBLISH_DIR/wwwroot" "$APPDIR/usr/bin/"
cp "$PUBLISH_DIR/LICENSE.txt" "$APPDIR/usr/bin/"
cp "$PUBLISH_DIR/THIRD-PARTY-NOTICES.txt" "$APPDIR/usr/bin/"

# The MCP exe ships inside the AppImage (decisions.md); its libe_sqlite3.so is
# the same one the client bundles into usr/lib, resolved via LD_LIBRARY_PATH.
cp "$MCP_PUBLISH_DIR/$MCP_NAME" "$APPDIR/usr/bin/"

for lib in libwebview.so libnfd.so libe_sqlite3.so; do
    cp "$PUBLISH_DIR/$lib" "$APPDIR/usr/lib/"
done

# ============================================================================
echo "[5/8] Bundling webkit + dependency closure..."
# ============================================================================
# Never bundle these -- they must come from the host (glibc, graphics/display
# stack, audio, and other host-coupled libs). Bundling them segfaults on
# distros whose driver stacks don't match jammy's.
declare -A EXCLUDE=()
while IFS= read -r name; do EXCLUDE["$name"]=1; done <<'EOF'
ld-linux.so.2
ld-linux-x86-64.so.2
libanl.so.1
libBrokenLocale.so.1
libc.so.6
libcidn.so.1
libdl.so.2
libm.so.6
libmvec.so.1
libnss_compat.so.2
libnss_dns.so.2
libnss_files.so.2
libnss_hesiod.so.2
libnss_nisplus.so.2
libnss_nis.so.2
libpthread.so.0
libresolv.so.2
librt.so.1
libthread_db.so.1
libutil.so.1
libgcc_s.so.1
libstdc++.so.6
libdrm.so.2
libEGL.so.1
libgbm.so.1
libGL.so.1
libGLdispatch.so.0
libglapi.so.0
libGLX.so.0
libOpenGL.so.0
libX11.so.6
libX11-xcb.so.1
libxcb.so.1
libxcb-dri2.so.0
libxcb-dri3.so.0
libxcb-glx.so.0
libxcb-present.so.0
libxcb-randr.so.0
libxcb-render.so.0
libxcb-shape.so.0
libxcb-shm.so.0
libxcb-sync.so.1
libxcb-xfixes.so.0
libwayland-client.so.0
libwayland-cursor.so.0
libwayland-egl.so.1
libwayland-server.so.0
libXau.so.6
libXcursor.so.1
libXdamage.so.1
libXdmcp.so.6
libXext.so.6
libXfixes.so.3
libXi.so.6
libXinerama.so.1
libXrandr.so.2
libXrender.so.1
libXxf86vm.so.1
libXcomposite.so.1
libfontconfig.so.1
libfreetype.so.6
libfribidi.so.0
libharfbuzz.so.0
libasound.so.2
libjack.so.0
libpipewire-0.3.so.0
libpulse.so.0
libpulse-simple.so.0
libblkid.so.1
libcap.so.2
libcom_err.so.2
libcrypto.so.3
libdbus-1.so.3
libexpat.so.1
libffi.so.8
libgmp.so.10
libgpg-error.so.0
libICE.so.6
libkeyutils.so.1
libmount.so.1
libpcre2-8.so.0
libSM.so.6
libsystemd.so.0
libusb-1.0.so.0
libuuid.so.1
libz.so.1
libxkbcommon.so.0
libpixman-1.so.0
EOF

copy_deps() {
    local binary="$1" lib libname
    while IFS= read -r lib; do
        [ -f "$lib" ] || continue
        libname="$(basename "$lib")"
        [ -f "$libname" ] && continue
        [ -n "${EXCLUDE[$libname]:-}" ] && continue
        cp "$lib" .
    done < <(ldd "$binary" 2>/dev/null | grep "=> /" | awk '{print $3}')
}

cd "$APPDIR/usr/lib"

copy_deps libnfd.so
copy_deps libwebview.so

cp "$SYSTEM_LIB_DIR/libwebkit2gtk-4.1.so.0" .
ln -sf libwebkit2gtk-4.1.so.0 libwebkit2gtk-4.1.so
cp "$WEBKIT_PKG_DIR"/{WebKitNetworkProcess,WebKitWebProcess,WebKitGPUProcess} \
    "$APPDIR/usr/libexec/webkit2gtk-4.1/"
cp "$WEBKIT_PKG_DIR/injected-bundle/libwebkit2gtkinjectedbundle.so" \
    "$APPDIR/usr/lib64/webkit2gtk-4.1/injected-bundle/"
copy_deps libwebkit2gtk-4.1.so.0

# Transitive closure: keep passing bundled libs through copy_deps until the
# file count stops growing.
iteration=1
while true; do
    before=$(ls -1 | wc -l)
    for lib in *.so*; do
        if [ -f "$lib" ] && [ ! -L "$lib" ]; then
            copy_deps "$lib"
        fi
    done
    after=$(ls -1 | wc -l)
    if [ "$after" -eq "$before" ]; then
        break
    fi
    iteration=$((iteration + 1))
    if [ "$iteration" -gt 10 ]; then
        echo "WARNING: dependency closure did not converge after 10 iterations" >&2
        break
    fi
done
echo "  bundled $(ls -1 *.so* | wc -l) libraries in $iteration pass(es)"

# Unversioned symlinks for .NET P/Invoke: DllImport("libgtk-3") searches for
# the bare .so name first and would otherwise find the host copy over the
# bundled one.
ln -sf libgtk-3.so.0 libgtk-3.so
ln -sf libgdk-3.so.0 libgdk-3.so
ln -sf libgobject-2.0.so.0 libgobject-2.0.so
ln -sf libglib-2.0.so.0 libglib-2.0.so

cd "$BUILD_DIR"

# ============================================================================
echo "[6/8] Patching webkit hardcoded helper paths..."
# ============================================================================
# WebKit hardcodes absolute /usr paths to its helper processes at compile time.
# Replace them with same-length /tmp paths that AppRun symlinks back into the
# AppDir. sed is byte-safe here: LANG=C, same-length replacement, no NUL in
# either string. The longer injected-bundle form must be substituted first.
patch_webkit_paths() {
    local f="$1"
    LANG=C sed -i \
        -e "s|$WEBKIT_OLD_PATH/injected-bundle/|$WEBKIT_NEW_PATH/injected-bundle/|g" \
        -e "s|$WEBKIT_OLD_PATH|$WEBKIT_NEW_PATH|g" \
        "$f"
    if LANG=C grep -q "$WEBKIT_OLD_PATH" "$f"; then
        echo "ERROR: unpatched webkit path remains in $f" >&2
        exit 1
    fi
    echo "  patched $(basename "$f")"
}
patch_webkit_paths "$APPDIR/usr/lib/libwebkit2gtk-4.1.so.0"
patch_webkit_paths "$APPDIR/usr/lib/libjavascriptcoregtk-4.1.so.0"

# ============================================================================
echo "[7/8] Creating AppRun, desktop entry, and icons..."
# ============================================================================
cat > "$APPDIR/AppRun" <<EOF
#!/bin/sh
SELF=\$(readlink -f "\$0")
HERE=\${SELF%/*}
export APPDIR="\${APPDIR:-\${HERE}}"

# Bundled webkit/gtk plus the .NET native libs all resolve from here.
export LD_LIBRARY_PATH="\${APPDIR}/usr/lib:\${LD_LIBRARY_PATH:-}"

# MCP dispatch: \`<AppImage> --mcp <args>\` execs the bundled MCP server with
# stdio inherited, so the AppImage file itself is the stable path an agent
# config points at (decisions.md 2026-07-29).
if [ "\${1:-}" = "--mcp" ]; then
    shift
    exec "\${APPDIR}/usr/bin/$MCP_NAME" "\$@"
fi

# The webkit binaries were patched to load helpers from $WEBKIT_TMP -- recreate
# the symlink farm into this (possibly moved) AppDir on every launch.
mkdir -p "$WEBKIT_TMP/lib/x86_64-linux-gnu/injected-bundle"
for helper in WebKitNetworkProcess WebKitWebProcess WebKitGPUProcess; do
    ln -sf "\${APPDIR}/usr/libexec/webkit2gtk-4.1/\${helper}" \
        "$WEBKIT_TMP/lib/x86_64-linux-gnu/" 2>/dev/null || true
done
ln -sf "\${APPDIR}/usr/lib64/webkit2gtk-4.1/injected-bundle/libwebkit2gtkinjectedbundle.so" \
    "$WEBKIT_TMP/lib/x86_64-linux-gnu/injected-bundle/" 2>/dev/null || true

exec "\${APPDIR}/usr/bin/$APP_NAME" "\$@"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/$APP_ID.desktop" <<EOF
[Desktop Entry]
Name=$APP_NAME
GenericName=Notes and Tasks
Comment=Self-hostable notes and tasks, built for AI agents
Exec=$APP_NAME
Icon=$APP_ID
Terminal=false
Type=Application
Categories=Office;Utility;
Keywords=notes;tasks;markdown;sync;
StartupNotify=true
EOF

cp "$ICON_DIR/icon-256.png" "$APPDIR/.DirIcon"
cp "$ICON_DIR/icon-256.png" "$APPDIR/$APP_ID.png"
for size in 16 24 32 48 64 128 256 512; do
    hicolor_dir="$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps"
    mkdir -p "$hicolor_dir"
    cp "$ICON_DIR/icon-$size.png" "$hicolor_dir/$APP_ID.png"
done

# ============================================================================
echo "[8/8] Packaging with Velopack..."
# ============================================================================
# vpk treats a packDir whose name ends in .AppDir as a pre-staged AppDir and
# uses it as-is, custom AppRun included.
VELOPACK_APPDIR="$BUILD_DIR/$APP_NAME.AppDir"
rm -rf "$VELOPACK_APPDIR"
mv "$APPDIR" "$VELOPACK_APPDIR"
APPDIR="$VELOPACK_APPDIR"

# vpk targets an older runtime than the repo's .NET 10; roll-forward bridges that.
DOTNET_ROLL_FORWARD=Major vpk pack \
    --packId "$APP_NAME" \
    --packVersion "$PACK_VERSION" \
    --packDir "$APPDIR" \
    --mainExe "$APP_NAME" \
    --packTitle "$APP_NAME" \
    --channel linux-x64 \
    --outputDir "$OUTPUT_DIR"

rm -rf "$APPDIR" "$PUBLISH_DIR" "$MCP_PUBLISH_DIR"

echo
echo "=== Build complete ==="
ls -lh "$OUTPUT_DIR"
