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
# otherwise read as "nothing new broke".
if ! grep -qE "^(Passed!|Failed!)" "$OUTPUT"; then
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
