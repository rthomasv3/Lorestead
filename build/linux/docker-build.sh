#!/usr/bin/env bash
# Local AppImage build inside the jammy container (same env as the GitHub runner).
#
# Usage: ./docker-build.sh [--version <semver>]   (defaults to 0.0.1-local.1)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 0.0.1, not 0.0.0: Velopack rejects pack versions below 0.0.1.
VERSION="0.0.1-local.1"
while [ $# -gt 0 ]; do
    case "$1" in
        --version)
            VERSION="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Usage: $0 [--version <semver>]" >&2
            exit 1
            ;;
    esac
done

if [ -n "${SUDO_UID:-}" ]; then
    USER_ID=$SUDO_UID
    GROUP_ID=$SUDO_GID
else
    USER_ID=$(id -u)
    GROUP_ID=$(id -g)
fi

echo "[docker] building builder image (cached after first run)..."
docker build \
    --build-arg USER_ID="$USER_ID" \
    --build-arg GROUP_ID="$GROUP_ID" \
    -t lorestead-appimage-builder \
    "$SCRIPT_DIR"

echo "[docker] running build..."
# Git Bash rewrites POSIX-looking args into Windows paths (the -w path becomes
# C:/Program Files/Git/...); turn that off and hand docker a Windows-style
# mount source it accepts as-is.
MOUNT_SRC="$PROJECT_ROOT"
case "$(uname -s)" in
    MINGW*|MSYS*)
        MOUNT_SRC="$(cd "$PROJECT_ROOT" && pwd -W)"
        export MSYS_NO_PATHCONV=1
        ;;
esac

# label=disable instead of :z -- avoids SELinux-relabeling the whole repo on Fedora.
# The named volume keeps the NuGet package cache across runs (--rm discards the
# container's home directory).
docker run --rm \
    --security-opt label=disable \
    -v "$MOUNT_SRC:/build" \
    -v lorestead-nuget:/home/builder/.nuget \
    -w /build/build/linux \
    lorestead-appimage-builder \
    bash build-velopack.sh --version "$VERSION"

echo "[docker] done:"
ls -lh "$SCRIPT_DIR/output/"
