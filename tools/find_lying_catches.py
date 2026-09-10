#!/usr/bin/env python3
"""Find methods that report SUCCESS on a path where a caught exception left the
work undone.

    python tools/find_lying_catches.py            # report
    python tools/find_lying_catches.py --check    # CI gate: fail if the count moved

WHY THIS IS NOT A REGEX
-----------------------
The brief that commissioned this tried `catch { ... } return true;` as a regex and
got **0 sites** — a false negative, because the four confirmed cases have a closing
brace, a blank line and often a comment between the catch block and the return. A
regex over a brace language cannot see "the catch block ends here and the next
statement on this path is `return true`". So this brace-matches instead: it walks
each method body, finds each `catch` block, and asks what the method does after it.

WHAT COUNTS, AND WHAT DELIBERATELY DOES NOT
-------------------------------------------
Reported: a method returning `bool`/`Result` whose body contains a catch block that
falls through (no return/throw/continue/break inside it) to a `return true` or
`return Result.Succeeded` — i.e. the exception was caught, the work was abandoned,
and the caller was told it succeeded.

NOT reported, on purpose:

  * `catch { }` around an optional Revit read. CLAUDE.md section 4 records 683
    empty/near-empty catches, most of them the legitimate
    `try { x = el.get_Parameter(...); } catch { }` idiom. That is a different and
    far larger thing, and folding the two together produces a number nobody can
    act on. This asks only about catches that LIE ABOUT THE OUTCOME.
  * a catch that returns false, rethrows, or sets an error/out parameter.
  * void methods — they promise nothing, so they cannot break the promise.

The count is not a quality score. Each hit is a question: "did the caller need to
know that failed?" Some will legitimately be no.
"""
import argparse
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCAN_DIRS = ["StingTools"]
SKIP_DIR_PARTS = {"obj", "bin", ".git", ".vs"}

# The baseline is the count this sweep found when it was written. Raise it only
# with a note saying which method and why the caller does not need to know.
BASELINE = None  # set from BASELINE_FILE if present

BASELINE_FILE = os.path.join(ROOT, "tools", "lying_catches_baseline.txt")

SUCCESS_RETURNS = ("return true;", "return Result.Succeeded;")

METHOD_RE = re.compile(
    r"(?P<indent>[ \t]*)"
    r"(?:(?:public|private|protected|internal|static|virtual|override|sealed|async|new|partial)\s+)+"
    r"(?P<ret>bool|Result)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*\("
)


