#!/usr/bin/env python3
"""Assert that STING_TAG_TOKEN_POLICY.json still governs tagging.

WHY THIS EXISTS
===============
From 2026-08-10 to 2026-09-15 this file shipped, documented all ten ISO 19650 tag
tokens with a MANDATORY / DERIVED / OPTIONAL level and a per-token fallback, and was
named in two TagConfig comments — and NOTHING READ IT. The fallbacks were hardcoded
literals in TagConfig.BuildAndWriteTag. Editing the policy changed no behaviour, and
nothing anywhere said so.

That is the failure mode this codebase produces: not a crash, an authoritative-looking
artefact that governs nothing. A green build and valid JSON both agreed the file was
fine. Only reading the call sites showed otherwise.

So this gate checks two things a unit test cannot:

  1. The policy is READ by the tagging path. A test can prove TagTokenPolicy.Resolve
     behaves correctly while nothing calls it — which is exactly the state being
     guarded against.
  2. Every token's `param` names a real shared parameter. The policy claiming to
     govern ASS_FOO_TXT when no such parameter exists is the same class of defect one
     layer down.

Usage:  python tools/check_token_policy_wired.py [--check]
        --check exits 1 on any finding (the CI form). Without it, reports only.
"""

import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
POLICY = os.path.join(ROOT, "StingTools", "Data", "STING_TAG_TOKEN_POLICY.json")
SHARED_PARAMS = os.path.join(ROOT, "StingTools", "Data", "MR_PARAMETERS.txt")

# The call sites that make the policy live. Each entry is (file, regex, why).
# A rename that breaks one of these fails here rather than silently un-wiring the file.
REQUIRED_CALLS = [
    (
        os.path.join("StingTools", "Core", "TagConfig.cs"),
        r"TagTokenPolicyRegistry\s*\.\s*Get\s*\(",
        "BuildAndWriteTag must LOAD the policy for the document being tagged.",
    ),
    (
        os.path.join("StingTools", "Core", "TagConfig.cs"),
        r"TagTokenPolicy\s*\.\s*Resolve\s*\(",
        "BuildAndWriteTag must RESOLVE each token through the policy rather than "
        "substituting a literal.",
    ),
    (
        os.path.join("StingTools", "Core", "TagTokenPolicyRegistry.cs"),
        r"TagTokenPolicy\s*\.\s*Merge\s*\(",
        "The registry must layer the project override over the corporate baseline.",
    ),
    # TOKPOL-1. The policy governed the TAG STRING from Phase 288, but the DERIVATION layer
    # did not: SpatialAutoDetect returned the literals "Z01" and "BLD1" straight from
    # ParameterHelpers.cs, so a project that overrode those fallbacks still got the
    # hardcoded pair written onto every element and the tag could disagree with the
    # parameter it came from. PolicyFallback closes that; without it the derivation layer
    # silently stops honouring the policy again, which is invisible from the tag.
    (
        os.path.join("StingTools", "Core", "ParameterHelpers.cs"),
        r"PolicyFallback\s*\(",
        "The DERIVATION layer (DetectLoc / DetectZone) must take its fallback from the "
        "policy, not from a literal in the source.",
    ),
    (
        os.path.join("StingTools", "Core", "ParameterHelpers.cs"),
        r"TagTokenPolicy\s*\.\s*Resolve\s*\(",
        "PolicyFallback must actually ask the policy, not re-implement it.",
    ),
]

