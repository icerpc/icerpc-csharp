FROM mcr.microsoft.com/vscode/devcontainers/dotnet:1-7.0-jammy

# Install necessary packages
RUN set -eux \
    && wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && sudo dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && sudo apt update \
    && sudo apt-get install -y libmsquic \
    && wget -q https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh \
    && bash dotnet-install.sh --channel 8.0.1xx --quality preview --install-dir ~/.dotnet8.0 \
    && rm -rf /var/lib/apt/lists/* \
    && sudo apt-get clean

# Set environment variables
ENV DOTNET_ROOT=$HOME/.dotnet8.0 \
    PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
