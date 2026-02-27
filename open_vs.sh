#!/bin/bash
# Open Visual Studio with StingTools project
PROJECT_DIR="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
PROJECT="$PROJECT_DIR/StingTools/StingTools.csproj"

# Convert to Windows path for devenv
WIN_PROJECT="$(cygpath -w "$PROJECT" 2>/dev/null || echo "$PROJECT")"

# Try VS 2022 editions
for edition in Community Professional Enterprise; do
    devenv="/c/Program Files/Microsoft Visual Studio/2022/$edition/Common7/IDE/devenv.exe"
    if [ -f "$devenv" ]; then
        echo "Opening in VS 2022 $edition..."
        "$devenv" "$WIN_PROJECT" &
        exit 0
    fi
done

# Try VS 2019 editions
for edition in Community Professional Enterprise; do
    devenv="/c/Program Files (x86)/Microsoft Visual Studio/2019/$edition/Common7/IDE/devenv.exe"
    if [ -f "$devenv" ]; then
        echo "Opening in VS 2019 $edition..."
        "$devenv" "$WIN_PROJECT" &
        exit 0
    fi
done

# Fallback: try PATH
if command -v devenv.exe &>/dev/null; then
    echo "Opening in Visual Studio..."
    devenv.exe "$WIN_PROJECT" &
    exit 0
fi

echo "Visual Studio not found."
echo "Open manually: $WIN_PROJECT"