# Patterns that must NOT appear.
#
# The positive checks above are not enough on their own, and finding that out is the point
# of writing this comment. `PolicyFallback\(` matches the METHOD DEFINITION, so deleting
# every CALL to it left the gate green — a check satisfied by the existence of the thing
# rather than by its use. That is the third vacuous assertion in this phase, after a gate
# that read commented-out code as live data and one that scanned two files out of 1,656.
#
# So the real assertion is negative and specific: the literals must not be returned bare
# from the derivation layer. That cannot be satisfied by a declaration.
FORBIDDEN = [
    (
        os.path.join("StingTools", "Core", "ParameterHelpers.cs"),
        "DetectZone",
        r'return\s+"Z01"\s*;',
        'DetectZone returns the literal "Z01" again. The ZONE fallback belongs to '
        "STING_TAG_TOKEN_POLICY.json — a project that overrides it would be ignored, "
        "and the tag and the parameter could disagree about the same element.",
    ),
    (
        os.path.join("StingTools", "Core", "ParameterHelpers.cs"),
        "DetectLoc",
        r':\s*"BLD1"\s*;',
        'DetectLoc falls back to the literal "BLD1" again. Same defect as the ZONE one '
        "above: the policy stops being the single place that decides.",
    ),
]


def method_body(src, name):
    """From a method signature to the next method signature at the same indent.

    The forbidden patterns MUST be scoped to a method. Applied file-wide,
    /return "Z01";/ also matches `ParseZoneCode`, which legitimately maps "NORTH" to Z01 —
    that is a parsed RESULT, not a fallback, and failing on it would be crying wolf. The
    first draft of this check did exactly that, and went red on a correct tree.
    """
    m = re.search(r"public static string " + name + r"\s*\(", src)
    if not m:
        return None
    rest = src[m.end():]
    nxt = re.search(r"\n        (?:private|public|internal) static ", rest)
    return rest[:nxt.start()] if nxt else rest

# Literals that used to be substituted inline. Their return would mean a token stopped
# going through the policy. Matched as an ASSIGNMENT to a token variable, so ordinary
# mentions of "GEN" elsewhere in the file do not trip it.
BANNED_INLINE = re.compile(
    r"""\b(disc|loc|zone|lvl|sys|func|prod)\s*=\s*"(A|BLD1|Z01|L00|GEN)"\s*;""")


def fail(findings, msg):
    findings.append(msg)


