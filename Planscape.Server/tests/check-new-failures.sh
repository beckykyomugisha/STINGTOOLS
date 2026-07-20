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
# Two summary formats have to be accepted. `dotnet test` on a single project
# prints the VSTest console summary ("Passed!  - Failed: 0, ..."). On a
# SOLUTION it goes through MSBuild, and when VSTestTask returns false that
# line is never emitted — the only summary is the MSBuild-style
# "Test Run Failed." / "Total tests: N". Matching just the former made this
# gate abort with "the run did not complete" on every solution-level run that
# had failures, which is precisely when its diff is worth reading.
if ! grep -qE "^\s*(Passed!|Failed!|Test Run (Successful|Failed)\.|Total tests:)" "$OUTPUT"; then
  echo "::error::no test summary in output — the run did not complete"
  tail -30 "$OUTPUT"
  exit 1
fi

grep -o "Planscape\.Tests\.[A-Za-z0-9_.]*\ \[FAIL\]" "$OUTPUT" \
  | sed 's/ \[FAIL\]//' | sort -u > /tmp/actual-failures.txt

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
