# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Native AOT cross-compiles nothing - the linux binary is produced by linux clang.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY Directory.Packages.props ./
# src-level build props carry MinVer; without .git in the context it stamps
# 0.0.0-alpha.0 - the release workflow overrides via MinVerVersionOverride.
COPY src/Directory.Build.props src/
# The server csproj copies these into /app beside the binary.
COPY LICENSE.txt THIRD-PARTY-NOTICES.txt ./
# Core embeds the first-run seed icon from the repo-root icon directory.
COPY icon/ icon/
COPY src/Lorestead.Core/ src/Lorestead.Core/
COPY src/Lorestead.Server/ src/Lorestead.Server/
RUN dotnet publish src/Lorestead.Server -c Release -r linux-x64 -o /app \
    && mkdir /data-seed

# Self-contained AOT binary: chiseled runtime-deps carries only the native libs it
# links against (glibc, OpenSSL, zlib) - no shell, no package manager, no ICU
# (InvariantGlobalization). Runs as the non-root app user (uid 1654).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app ./
# Pre-created with app ownership so an anonymous /data volume is writable; there
# is no shell in this image to fix it up at runtime.
COPY --from=build --chown=1654:1654 /data-seed /data
# Port 8080 comes from the base image's HTTP_PORTS default.
ENV LORESTEAD_DATA_DIR=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["/app/Lorestead.Server"]
