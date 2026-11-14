# Multi-stage build for SmartScout (.NET 10 Web API + Static Scouting Console)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore using project manifests first for better layer caching
COPY SmartScout.slnx ./
COPY SmartScout.Domain/SmartScout.Domain.csproj SmartScout.Domain/
COPY SmartScout.Application/SmartScout.Application.csproj SmartScout.Application/
COPY SmartScout.Infrastructure/SmartScout.Infrastructure.csproj SmartScout.Infrastructure/
COPY SmartScout.Api/SmartScout.Api.csproj SmartScout.Api/
RUN dotnet restore SmartScout.Api/SmartScout.Api.csproj

# Copy source and publish Release build
COPY SmartScout.Domain/ SmartScout.Domain/
COPY SmartScout.Application/ SmartScout.Application/
COPY SmartScout.Infrastructure/ SmartScout.Infrastructure/
COPY SmartScout.Api/ SmartScout.Api/
RUN dotnet publish SmartScout.Api/SmartScout.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for container healthcheck and font/imaging libs needed by QuestPDF
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    ScraperSettings__Headless=true

EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SmartScout.Api.dll"]
