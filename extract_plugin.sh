#!/bin/bash
# ──────────────────────────────────────────────────────────────────
#  StingTools Plugin Extraction / Deployment Script
#  Copies built plugin files to a CompiledPlugin directory
#  ready for deployment to Revit addins folder.
# ──────────────────────────────────────────────────────────────────

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/CompiledPlugin"

echo "Creating STING Tools CompiledPlugin..."

# Find the built DLL — check common output paths
DLL_PATH=""
for candidate in \
    "$SCRIPT_DIR/StingTools/bin/Release/StingTools.dll" \
    "$SCRIPT_DIR/StingTools/bin/Release/net8.0-windows/StingTools.dll" \
    "$SCRIPT_DIR/StingTools/bin/Debug/StingTools.dll" \
    "$SCRIPT_DIR/StingTools/bin/Debug/net8.0-windows/StingTools.dll"; do
    if [ -f "$candidate" ]; then
        DLL_PATH="$candidate"
        break
    fi
done

if [ -z "$DLL_PATH" ]; then
    echo "ERROR: StingTools.dll not found. Run build.bat first."
    echo "  Searched:"
    echo "    StingTools/bin/Release/"
    echo "    StingTools/bin/Release/net8.0-windows/"
    echo "    StingTools/bin/Debug/"
    echo "    StingTools/bin/Debug/net8.0-windows/"
    exit 1
fi

BIN_DIR="$(dirname "$DLL_PATH")"
echo "  Found DLL at: $DLL_PATH"

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Copy main assemblies
cp "$BIN_DIR/StingTools.dll" "$OUTPUT_DIR/"
echo "  Copied StingTools.dll"

# Copy dependency DLLs if present
for dep in Newtonsoft.Json.dll ClosedXML.dll DocumentFormat.OpenXml.dll; do
    if [ -f "$BIN_DIR/$dep" ]; then
        cp "$BIN_DIR/$dep" "$OUTPUT_DIR/"
        echo "  Copied $dep"
    fi
done

# Copy data directory
if [ -d "$BIN_DIR/data" ]; then
    cp -r "$BIN_DIR/data" "$OUTPUT_DIR/"
    echo "  Copied data/ directory"
elif [ -d "$SCRIPT_DIR/StingTools/Data" ]; then
    mkdir -p "$OUTPUT_DIR/data"
    cp "$SCRIPT_DIR/StingTools/Data/"* "$OUTPUT_DIR/data/" 2>/dev/null || true
    echo "  Copied Data/ from source"
fi

# Copy addin manifest
if [ -f "$SCRIPT_DIR/StingTools.addin" ]; then
    cp "$SCRIPT_DIR/StingTools.addin" "$OUTPUT_DIR/"
    echo "  Copied StingTools.addin"
fi

echo ""
echo "═══════════════════════════════════════════════"
echo " Plugin extracted to: $OUTPUT_DIR"
echo "═══════════════════════════════════════════════"
echo ""
echo " Deploy to Revit:"
echo "   1. Copy CompiledPlugin contents to your plugin folder"
echo "   2. Update Assembly path in StingTools.addin"
echo "   3. Copy StingTools.addin to Revit addins folder:"
echo "      %APPDATA%\\Autodesk\\Revit\\Addins\\2025\\"
echo "   4. Restart Revit"
echo ""
