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

# ── Write addin manifest with the ACTUAL deploy path ─────────────
# The checked-in StingTools.addin has a placeholder path
# (C:\Dev\STINGTOOLS\...). We generate a stamped copy at deploy
# time so the manifest always points at the real CompiledPlugin dir
# regardless of where the repo is cloned.
echo "  Writing stamped StingTools.addin..."

# Translate the POSIX deploy path back to a Windows path for the XML.
# On Git Bash / MSYS DEPLOY_DIR looks like /c/Users/…  or a plain
# Linux path on CI.  Try cygpath first; fall back to sed heuristic.
if command -v cygpath &>/dev/null; then
    WIN_DEPLOY="$(cygpath -w "$DEPLOY_DIR")"
elif [[ "$DEPLOY_DIR" =~ ^/([a-z])/ ]]; then
    WIN_DEPLOY="${BASH_REMATCH[1]^^}:${DEPLOY_DIR:2}"
    WIN_DEPLOY="${WIN_DEPLOY//\//\\}"
else
    WIN_DEPLOY="$DEPLOY_DIR"
fi

cat > "$DEPLOY_DIR/StingTools.addin" <<ADDIN_EOF
<?xml version="1.0" encoding="utf-8"?>
<!--
  CRITICAL DEPLOYMENT NOTE:
  This .addin file must exist in ONLY ONE of these locations:
    - Per-user:    %AppData%\Autodesk\Revit\Addins\2025\
    - Per-machine: %ProgramData%\Autodesk\Revit\Addins\2025\

  If copies exist in BOTH locations, Revit loads the plugin TWICE, causing:
    - Double-registration of dockable panes, external events, and IUpdaters
    - Race conditions and doubled memory usage
    - Crashes on startup or when clicking buttons

  Recommended: Use per-user (%AppData%) location ONLY.
  Also remove any legacy pyRevit extensions that duplicate StingTools functionality:
    - pyRevit_*_StingDocs.dll
    - pyRevit_*_STINGTags.dll
    - pyRevit_*_STINGTemp.dll
-->
<RevitAddIns>
  <AddIn Type="Application">
    <Name>STING Tools</Name>
    <Assembly>$WIN_DEPLOY\StingTools.dll</Assembly>
    <AddInId>A1B2C3D4-5678-9ABC-DEF0-123456789ABC</AddInId>
    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>
    <VendorId>Planscape</VendorId>
    <VendorDescription>Planscape - ISO 19650 BIM Automation</VendorDescription>
    <VendorEmail>support@planscape.app</VendorEmail>
    <!--
      Pack 5 - Assembly Load Context isolation (Revit 2026+).
      Loads STING into a private ALC so Newtonsoft.Json, ClosedXML, Lucene and
      other shared-dependency versions can't conflict with other add-ins that
      pin a different version. Revit 2025 silently ignores this element.
    -->
    <UseRevitContext>false</UseRevitContext>
  </AddIn>
</RevitAddIns>
ADDIN_EOF

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