def strip_noise(src):
    """Blank out string/char literals and comments so brace counting is honest."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and nxt == "*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(ch if ch == "\n" else " " for ch in src[i:j]))
            i = j
        elif c == '"' and src[i - 1:i] == "@":
            j = i + 1
            while j < n:
                if src[j] == '"':
                    if src[j + 1:j + 2] == '"':
                        j += 2
                        continue
                    break
                j += 1
            j = min(j + 1, n)
            out.append("".join(ch if ch == "\n" else " " for ch in src[i:j]))
            i = j
        elif c in '"\'':
            q = c
            j = i + 1
            while j < n and src[j] != q:
                if src[j] == "\\":
                    j += 1
                j += 1
            j = min(j + 1, n)
            out.append(" " * (j - i))
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


def block_end(masked, open_brace):
    """Index just past the matching '}' for the '{' at open_brace."""
    depth = 0
    for i in range(open_brace, len(masked)):
        if masked[i] == "{":
            depth += 1
        elif masked[i] == "}":
            depth -= 1
            if depth == 0:
                return i + 1
    return -1


def find_methods(src, masked):
    """Yield (name, return_type, body_start, body_end) for bool/Result methods."""
    for m in METHOD_RE.finditer(masked):
        paren = masked.find("(", m.end() - 1)
        depth, i = 0, paren
        while i < len(masked):
            if masked[i] == "(":
                depth += 1
            elif masked[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        brace = masked.find("{", i)
        semi = masked.find(";", i)
        if brace < 0 or (0 <= semi < brace):
            continue                      # abstract / interface / expression-bodied
        end = block_end(masked, brace)
        if end < 0:
            continue
        yield m.group("name"), m.group("ret"), brace, end


def catch_blocks(masked, start, end):
    """Yield (catch_body_start, catch_body_end) inside [start, end)."""
    i = start
    while True:
        c = masked.find("catch", i)
        if c < 0 or c >= end:
            return
        brace = masked.find("{", c)
        if brace < 0 or brace >= end:
            return
        cend = block_end(masked, brace)
        if cend < 0 or cend > end:
            return
        yield brace, cend
        i = cend


def escapes(text):
    """True when the catch body leaves the method or the loop by itself."""
    return bool(re.search(r"\b(return|throw|continue|break|goto)\b", text))


# A catch that RECORDS the failure somewhere the caller or the user will see it is
# not lying, even when the method goes on to return success. The overwhelmingly
# common and legitimate shape in this codebase is:
#
#     foreach (row) { try { ... } catch (ex) { errors.Add(ex.Message); } }
#     TaskDialog.Show(... errors ...);
#     return Result.Succeeded;
#
# The command DID succeed, and it told the user what did not. Counting those
# produces a number nobody can act on - the exact trap the brief warned about
# with CLAUDE.md's 683 empty catches. So a catch only counts when it is SILENT
# about the outcome: it logs, or it does nothing, and nothing else on that path
# carries the failure out.
RECORDS = (
    ".Add(",          # errors.Add(...), failures.Add(...)
    "++",             # a failure counter
    "TaskDialog",     # told the user directly
    "message =",      # the IExternalCommand out-parameter
    "MessageBox",
    ".Append",        # a report being built
    "= false",        # a success flag being cleared
    "= true",         # an error flag being set
)


def records_the_failure(text):
    """True when the catch body puts the failure somewhere other than a log."""
    return any(tok in text for tok in RECORDS)


def scan_file(path):
    raw = io.open(path, encoding="utf-8", errors="replace").read()
    masked = strip_noise(raw)
    hits = []
    for name, ret, bstart, bend in find_methods(raw, masked):
        for cstart, cend in catch_blocks(masked, bstart, bend):
            body = masked[cstart:cend]
            if escapes(body):
                continue
            if records_the_failure(body):
                continue          # reported, not swallowed
            after = masked[cend:bend]
            # The first statement-ish thing after the catch, on this path.
            for token in SUCCESS_RETURNS:
                idx = after.find(token)
                if idx < 0:
                    continue
                between = after[:idx]
                # Only a straight fall-through counts: no other return, and no new
                # block opening between the catch and the success return.
                if re.search(r"\breturn\b", between):
                    continue
                # Closing braces are fine - they only mean the catch sat inside an if
                # or a loop and we have left it, which is precisely the shape of the
                # four confirmed cases in FamilyCommands.cs. An OPENING brace is not:
                # it means the success return is inside some other branch.
                #
                # Requiring the braces to BALANCE here is what made the first version
                # of this sweep miss all four - the same false negative, by a different
                # route, as the regex it was written to replace.
                if "{" in between:
                    continue
                if records_the_failure(between):
                    continue      # the failure is surfaced before the return
                line = raw[:cend].count("\n") + 1
                hits.append((os.path.relpath(path, ROOT).replace("\\", "/"),
                             line, name, ret, token))
                break
    return hits


def sweep():
    hits = []
    for d in SCAN_DIRS:
        for root, dirs, files in os.walk(os.path.join(ROOT, d)):
            dirs[:] = [x for x in dirs if x not in SKIP_DIR_PARTS]
            for f in files:
                if f.endswith(".cs"):
                    hits.extend(scan_file(os.path.join(root, f)))
    hits.sort()
    return hits


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true",
                    help="exit non-zero if the count exceeds the recorded baseline")
    ap.add_argument("--write-baseline", action="store_true")
    args = ap.parse_args()

    hits = sweep()

    # TWO TIERS, because one number across both is not actionable.
    #
    # Tier 1  a bool-returning HELPER. Its return value is the caller's only signal,
    #         so returning true after a caught failure is a lie with a consumer.
    #         All four confirmed cases were here.
    # Tier 2  an IExternalCommand.Execute returning Result.Succeeded. Sampled eight
    #         at random and every one was an optional side-effect AFTER the real work
    #         had been done and reported: opening Explorer, copying to the clipboard,
    #         refreshing a panel, setting a window owner, a legend that falls back to
    #         a default. The command did succeed. Listed for triage, not counted as a
    #         defect.
    tier1 = [h for h in hits if h[3] == "bool"]
    tier2 = [h for h in hits if h[3] != "bool"]

    print("Methods returning success on a path where a caught exception left the "
          "work undone: %d\n" % len(hits))
    print("  Tier 1 - bool-returning helpers, where the return value IS the outcome "
          "signal: %d" % len(tier1))
    for path, line, name, ret, token in tier1:
        print("      %s:%d  %s  ->  %s" % (path, line, name, token))
    print("\n  Tier 2 - Execute returning Result.Succeeded, usually an optional "
          "side-effect after the real work: %d" % len(tier2))
    for path, line, name, ret, token in tier2:
        print("      %s:%d  %s  ->  %s" % (path, line, name, token))
    print("")

    if args.write_baseline:
        io.open(BASELINE_FILE, "w", encoding="utf-8").write(str(len(hits)) + "\n")
        print(f"\nbaseline written: {len(hits)}")
        return 0

    if args.check:
        if not os.path.exists(BASELINE_FILE):
            sys.stderr.write("\nNo baseline recorded. Run --write-baseline.\n")
            return 1
        baseline = int(io.open(BASELINE_FILE, encoding="utf-8").read().strip())
        if len(hits) > baseline:
            sys.stderr.write(
                f"\nLYING-CATCH COUNT ROSE: {baseline} -> {len(hits)}.\n"
                "A method now reports success on a path where a caught exception left\n"
                "the work undone. Either fix it, or raise tools/lying_catches_baseline.txt\n"
                "with a note saying which method and why its caller does not need to know.\n")
            return 1
        if len(hits) < baseline:
            print(f"\nCount FELL {baseline} -> {len(hits)}. Lower the baseline.")
        else:
            print("\nCount matches the baseline.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
