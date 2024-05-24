FROM debian:bookworm

# Insall curl for downloading required files
RUN set -eux \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl

# Install prerequisites
RUN set -eux \
    && curl -o packages-microsoft-prod.deb https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y libmsquic git libicu72 build-essential

# Install .NET 8.0 and 9.0
RUN set -eux \
    && curl https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh -L \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh -v latest -c 8.0 \
    && ./dotnet-install.sh -v latest -q preview -c 9.0

# Install Rust
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y

# Set environment variables
ENV DOTNET_ROOT=~/.dotnet
ENV PATH=$DOTNET_ROOT:$DOTNET_ROOT/tools:~/.cargo/bin:$PATH