def main():
    check = "--check" in sys.argv
    findings = []

    # ── 1. The policy file parses, and says what it must ────────────────────
    if not os.path.exists(POLICY):
        print("MISSING: " + POLICY)
        return 1
    with io.open(POLICY, encoding="utf-8-sig") as fh:
        policy = json.load(fh)

    tokens = policy.get("tokens") or []
    if len(tokens) < 10:
        fail(findings, "Policy declares only %d tokens; expected the ten ISO 19650 "
                       "tag tokens." % len(tokens))

    levels = {"MANDATORY", "DERIVED", "OPTIONAL"}
    for t in tokens:
        name = t.get("token") or "(unnamed)"
        if t.get("level") not in levels:
            fail(findings, "Token %s has level %r, which is not one of %s. Newtonsoft "
                           "leaves an unparsed level at the enum's zero value, which is "
                           "OPTIONAL — a typo here silently makes a token's blank legal."
                 % (name, t.get("level"), sorted(levels)))
        if not (t.get("why") or "").strip():
            fail(findings, "Token %s has no `why`. Every level here is a judgement call "
                           "and the next person needs the reasoning, not the verdict." % name)

    # ── 2. Every `param` names a real shared parameter ──────────────────────
    declared = set()
    if os.path.exists(SHARED_PARAMS):
        with io.open(SHARED_PARAMS, encoding="utf-8-sig", errors="replace") as fh:
            for line in fh:
                f = line.rstrip("\n").split("\t")
                if len(f) > 2 and f[0] == "PARAM" and f[2]:
                    declared.add(f[2])
    else:
        fail(findings, "MISSING: " + SHARED_PARAMS)

    if declared:
        for t in tokens:
            param = (t.get("param") or "").strip()
            if param and param not in declared:
                fail(findings, "Token %s names param %s, which MR_PARAMETERS.txt does "
                               "not declare." % (t.get("token"), param))

    # ── 3. The policy is actually READ ──────────────────────────────────────
    for rel, pattern, why in REQUIRED_CALLS:
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            fail(findings, "MISSING: %s — %s" % (rel, why))
            continue
        with io.open(path, encoding="utf-8-sig", errors="replace") as fh:
            src = fh.read()
        if not re.search(pattern, src):
            fail(findings,
                 "%s no longer matches /%s/.\n      %s\n      Without this call the "
                 "policy file is decorative again, which is the exact state this gate "
                 "exists to prevent." % (rel, pattern, why))

    for rel, method, pattern, why in FORBIDDEN:
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            continue
        with io.open(path, encoding="utf-8-sig", errors="replace") as fh:
            src = fh.read()
        body = method_body(src, method)
        if body is None:
            fail(findings, "%s: method %s not found — this check can no longer assert "
                           "anything." % (rel, method))
            continue
        if re.search(pattern, body):
            fail(findings,
                 "%s.%s matches the FORBIDDEN pattern /%s/.\n      %s"
                 % (rel, method, pattern, why))

    # ── 4. The inline literals have not come back ───────────────────────────
    tagconfig = os.path.join(ROOT, "StingTools", "Core", "TagConfig.cs")
    if os.path.exists(tagconfig):
        with io.open(tagconfig, encoding="utf-8-sig", errors="replace") as fh:
            for n, line in enumerate(fh, 1):
                if line.lstrip().startswith("//"):
                    continue
                m = BANNED_INLINE.search(line)
                if m:
                    fail(findings, "TagConfig.cs:%d substitutes %s = %r inline. That token "
                                   "has stopped going through the policy." % (n, m.group(1), m.group(2)))

    # ── 5. Report what the policy does NOT yet govern ───────────────────────
    #
    # Reported, never failed. The policy governs tag COMPOSITION (TagConfig). The
    # token DERIVATION layer — TokenAutoPopulator.PopulateAll and the SpatialAutoDetect
    # detectors — carries its own literals, and because PopulateAll writes LOC/ZONE onto
    # the element first, BuildAndWriteTag usually reads a non-empty value and the
    # policy's fallback for those two never fires.
    #
    # Listing them is the point. A gate that printed "OK" while half the fallbacks
    # lived somewhere else would be making the same claim the policy file itself made
    # for a month. See ROADMAP TOKPOL-1.
    ungoverned = []
    for rel in [os.path.join("StingTools", "Core", "ParameterHelpers.cs")]:
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            continue
        with io.open(path, encoding="utf-8-sig", errors="replace") as fh:
            for n, line in enumerate(fh, 1):
                if line.lstrip().startswith("//"):
                    continue
                m = BANNED_INLINE.search(line)
                if m:
                    ungoverned.append("%s:%d  %s = %r" % (rel, n, m.group(1), m.group(2)))

    # ── Report ──────────────────────────────────────────────────────────────
    print("Tag token policy")
    print("  tokens declared            : %d" % len(tokens))
    print("  shared parameters declared : %d" % len(declared))
    print("  required call sites        : %d" % len(REQUIRED_CALLS))
    print("  governed layer             : tag composition (TagConfig.BuildAndWriteTag, BuildSeqKey)")
    print("  NOT yet governed           : %d literal(s) in the derivation layer" % len(ungoverned))
    for u in ungoverned:
        print("      " + u)
    if ungoverned:
        print("      ^ reported, not failed. These run BEFORE composition and write the")
        print("        token parameters, so the policy's LOC/ZONE fallback values are")
        print("        usually unreachable through the normal pipeline. ROADMAP TOKPOL-1.")

    if findings:
        print("\nTOKEN POLICY GATE FAILED — %d finding(s):" % len(findings))
        for f in findings:
            print("  - " + f)
        print("\nThis file governed nothing for a month. A data file that looks")
        print("authoritative and is read by no one is worse than no file at all.")
        return 1 if check else 0

    print("\nToken policy gate OK.")
    print("  The policy parses, names only real parameters, and is read by the tagging path.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
