FROM mcr.microsoft.com/vscode/devcontainers/dotnet:1-8.0-jammy

USER vscode
WORKDIR /home/vscode

# Install necessary packages
RUN set -eux \
    && wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && sudo dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && sudo apt update \
    && sudo apt-get install -y libmsquic \
    && sudo rm -rf /var/lib/apt/lists/* \
    && sudo apt-get clean \
    && curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y
