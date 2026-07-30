#!/usr/bin/env bash
# Builds the Lorestead macOS .app/.pkg via Velopack (vpk pack). vpk generates
# the .app bundle from the flat publish dir (everything lands in
# Contents/MacOS, which is where Galdr's FolderContent expects wwwroot), then
# signs, notarizes, and packages - replacing Vellerune's manual
# codesign/productbuild flow. The MCP exe ships inside the bundle
# (decisions.md 2026-07-29): agent configs point at
# /Applications/Lorestead.app/Contents/MacOS/Lorestead.Mcp.
#
# Usage: ./build-velopack.sh --version <semver> \
#            [--sign-app-identity <subject>] [--sign-install-identity <subject>] \
#            [--notary-profile <name>] [--keychain <path>]
#
# Without signing options this produces unsigned artifacts for local iteration.
set -euo pipefail

PACK_VERSION=""
SIGN_APP_IDENTITY=""
SIGN_INSTALL_IDENTITY=""
NOTARY_PROFILE=""
KEYCHAIN_PATH=""
while [ $# -gt 0 ]; do
    case "$1" in
        --version)               PACK_VERSION="$2";           shift 2 ;;
        --sign-app-identity)     SIGN_APP_IDENTITY="$2";      shift 2 ;;
        --sign-install-identity) SIGN_INSTALL_IDENTITY="$2";  shift 2 ;;
        --notary-profile)        NOTARY_PROFILE="$2";         shift 2 ;;
        --keychain)              KEYCHAIN_PATH="$2";          shift 2 ;;
        *)
            echo "Unknown option: $1" >&2
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
BUNDLE_ID="io.github.rthomasv3.Lorestead"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CLIENT_CSPROJ="$PROJECT_ROOT/src/Lorestead.Client/Lorestead.Client.csproj"
MCP_CSPROJ="$PROJECT_ROOT/src/Lorestead.Mcp/Lorestead.Mcp.csproj"
ICON_PATH="$PROJECT_ROOT/icon/icon.icns"
BUILD_DIR="$SCRIPT_DIR/tmp"
PUBLISH_DIR="$BUILD_DIR/publish"
MCP_PUBLISH_DIR="$BUILD_DIR/publish-mcp"
OUTPUT_DIR="$SCRIPT_DIR/output"

# Homebrew's dotnet ships a misconfigured Native AOT runtime pack that links
# against /opt/homebrew/opt paths which don't exist on end-user machines
# (learned on Vellerune). Use the official SDK: CI's setup-dotnet is fine;
# locally set DOTNET=$HOME/.dotnet/dotnet if brew's dotnet shadows it.
DOTNET="${DOTNET:-dotnet}"

echo "=== Building Lorestead macOS package ==="
echo "Project root: $PROJECT_ROOT"
echo "Output dir:   $OUTPUT_DIR"
echo "Version:      $PACK_VERSION"
echo "Signing:      ${SIGN_APP_IDENTITY:-none (unsigned build)}"

mkdir -p "$OUTPUT_DIR"
rm -rf "$PUBLISH_DIR" "$MCP_PUBLISH_DIR"

# ============================================================================
echo "[1/3] Publishing client (AOT)..."
# ============================================================================
# MinVerVersionOverride keeps the binary's stamped version identical to the
# pack version even when the working tree isn't at the release tag.
"$DOTNET" publish "$CLIENT_CSPROJ" \
    -c Release -r osx-arm64 \
    -p:MinVerVersionOverride="$PACK_VERSION" \
    -p:NativeDebugSymbols=false \
    -o "$PUBLISH_DIR"

# ============================================================================
echo "[2/3] Publishing MCP server (AOT) and staging beside the client..."
# ============================================================================
"$DOTNET" publish "$MCP_CSPROJ" \
    -c Release -r osx-arm64 \
    -p:MinVerVersionOverride="$PACK_VERSION" \
    -p:NativeDebugSymbols=false \
    -o "$MCP_PUBLISH_DIR"

cp "$MCP_PUBLISH_DIR/$MCP_NAME" "$PUBLISH_DIR/"

# dSYM debug bundles are directories; vpk's default exclude only catches .pdb.
rm -rf "$PUBLISH_DIR"/*.dSYM "$PUBLISH_DIR"/*.dbg

# ============================================================================
echo "[3/3] Packaging with Velopack..."
# ============================================================================
# vpk targets an older runtime than the repo's .NET 10; roll-forward bridges that.
export DOTNET_ROLL_FORWARD=Major

PACK_ARGS=(
    --packId "$APP_NAME"
    --packVersion "$PACK_VERSION"
    --packDir "$PUBLISH_DIR"
    --mainExe "$APP_NAME"
    --icon "$ICON_PATH"
    --bundleId "$BUNDLE_ID"
    --channel osx-arm64
    --outputDir "$OUTPUT_DIR"
)

if [ -n "$SIGN_APP_IDENTITY" ]; then
    PACK_ARGS+=(--signAppIdentity "$SIGN_APP_IDENTITY")
fi
if [ -n "$SIGN_INSTALL_IDENTITY" ]; then
    PACK_ARGS+=(--signInstallIdentity "$SIGN_INSTALL_IDENTITY")
fi
if [ -n "$NOTARY_PROFILE" ]; then
    PACK_ARGS+=(--notaryProfile "$NOTARY_PROFILE")
fi
if [ -n "$KEYCHAIN_PATH" ]; then
    PACK_ARGS+=(--keychain "$KEYCHAIN_PATH")
fi

vpk pack "${PACK_ARGS[@]}"

rm -rf "$PUBLISH_DIR" "$MCP_PUBLISH_DIR"

echo
echo "=== Build complete ==="
ls -lh "$OUTPUT_DIR"
