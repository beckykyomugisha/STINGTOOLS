#!/usr/bin/env bash
# Fail only on NEW test failures — those not listed in known-failing-tests.txt.
#
# Usage:  check-new-failures.sh <dotnet-test-output-file>
#
# The suite carries 66 uniquely-named pre-existing failures. Gating CI on "zero
# failures" makes it permanently red and therefore ignored; gating on "no new
# failures" makes it mean something today, without blocking on a backlog that
# predates this workflow.
set -uo pipefail

OUTPUT="${1:?usage: check-new-failures.sh <dotnet-test-output-file>}"
BASELINE="$(dirname "$0")/known-failing-tests.txt"

[ -f "$OUTPUT" ]   || { echo "::error::test output not found: $OUTPUT"; exit 1; }
[ -f "$BASELINE" ] || { echo "::error::baseline not found: $BASELINE"; exit 1; }

# A run that produced no summary line never got as far as running tests (build
# break, host crash). Treat that as failure — an empty failure list would
# otherwise read as "nothing new broke". Accept every summary shape `dotnet test`
# emits: it varies by logger and by whether the run was invoked at the solution
# or project level.
#   VSTest console logger (per project):  "Passed!  - Failed: …" / "Failed!  - …"
#   VSTest aggregate / older format:      "Total tests: N … Failed: N"
#   Microsoft.Testing.Platform (MSBuild): "Test summary: total: N … failed: N"
if ! grep -qE "^[[:space:]]*(Passed!|Failed!|Total tests:|Test summary:)" "$OUTPUT"; then
  echo "::error::no test summary in output — the run did not complete"
  tail -30 "$OUTPUT"
  exit 1
fi

# Theory cases print as `Name(arg: "x", …) [FAIL]`. The previous pattern
# required a space immediately before "[FAIL]", so the "(" ended the match and
# every failing theory case was invisible to this check — a newly-broken theory
# would have sailed through CI. Match the optional argument list and strip it,
# collapsing cases to the base method name the baseline file lists.
grep -oE "Planscape\.Tests\.[A-Za-z0-9_.]+(\(.*\))? \[FAIL\]" "$OUTPUT" \
  | sed -E 's/\(.*\)//; s/ \[FAIL\]//' | sort -u > /tmp/actual-failures.txt

# Belt-and-braces against a summary whose per-test "[FAIL]" lines we can't parse:
# if the run's own summary reports failures but we extracted NONE, the format is
# unrecognised and a silent "0 new failures" would be a false green. (A count
# mismatch is expected and fine — theory cases collapse to one method name here;
# this only fires on a total parse miss.)
reported_failed=$(grep -oiE "failed:[[:space:]]*[0-9]+" "$OUTPUT" \
  | grep -oE "[0-9]+" | sort -rn | head -1)
if [ "${reported_failed:-0}" -gt 0 ] && [ ! -s /tmp/actual-failures.txt ]; then
  echo "::error::summary reports ${reported_failed} failure(s) but none were parsed from the output —"
  echo "         the per-test [FAIL] format was not recognised. Pin the console logger, e.g."
  echo "         dotnet test … --logger 'console;verbosity=normal'"
  exit 1
fi

grep -vE '^\s*(#|$)' "$BASELINE" | sort -u > /tmp/baseline-failures.txt

NEW=$(comm -13 /tmp/baseline-failures.txt /tmp/actual-failures.txt)
FIXED=$(comm -23 /tmp/baseline-failures.txt /tmp/actual-failures.txt)

if [ -n "$FIXED" ]; then
  echo "::notice::These baseline failures now PASS — delete them from known-failing-tests.txt:"
  echo "$FIXED" | sed 's/^/  /'
fi

if [ -n "$NEW" ]; then
  echo "::error::NEW test failures (not in the baseline):"
  echo "$NEW" | sed 's/^/  /'
  echo
  echo "If one of these is an order/parallelism flake (ROADMAP DEP-7), confirm"
  echo "against main before treating it as a regression."
  exit 1
fi

echo "No new failures. ($(wc -l < /tmp/actual-failures.txt) failing, all known.)"
