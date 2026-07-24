# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Native AOT cross-compiles nothing - the linux binary is produced by linux clang.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY Directory.Packages.props ./
COPY src/SylvaNote.Core/ src/SylvaNote.Core/
COPY src/SylvaNote.Server/ src/SylvaNote.Server/
RUN dotnet publish src/SylvaNote.Server -c Release -r linux-x64 -o /app \
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
ENV SYLVANOTE_DATA_DIR=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["/app/SylvaNote.Server"]
