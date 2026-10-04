#!/bin/bash
# check_stage_routing.sh — proves extract_plugin.sh installs each stage into the Revit
# years build.bat asks for, and no others (ROADMAP ELEC-29). Runs on Linux CI and in Git
# Bash. It never touches a real Revit: APPDATA is a temporary folder, and the "build"
# is a dummy StingTools.dll.
#
# Cases:
#   1. STING_INSTALL_YEARS unset        -> every year (the pre-ELEC-29 behaviour)
#   2. STING_INSTALL_YEARS="2025 2026"  -> those two only
#   3. STING_INSTALL_YEARS="none"       -> no year. cmd cannot pass an empty variable
#      (set "X=" deletes it), and an unset value would fall back to case 1 — which is
#      exactly the bug a dry run of build.bat found: the 2026 compile-check stage
#      was installed into 2025, 2026 and 2027.
#   4. STING_INSTALL_YEARS="2027"       -> 2027 only, pointing at its own stage
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/build/data"
echo dummy > "$WORK/build/StingTools.dll"
echo '{}' > "$WORK/build/data/x.json"

fail=0
run_case() {   # name, install-years ("" = unset), stage dir name, expected years
  local name="$1" years="$2" stage="$3" expect="$4"
  local app="$WORK/app_$name"
  mkdir -p "$app/Autodesk/Revit/Addins/2025" "$app/Autodesk/Revit/Addins/2026" "$app/Autodesk/Revit/Addins/2027"
  (
    export APPDATA="$app" STING_DEPLOY=1 STING_DEPLOY_ALLOW_WORKTREE=1
    export STING_BUILD_DIR="$WORK/build" STING_STAGE_DIR="$WORK/$stage"
    if [ "$years" = "<unset>" ]; then unset STING_INSTALL_YEARS; else export STING_INSTALL_YEARS="$years"; fi
    bash "$ROOT/extract_plugin.sh" > "$WORK/$name.log" 2>&1
  ) || { echo "FAIL [$name]: extract_plugin.sh exited non-zero"; tail -5 "$WORK/$name.log"; fail=1; return; }
  for y in 2025 2026 2027; do
    local f="$app/Autodesk/Revit/Addins/$y/StingTools.addin"
    local want=0; for e in $expect; do [ "$e" = "$y" ] && want=1; done
    if [ "$want" = 1 ]; then
      if [ ! -f "$f" ] || ! grep -q "$stage" "$f"; then echo "FAIL [$name]: Addins/$y should point at $stage"; fail=1; fi
    elif [ -f "$f" ]; then
      echo "FAIL [$name]: Addins/$y was written but should not be"; fail=1
    fi
  done
  echo "ok   [$name] installs into: ${expect:-(none)}"
}

run_case default   "<unset>"   CompiledPlugin       "2025 2026 2027"
run_case primary   "2025 2026" CompiledPlugin       "2025 2026"
run_case checkonly "none"      CompiledPlugin-R2026 ""
run_case own2027   "2027"      CompiledPlugin-R2027 "2027"

# build.bat must never "clear" STING_INSTALL_YEARS: in cmd, set "X=" DELETES X, so the
# stage would be installed into every year (case 1). It passes "none" instead.
if grep -qiE 'set "STING_INSTALL_YEARS="' "$ROOT/build.bat"; then
  echo 'FAIL [build.bat]: set "STING_INSTALL_YEARS=" deletes the variable - pass "none"'; fail=1
else
  echo "ok   [build.bat] never passes an empty STING_INSTALL_YEARS"
fi

if [ "$fail" != 0 ]; then echo "Stage routing check FAILED."; exit 1; fi
echo "Stage routing check passed."
