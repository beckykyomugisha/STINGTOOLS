#!/bin/bash
# ──────────────────────────────────────────────────────────────────
#  StingTools Plugin Deployment Script
#  Copies build output to CompiledPlugin/ folder ready for Revit
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
    # Fallback: copy from source Data/ directory
    cp -rf "$SCRIPT_DIR/StingTools/Data/"* "$DEPLOY_DIR/data/"
else
    echo "  WARNING: No data files found to copy."
fi

# ── Copy addin manifest ───────────────────────────────────────────
if [ -f "$SCRIPT_DIR/StingTools.addin" ]; then
    echo "  Copying StingTools.addin..."
    cp -f "$SCRIPT_DIR/StingTools.addin" "$DEPLOY_DIR/"
fi

# ── Auto-deploy to Revit Addins folder (Phase 188 follow-up) ──────
# Manifest now points at CompiledPlugin/StingTools.dll, so the only
# manual step remaining is "copy StingTools.addin into Revit Addins
# folder once." This block does that automatically for Revit 2025/26/27
# common locations so every rebuild reaches Revit without manual copy.
if [ -n "$APPDATA" ]; then
    # APPDATA on Git Bash / MSYS may arrive in Windows form (C:\Users\...).
    # Try the value directly first; if that's not a directory, translate
    # backslashes to forward slashes and the drive letter to /c/ form.
    REVIT_ADDINS_BASE="$APPDATA/Autodesk/Revit/Addins"
    if [ ! -d "$REVIT_ADDINS_BASE" ]; then
        REVIT_ADDINS_BASE="$(echo "$APPDATA" | sed 's|\\|/|g; s|^\([A-Za-z]\):|/\L\1|')/Autodesk/Revit/Addins"
    fi
    deployed=0
    for ver in 2025 2026 2027; do
        target_dir="$REVIT_ADDINS_BASE/$ver"
        if [ -d "$target_dir" ]; then
            if cp -f "$DEPLOY_DIR/StingTools.addin" "$target_dir/StingTools.addin" 2>/dev/null; then
                echo "  Auto-deployed manifest to: $target_dir"
                deployed=$((deployed + 1))
            fi
        fi
    done
    if [ "$deployed" = "0" ]; then
        echo "  (No Revit Addins folder found under \$APPDATA — manual copy still needed.)"
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
echo "  Location:   $DEPLOY_DIR"
echo "  DLLs:       $DLL_COUNT"
echo "  Data files: $DATA_COUNT"
echo ""
echo "  Deploy to Revit:"
echo "    1. Update Assembly path in CompiledPlugin/StingTools.addin"
echo "       to point to: <your-plugin-folder>/StingTools.dll"
echo "    2. Copy StingTools.addin to your Revit addins folder:"
echo "       %APPDATA%/Autodesk/Revit/Addins/2025/  (or 2026, 2027)"
echo "    3. Copy CompiledPlugin/ contents to your plugin folder"
echo "    4. Restart Revit"
echo ""
