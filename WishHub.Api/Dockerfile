# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
ENV DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_XMLDOC_MODE=skip
# Keep restored packages in the layer: this also works with remote BuildKit caches.
COPY WishHub.sln ./
COPY WishHub.Core/*.csproj WishHub.Core/
COPY WishHub.Infrastructure/*.csproj WishHub.Infrastructure/
COPY WishHub.Parsing/*.csproj WishHub.Parsing/
COPY WishHub.Api/*.csproj WishHub.Api/
RUN dotnet restore WishHub.Api/WishHub.Api.csproj
COPY WishHub.Core/ WishHub.Core/
COPY WishHub.Infrastructure/ WishHub.Infrastructure/
COPY WishHub.Parsing/ WishHub.Parsing/
COPY WishHub.Api/ WishHub.Api/
RUN dotnet publish WishHub.Api/WishHub.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV DOTNET_RUNNING_IN_CONTAINER=true ASPNETCORE_URLS=http://+:5000
# Used by the existing Compose HTTP health check.
RUN apt-get update && apt-get install -y --no-install-recommends wget && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 5000
ENTRYPOINT ["dotnet", "WishHub.Api.dll"]
