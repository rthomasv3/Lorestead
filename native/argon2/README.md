# Argon2 native library

Prebuilt copies of the reference Argon2 implementation
([P-H-C/phc-winner-argon2](https://github.com/P-H-C/phc-winner-argon2)), one folder
per .NET runtime identifier, every file named `libargon2` so a single
`DllImport("libargon2")` resolves on all platforms. Lorestead's vault derives its
password key with Argon2id through `Lorestead.Core.Vault.Argon2idKdf`.

`manifest.json` records the upstream commit, the compilers, the flags, and the
workflow run that produced these files; `SHA256SUMS` covers every library.

## Rebuilding

Run the **Native Argon2** workflow (`.github/workflows/native-argon2.yml`) by hand.
It builds every target, runs upstream's known-answer tests, loads each desktop
library from .NET on its own OS and checks two known answers, and uploads one
artifact laid out like this folder. Download it, replace this folder's contents,
check `SHA256SUMS`, and commit. The library has been frozen upstream since 2019, so
this only happens when the flags change.

| Folder | How it is built | Code path |
|---|---|---|
| linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64 | Zig's C compiler on Ubuntu | x64: SSSE3 (`opt.c`), arm64: portable (`ref.c`) |
| android-arm64, android-arm, android-x64, android-x86 | Android NDK on Ubuntu, API 24, 16 KiB page alignment | x86/x64: SSSE3, arm: portable |
| ios-arm64, iossimulator-arm64, iossimulator-x64 | Apple clang on macOS, iOS 14.0, static libraries | arm64: portable, x64: SSSE3 |

Threads are enabled everywhere: a derivation with parallelism p runs p threads.

## Wiring

`Argon2.targets` beside this file places the right library for the project that
imports it: beside the executable on desktop (the runtime identifier's copy for a
publish, the SDK host's for a plain build), as per-ABI native libraries on Android,
and as a static native reference on iOS. Import it from every project that calls
`Argon2idKdf`; Lorestead.Core deliberately does not, so the MCP server never ships
the library. The Linux AppImage script copies `libargon2.so` into `usr/lib` by name,
like the other native libraries.

## License

Argon2 is dual-licensed under CC0 1.0 and Apache License 2.0; Lorestead takes it
under Apache 2.0. See THIRD-PARTY-NOTICES.txt at the repository root.
