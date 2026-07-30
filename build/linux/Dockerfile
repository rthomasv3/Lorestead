# Local AppImage build environment mirroring the GitHub ubuntu-22.04 runner.
# Jammy is deliberate: its webkit2gtk-4.1 binaries set the AppImage's glibc floor.
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=UTC

RUN apt-get update && apt-get install -y \
    curl \
    ca-certificates \
    git \
    file \
    xz-utils \
    clang \
    zlib1g-dev \
    binutils \
    squashfs-tools \
    libwebkit2gtk-4.1-dev \
    libgtk-3-dev \
    && rm -rf /var/lib/apt/lists/*

RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /build

# Build as the host user so bin/obj and outputs in the mounted repo stay editable
ARG USER_ID=1000
ARG GROUP_ID=1000
RUN groupadd -g ${GROUP_ID} builder || true \
    && useradd -m -u ${USER_ID} -g ${GROUP_ID} builder || true
USER builder

ENV DOTNET_ROOT="/usr/share/dotnet"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV PATH="/home/builder/.dotnet/tools:${PATH}"

# Keep the vpk CLI on the same version as the Velopack library reference.
RUN dotnet tool install -g vpk --version 1.2.0

CMD ["/bin/bash"]
