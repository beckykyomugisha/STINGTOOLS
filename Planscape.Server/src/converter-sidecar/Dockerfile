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

FROM node:20-alpine

# IfcOpenShell is brought in via a separate stage when needed for IFC →
# glTF; v1 only handles GLB chunking so the image stays light.
RUN apk add --no-cache curl python3 make g++

WORKDIR /app
COPY package.json server.js handlers/ /app/
RUN npm install --omit=dev

EXPOSE 7700

HEALTHCHECK --interval=30s --timeout=5s CMD curl -f http://localhost:7700/health || exit 1

CMD ["node", "server.js"]
