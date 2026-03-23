# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY src/StingBIM.Shared/StingBIM.Shared.csproj src/StingBIM.Shared/
COPY src/StingBIM.Core/StingBIM.Core.csproj src/StingBIM.Core/
COPY src/StingBIM.Infrastructure/StingBIM.Infrastructure.csproj src/StingBIM.Infrastructure/
COPY src/StingBIM.MIM/StingBIM.MIM.csproj src/StingBIM.MIM/
COPY src/StingBIM.API/StingBIM.API.csproj src/StingBIM.API/
RUN dotnet restore src/StingBIM.API/StingBIM.API.csproj

# Copy everything and build
COPY . .
RUN dotnet publish src/StingBIM.API/StingBIM.API.csproj -c Release -o /app --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "StingBIM.API.dll"]
