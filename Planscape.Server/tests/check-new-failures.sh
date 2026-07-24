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
#
# `dotnet test` on a SOLUTION prints the VSTest logger summary ("Total tests: N
# / Passed / Failed") and "Test Run Successful/Failed.", NOT the CLI's
# "Passed!/Failed!" line (that only appears for a single project on some SDKs).
# The old anchor matched neither, so a perfectly complete run was reported as
# "did not complete" and the gate was permanently red. Accept any of the shapes
# that only appear once the run has actually finished.
if ! grep -qE "^(Passed!|Failed!)|^Total tests: [0-9]+|^Test Run (Successful|Failed)\." "$OUTPUT"; then
  echo "::error::no test summary in output — the run did not complete"
  tail -30 "$OUTPUT"
  exit 1
fi

# Theory cases print as `Name(arg: "x", …) [FAIL]`. Requiring a space immediately
# before "[FAIL]" made the "(" end the match, so every failing theory case was
# invisible — a newly-broken theory would have sailed through. Match the optional
# argument list and strip it, collapsing cases to the base method name the
# baseline lists. (This half overlaps the fix in open PR #460.)
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
