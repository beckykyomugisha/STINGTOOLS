# S5.2 — Node sidecar that splits an uploaded GLB into per-discipline
# chunks via @gltf-transform/cli, applies Draco compression, and writes
# each chunk back via the API's tenant-scoped storage endpoint.
#
# Stays a separate container from the .NET API so:
#   1. .NET image weight stays small.
#   2. We can scale the sidecar horizontally without scaling the API.
#   3. Updates to gltf-transform don't require redeploying the API.
#
# Build:
#   docker build -t planscape/converter-sidecar:latest .
# Run:
#   docker run --rm -p 7700:7700 -e API_BASE=https://api.planscape.app planscape/converter-sidecar:latest

# Debian (glibc) base so the prebuilt IfcConvert binary runs — the
# ifcopenshell-builds binaries are glibc, not musl, so node:alpine can't run them.
FROM node:20-bookworm-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl unzip ca-certificates python3 make g++ \
 && rm -rf /var/lib/apt/lists/*

# IfcConvert (IfcOpenShell) for the /ifc-to-glb endpoint. ifcopenshell-builds
# uses content-hashed filenames, so the URL drifts per release — override at
# build time with --build-arg IFCCONVERT_URL=<current linux64 IfcConvert zip>
# (see https://github.com/IfcOpenShell/IfcOpenShell releases). The install is
# NON-FATAL: if it fails, the image still serves GLB chunking and /ifc-to-glb
# returns 501 until a working URL is supplied.
ARG IFCCONVERT_URL=https://s3.amazonaws.com/ifcopenshell-builds/IfcConvert-v0.8.0-linux64.zip
RUN (curl -fsSL "$IFCCONVERT_URL" -o /tmp/ifc.zip \
      && unzip -o /tmp/ifc.zip -d /usr/local/bin \
      && chmod +x /usr/local/bin/IfcConvert \
      && rm -f /tmp/ifc.zip \
      && IfcConvert --version) \
   || echo "WARN: IfcConvert not installed (set IFCCONVERT_URL) — /ifc-to-glb will return 501"

WORKDIR /app
COPY package.json server.js /app/
RUN npm install --omit=dev

EXPOSE 7700

HEALTHCHECK --interval=30s --timeout=5s CMD curl -f http://localhost:7700/health || exit 1

CMD ["node", "server.js"]
