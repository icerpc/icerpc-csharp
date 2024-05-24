FROM debian:bookworm

# Install necessary packages
RUN set -eux \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -o packages-microsoft-prod.deb https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && rm packages-microsoft-prod.deb \
    && apt-get install -y libmsquic git libicu72 build-essential \
    && curl https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh -L \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh -v latest -c 8.0 \
    && ./dotnet-install.sh -v latest -q preview -c 9.0 \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean \
    && curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y \
    && echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc \
    && echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools:$HOME/.cargo/bin' >> ~/.bashrc
