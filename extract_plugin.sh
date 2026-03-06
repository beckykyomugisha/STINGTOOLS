#!/bin/bash
# ──────────────────────────────────────────────────────────────────
#  StingTools Deploy Script (Git Bash)
#  Copies built DLLs + data files to Revit addins directory
# ──────────────────────────────────────────────────────────────────

set -e

# ── Configuration ─────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_CONFIG="${1:-Release}"
BUILD_DIR="$SCRIPT_DIR/StingTools/bin/$BUILD_CONFIG"
PLUGIN_DIR="$SCRIPT_DIR/CompiledPlugin"

# Revit addins directories (per-user)
REVIT_ADDINS_BASE="$APPDATA/Autodesk/Revit/Addins"

# ── Detect all Revit versions ────────────────────────────────────
REVIT_VERSIONS=()
for V in 2025 2026 2027; do
    if [ -d "$REVIT_ADDINS_BASE/$V" ]; then
        REVIT_VERSIONS+=("$V")
        echo "Found Revit $V addins directory."
    fi
done

if [ ${#REVIT_VERSIONS[@]} -eq 0 ]; then
    echo "WARNING: No Revit addins directory found."
    echo "Will create CompiledPlugin folder only."
fi

# ── Verify build output exists ───────────────────────────────────
if [ ! -f "$BUILD_DIR/StingTools.dll" ]; then
    echo "ERROR: StingTools.dll not found at: $BUILD_DIR"
    echo ""
    echo "Build first with:"
    echo "  ./build.bat           (Windows cmd)"
    echo "  dotnet build StingTools/StingTools.csproj -c $BUILD_CONFIG"
    exit 1
fi

# ── Create CompiledPlugin folder ─────────────────────────────────
echo ""
echo "══════════════════════════════════════════════════"
echo "  Deploying StingTools ($BUILD_CONFIG)"
echo "══════════════════════════════════════════════════"
echo ""

echo "[1/4] Creating plugin package..."
rm -rf "$PLUGIN_DIR"
mkdir -p "$PLUGIN_DIR/data"

# Copy main DLLs
cp "$BUILD_DIR/StingTools.dll" "$PLUGIN_DIR/"
cp "$BUILD_DIR/Newtonsoft.Json.dll" "$PLUGIN_DIR/" 2>/dev/null || true
cp "$BUILD_DIR/ClosedXML.dll" "$PLUGIN_DIR/" 2>/dev/null || true

# Copy all transitive dependencies (DocumentFormat.OpenXml, etc.)
for dll in "$BUILD_DIR"/*.dll; do
    base=$(basename "$dll")
    if [ "$base" != "StingTools.dll" ] && \
       [ "$base" != "RevitAPI.dll" ] && \
       [ "$base" != "RevitAPIUI.dll" ]; then
        cp "$dll" "$PLUGIN_DIR/" 2>/dev/null || true
    fi
done

# Copy data files
if [ -d "$BUILD_DIR/data" ]; then
    cp -r "$BUILD_DIR/data/"* "$PLUGIN_DIR/data/"
    echo "  Copied $(ls "$PLUGIN_DIR/data/" | wc -l) data files."
else
    echo "  WARNING: No data/ directory in build output."
fi

echo "  Plugin package: $PLUGIN_DIR/"

# ── Build Windows path for .addin manifest ───────────────────────
echo "[2/4] Generating .addin manifest..."

# Convert to Windows path (e.g. C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.dll)
# cygpath -w works in Git Bash / MSYS2; fallback to manual conversion
if command -v cygpath &>/dev/null; then
    WIN_DLL_PATH=$(cygpath -w "$PLUGIN_DIR/StingTools.dll")
else
    WIN_DLL_PATH=$(echo "$PLUGIN_DIR/StingTools.dll" | sed 's|^/\([a-zA-Z]\)/|\1:\\|; s|/|\\|g')
fi

echo "  Assembly path: $WIN_DLL_PATH"

# ── Write .addin manifest function ───────────────────────────────
write_addin() {
    local dest="$1"
    printf '<?xml version="1.0" encoding="utf-8"?>\r\n' > "$dest"
    printf '<RevitAddIns>\r\n' >> "$dest"
    printf '  <AddIn Type="Application">\r\n' >> "$dest"
    printf '    <Name>STING Tools</Name>\r\n' >> "$dest"
    printf '    <Assembly>%s</Assembly>\r\n' "$WIN_DLL_PATH" >> "$dest"
    printf '    <AddInId>A1B2C3D4-5678-9ABC-DEF0-123456789ABC</AddInId>\r\n' >> "$dest"
    printf '    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>\r\n' >> "$dest"
    printf '    <VendorId>StingBIM</VendorId>\r\n' >> "$dest"
    printf '    <VendorDescription>StingBIM - ISO 19650 BIM Automation</VendorDescription>\r\n' >> "$dest"
    printf '    <VendorEmail>support@stingbim.com</VendorEmail>\r\n' >> "$dest"
    printf '  </AddIn>\r\n' >> "$dest"
    printf '</RevitAddIns>\r\n' >> "$dest"
}

write_addin "$PLUGIN_DIR/StingTools.addin"
echo "  Generated StingTools.addin"

# ── Deploy to ALL Revit addins folders ────────────────────────────
echo "[3/4] Deploying to Revit..."

if [ ${#REVIT_VERSIONS[@]} -gt 0 ]; then
    for REVIT_VERSION in "${REVIT_VERSIONS[@]}"; do
        REVIT_DEST="$REVIT_ADDINS_BASE/$REVIT_VERSION"
        write_addin "$REVIT_DEST/StingTools.addin"
        echo "  Revit $REVIT_VERSION: deployed .addin to $REVIT_DEST/"
    done
else
    echo "  SKIPPED: No Revit installation found."
    echo "  Manually copy CompiledPlugin/StingTools.addin to:"
    echo "    %APPDATA%\\Autodesk\\Revit\\Addins\\2025\\"
fi

# ── Summary ──────────────────────────────────────────────────────
echo "[4/4] Done!"
echo ""
echo "══════════════════════════════════════════════════"
echo "  DEPLOY COMPLETE"
echo "══════════════════════════════════════════════════"
echo ""
echo "  Plugin folder: $(cygpath -w "$PLUGIN_DIR" 2>/dev/null || echo "$PLUGIN_DIR")"
echo "  Files:"
ls -1 "$PLUGIN_DIR/"*.dll 2>/dev/null | while read f; do echo "    $(basename "$f")"; done
echo "    data/ ($(ls "$PLUGIN_DIR/data/" 2>/dev/null | wc -l) files)"
echo ""
if [ ${#REVIT_VERSIONS[@]} -gt 0 ]; then
    echo "  Restart Revit (${REVIT_VERSIONS[*]}) to load the updated plugin."
else
    echo "  Next steps:"
    echo "    1. Copy CompiledPlugin/StingTools.addin to Revit addins folder"
    echo "    2. Edit <Assembly> path in .addin to point to CompiledPlugin/"
    echo "    3. Restart Revit"
fi
echo ""
