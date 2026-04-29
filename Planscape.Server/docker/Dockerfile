# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY src/Planscape.Shared/Planscape.Shared.csproj src/Planscape.Shared/
COPY src/Planscape.Core/Planscape.Core.csproj src/Planscape.Core/
COPY src/Planscape.Infrastructure/Planscape.Infrastructure.csproj src/Planscape.Infrastructure/
COPY src/Planscape.MIM/Planscape.MIM.csproj src/Planscape.MIM/
COPY src/Planscape.PluginSync/Planscape.PluginSync.csproj src/Planscape.PluginSync/
COPY src/Planscape.API/Planscape.API.csproj src/Planscape.API/
COPY Planscape.sln .
RUN dotnet restore src/Planscape.API/Planscape.API.csproj

# Copy everything and build
COPY . .
RUN dotnet publish src/Planscape.API/Planscape.API.csproj -c Release -o /app --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# P7 — IfcConvert (IfcOpenShell) for IFC→glTF derivatives. Opt-in: only used
# when ModelConverter:Provider=ifcconvert. Pinned to a known-good release.
ARG IFCCONVERT_VERSION=0.7.0
RUN if [ "$IFCCONVERT_VERSION" != "skip" ]; then \
        apt-get update && apt-get install -y --no-install-recommends \
            ca-certificates wget unzip libxi6 libxrender1 libglu1-mesa && \
        wget -q "https://s3.amazonaws.com/ifcopenshell-builds/IfcConvert-v${IFCCONVERT_VERSION}-linux64.zip" -O /tmp/ifcconvert.zip && \
        unzip /tmp/ifcconvert.zip -d /usr/local/bin && \
        chmod +x /usr/local/bin/IfcConvert && \
        rm /tmp/ifcconvert.zip && \
        rm -rf /var/lib/apt/lists/* ; \
    fi

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Planscape.API.dll"]
