# syntax=docker/dockerfile:1.7

# =============================================
# Build stage
# =============================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    NUGET_XMLDOC_MODE=skip

# 1. Copy only project files for restore layer cache.
#
# IMPORTANT: we deliberately do NOT use `--mount=type=cache` for the NuGet
# folder here. Reason: BuildKit cache mounts are NOT preserved reliably across
# separate `RUN` steps in GitHub Actions cache (`type=gha`). Layer caching is.
# By restoring without a cache mount, packages land inside this layer, the
# layer is uploaded to the GHA layer cache, and the next `RUN` (publish)
# can see them.
COPY WishHub.sln ./
COPY WishHub.Core/*.csproj            WishHub.Core/
COPY WishHub.Infrastructure/*.csproj  WishHub.Infrastructure/
COPY WishHub.Parsing/*.csproj         WishHub.Parsing/
COPY WishHub.Api/*.csproj             WishHub.Api/

RUN dotnet restore WishHub.Api/WishHub.Api.csproj

# 2. Copy the rest of the source
COPY WishHub.Core/            WishHub.Core/
COPY WishHub.Infrastructure/  WishHub.Infrastructure/
COPY WishHub.Parsing/         WishHub.Parsing/
COPY WishHub.Api/             WishHub.Api/

# 3. Publish — `--no-restore` is safe here because restore packages live in
#    the previous layer (no cache mount → packages persisted in the layer).
RUN dotnet publish WishHub.Api/WishHub.Api.csproj \
        -c Release \
        -o /app \
        --no-restore \
        /p:UseAppHost=false

# 4. Download Playwright Chromium browser only (no apt deps here).
#    `playwright install chromium` ships TWO browsers by default:
#      - chromium          (~542 MB, modern headless, what our code actually uses)
#      - chromium_headless_shell (~304 MB, legacy headless, never invoked by us)
#    plus ffmpeg (~3 MB, only needed for video recording).
#    We strip the unused ones to save ~300 MB in the final image.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN pwsh /app/playwright.ps1 install chromium && \
    rm -rf /ms-playwright/chromium_headless_shell-* \
           /ms-playwright/ffmpeg-*

# =============================================
# Runtime stage
# =============================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:5000

# Install Chromium system dependencies for Debian 12 (bookworm).
# List taken from Playwright nativeDeps for chromium. We do NOT install pwsh
# in the runtime stage — that's where most of the bloat used to come from.
RUN apt-get update && apt-get install -y --no-install-recommends \
        # Chromium runtime deps
        libasound2 \
        libatk-bridge2.0-0 \
        libatk1.0-0 \
        libatspi2.0-0 \
        libcairo2 \
        libcups2 \
        libdbus-1-3 \
        libdrm2 \
        libgbm1 \
        libglib2.0-0 \
        libnspr4 \
        libnss3 \
        libpango-1.0-0 \
        libx11-6 \
        libxcb1 \
        libxcomposite1 \
        libxdamage1 \
        libxext6 \
        libxfixes3 \
        libxkbcommon0 \
        libxrandr2 \
        libwayland-client0 \
        fonts-liberation \
        fonts-unifont \
        # wget — for container HEALTHCHECK
        wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
COPY --from=build /ms-playwright /ms-playwright

EXPOSE 5000
ENTRYPOINT ["dotnet", "WishHub.Api.dll"]
