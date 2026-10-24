# OSS Scanner build of IceRPC for C# (https://github.com/icerpc/icerpc-csharp).
#
# The scanner builds this file with the IceRPC checkout as the build context. The network is available only while
# this file builds; the scan itself runs offline, so the .NET SDK, every NuGet package, and the slicec and protoc
# compilers are fetched here.
#
# /src is the checkout, with the IceRpc.slnx solution (sources and tests) built in the Debug configuration: no
# optimization, and Debug.Assert enabled. The tests turn a failed Debug.Assert into a test failure. Run a test
# project with, for example, `dotnet test tests/IceRpc.Tests --filter "FullyQualifiedName~IceRpcProtocolConnection"`.
#
# The examples, the project templates' generated projects, and the docfx examples are not built: they restore IceRPC
# packages from NuGet feeds that are not reachable during the scan.

# The OS of the Linux CI runners (ubuntu-24.04).
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1

# The runtime dependencies of .NET, and git to produce patches.
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl git libicu74 libssl3t64 zlib1g tzdata \
    && rm -rf /var/lib/apt/lists/*

# libmsquic for the QUIC transport and its tests, pinned to the same version as CI
# (.github/actions/setup-dotnet): 2.6.1 fails every QUIC handshake to an IP address.
RUN set -eux; \
    curl -fsSL -o /tmp/packages-microsoft-prod.deb \
      https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb; \
    dpkg -i /tmp/packages-microsoft-prod.deb; \
    rm /tmp/packages-microsoft-prod.deb; \
    apt-get update; \
    apt-get install -y --no-install-recommends libmsquic=2.6.0; \
    rm -rf /var/lib/apt/lists/*

# The .NET 11 SDK. Ubuntu does not package .NET previews. CI installs the latest 11.0 preview
# (.github/actions/setup-dotnet); the version is pinned here, so that a new preview does not break the scanner's
# periodic rebuilds without anyone noticing. Update it to the SDK that CI builds with.
ARG DOTNET_SDK_VERSION=11.0.100-rc.1.26425.128
RUN set -eux; \
    curl -fsSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh; \
    bash /tmp/dotnet-install.sh --version "$DOTNET_SDK_VERSION" --install-dir "$DOTNET_ROOT"; \
    rm /tmp/dotnet-install.sh; \
    ln -s "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet; \
    dotnet --info

# scanner contract: the checkout lives inside the image, at /src
COPY . /src
WORKDIR /src

# Restores every NuGet package into ~/.nuget/packages and downloads slicec and protoc into the obj directories of
# src/IceRpc.Slice.Tools and src/IceRpc.Protobuf.Tools, so later builds, including incremental builds of modified
# tests, work offline.
RUN dotnet build IceRpc.slnx

# Runs the tests to show the image works. Failures are reported but do not fail the build.
RUN dotnet test IceRpc.slnx --no-build --blame-hang-timeout 60s \
    || echo "WARNING: dotnet test reported failures"
