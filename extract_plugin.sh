#!/bin/bash
# ──────────────────────────────────────────────────────────────────
#  StingTools Plugin Deployment Script
#  Copies build output to CompiledPlugin/ and installs into Revit
#  per-user addins folder (%AppData%\Autodesk\Revit\Addins\STING\)
# ──────────────────────────────────────────────────────────────────

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/StingTools/bin/Release"
DEPLOY_DIR="$SCRIPT_DIR/CompiledPlugin"

echo "Creating STING Tools CompiledPlugin..."

# ── Verify build output exists ────────────────────────────────────
if [ ! -f "$BUILD_DIR/StingTools.dll" ]; then
    echo ""
    echo "ERROR: StingTools.dll not found at: $BUILD_DIR"
    echo "       Run build.bat first to compile the plugin."
    echo ""
    exit 1
fi

# ── Create deployment directory ───────────────────────────────────
mkdir -p "$DEPLOY_DIR"
mkdir -p "$DEPLOY_DIR/data"

# ── Copy DLLs ─────────────────────────────────────────────────────
echo "  Copying DLLs..."
cp -f "$BUILD_DIR/StingTools.dll" "$DEPLOY_DIR/"

# Copy dependency DLLs (Newtonsoft.Json, ClosedXML, etc.)
for dll in "$BUILD_DIR"/*.dll; do
    basename="$(basename "$dll")"
    # Skip Revit API DLLs (they're in Revit's own folder)
    case "$basename" in
        RevitAPI.dll|RevitAPIUI.dll) continue ;;
    esac
    cp -f "$dll" "$DEPLOY_DIR/"
done

# ── Copy data files ───────────────────────────────────────────────
echo "  Copying data files..."
if [ -d "$BUILD_DIR/data" ]; then
    cp -rf "$BUILD_DIR/data/"* "$DEPLOY_DIR/data/"
elif [ -d "$SCRIPT_DIR/StingTools/Data" ]; then
    cp -rf "$SCRIPT_DIR/StingTools/Data/"* "$DEPLOY_DIR/data/"
else
    echo "  WARNING: No data files found to copy."
fi

# ── Copy addin manifest ───────────────────────────────────────────
if [ -f "$SCRIPT_DIR/StingTools.addin" ]; then
    echo "  Copying StingTools.addin..."
    cp -f "$SCRIPT_DIR/StingTools.addin" "$DEPLOY_DIR/"
fi

# ── Auto-install into Revit per-user addins folder ────────────────
# Detect %APPDATA% on Windows/MSYS2/Git-Bash
APPDATA_WIN="${APPDATA:-}"
if [ -n "$APPDATA_WIN" ]; then
    # Convert Windows path (C:\Users\...) to Unix path (/c/Users/...)
    APPDATA_UNIX="$(echo "$APPDATA_WIN" | sed 's|\\|/|g' | sed 's|^\([A-Za-z]\):|/\L\1|')"
    STING_DIR="$APPDATA_UNIX/Autodesk/Revit/Addins/STING"

    if [ -d "$(dirname "$(dirname "$STING_DIR")")" ]; then
        echo "  Auto-installing to: $STING_DIR"
        mkdir -p "$STING_DIR/data"

        # Copy everything
        cp -f "$DEPLOY_DIR/StingTools.dll" "$STING_DIR/"
        for dll in "$DEPLOY_DIR"/*.dll; do
            cp -f "$dll" "$STING_DIR/" 2>/dev/null || true
        done
        cp -rf "$DEPLOY_DIR/data/"* "$STING_DIR/data/" 2>/dev/null || true

        # Write the .addin file pointing at the STING folder
        # for each installed Revit version
        for VER in 2025 2026 2027; do
            ADDIN_DIR="$APPDATA_UNIX/Autodesk/Revit/Addins/$VER"
            if [ -d "$ADDIN_DIR" ]; then
                cat > "$ADDIN_DIR/StingTools.addin" <<ADDINEOF
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>STING Tools</Name>
    <Assembly>$APPDATA_WIN\\Autodesk\\Revit\\Addins\\STING\\StingTools.dll</Assembly>
    <AddInId>A1B2C3D4-5678-9ABC-DEF0-123456789ABC</AddInId>
    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>
    <VendorId>Planscape</VendorId>
    <VendorDescription>Planscape - ISO 19650 BIM Automation</VendorDescription>
    <VendorEmail>support@planscape.app</VendorEmail>
    <UseRevitContext>false</UseRevitContext>
  </AddIn>
</RevitAddIns>
ADDINEOF
                echo "    → Revit $VER: $ADDIN_DIR/StingTools.addin"
            fi
        done
        echo "  ✓ Auto-install complete. Restart Revit to load."
    else
        echo "  (Revit addins directory not found — manual deploy required)"
    fi
fi

# ── Report ────────────────────────────────────────────────────────
DLL_COUNT=$(find "$DEPLOY_DIR" -maxdepth 1 -name "*.dll" | wc -l)
DATA_COUNT=$(find "$DEPLOY_DIR/data" -type f 2>/dev/null | wc -l)

echo ""
echo "═══════════════════════════════════════════════════"
echo " DEPLOYMENT READY"
echo "═══════════════════════════════════════════════════"
echo ""
echo "  CompiledPlugin: $DEPLOY_DIR"
echo "  DLLs:           $DLL_COUNT"
echo "  Data files:     $DATA_COUNT"
echo ""
echo "  Manual deploy (if auto-install was skipped):"
echo "    1. Create folder: %AppData%\\Autodesk\\Revit\\Addins\\STING\\"
echo "    2. Copy all files from CompiledPlugin\\ into that folder"
echo "    3. Copy StingTools.addin to:"
echo "       %AppData%\\Autodesk\\Revit\\Addins\\2025\\  (or 2026, 2027)"
echo "    4. Edit the .addin Assembly path to match your STING folder"
echo "    5. Restart Revit"
echo ""
