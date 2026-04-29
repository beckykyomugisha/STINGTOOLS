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

# P7 — IfcConvert (IfcOpenShell) for IFC→glTF derivatives. OPT-IN: pass
# `--build-arg IFCCONVERT_URL=<full-url>` to install at build time. Default
# is "skip" because the IfcOpenShell build bucket filenames include a
# build-hash suffix that needs to be looked up per release (e.g.
# `IfcConvert-v0.8.0-1b1fd1e-linux64.zip`). When the URL is provided the
# binary is unzipped to a temp dir, the IfcConvert executable is moved to
# /usr/local/bin/IfcConvert, and the temp dir cleaned up.
ARG IFCCONVERT_URL=skip
RUN if [ "$IFCCONVERT_URL" != "skip" ]; then \
        apt-get update && apt-get install -y --no-install-recommends \
            ca-certificates wget unzip libxi6 libxrender1 libglu1-mesa && \
        mkdir -p /tmp/ifcconvert && \
        wget -q "$IFCCONVERT_URL" -O /tmp/ifcconvert.zip && \
        unzip -o /tmp/ifcconvert.zip -d /tmp/ifcconvert && \
        find /tmp/ifcconvert -type f -name 'IfcConvert' -exec mv {} /usr/local/bin/IfcConvert \; && \
        chmod +x /usr/local/bin/IfcConvert && \
        rm -rf /tmp/ifcconvert /tmp/ifcconvert.zip && \
        rm -rf /var/lib/apt/lists/* && \
        /usr/local/bin/IfcConvert --version || (echo "IfcConvert install verification failed" && exit 1) ; \
    fi

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Planscape.API.dll"]
