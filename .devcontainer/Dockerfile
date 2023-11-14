FROM mcr.microsoft.com/vscode/devcontainers/dotnet:1-7.0-jammy

USER vscode
WORKDIR /home/vscode

# Install necessary packages
RUN set -eux \
    && wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && sudo dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && sudo apt update \
    && sudo apt-get install -y libmsquic \
    && wget -q https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh \
    && bash dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet8.0 \
    && sudo rm -rf /var/lib/apt/lists/* \
    && sudo apt-get clean \
    && curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y

# Set environment variables
ENV DOTNET_ROOT=/home/vscode/.dotnet8.0 \
    PATH=/home/vscode/.dotnet8.0:/home/vscode/.dotnet8.0/tools:/home/vscode/.cargo/bin:$PATH
