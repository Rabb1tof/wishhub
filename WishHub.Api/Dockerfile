FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# SDK-образ уже содержит pwsh — нужен только для playwright.ps1 install --with-deps

COPY . .

RUN dotnet restore WishHub.Api/WishHub.Api.csproj
RUN dotnet build WishHub.Api/WishHub.Api.csproj -c Release --no-restore

# Официальный способ установки: playwright.ps1 install --with-deps.
# Флаг --with-deps ставит apt-пакеты для Chromium, browsers уходят в /ms-playwright.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN pwsh WishHub.Api/bin/Release/net9.0/playwright.ps1 install --with-deps chromium

RUN dotnet publish WishHub.Api/WishHub.Api.csproj -c Release -o /app --no-restore --no-build

# =============================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Install PowerShell (tarball) для playwright.ps1 install-deps в runtime
ARG PWSH_VERSION=7.4.6
RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates libicu72 && \
    ARCH=$(dpkg --print-architecture) && \
    if [ "$ARCH" = "arm64" ]; then PWSH_ARCH="linux-arm64"; else PWSH_ARCH="linux-x64"; fi && \
    wget -q "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell-${PWSH_VERSION}-${PWSH_ARCH}.tar.gz" -O /tmp/pwsh.tar.gz && \
    mkdir -p /opt/microsoft/powershell/7 && \
    tar zxf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/7 && \
    chmod +x /opt/microsoft/powershell/7/pwsh && \
    ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh && \
    rm /tmp/pwsh.tar.gz && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
COPY --from=build /ms-playwright /ms-playwright

# Ставим только системные deps для Chromium (браузеры уже скопированы)
RUN pwsh playwright.ps1 install-deps chromium

EXPOSE 5000
ENTRYPOINT ["dotnet", "WishHub.Api.dll"]