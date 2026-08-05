#!/usr/bin/env python3
"""KUT (Phase 192) offline pre-flight verifier.

Clears the PRECONDITIONS behind docs/examples/KUT/REVIT_SMOKE_TEST.md. It
executes no command and replaces no step — all 27 checklist steps still need a
Revit session. What it removes is the class of failure that is silent rather
than loud, and that would otherwise be discovered *during* that session (or
worse, not discovered at all, because the command ran and reported zeroes):

  * an unregistered shared parameter binds to nothing and every downstream
    audit reads empty;
  * a mistyped JSON key is left at its default by Newtonsoft — valid JSON,
    green build, dead rule;
  * a workflow commandTag that no longer resolves only surfaces mid-run,
    in front of the Owner;
  * a malformed regex in the CSI map is swallowed by a catch{}, and the rule
    then matches on category alone and assigns the WRONG section.

So the value is not "N of 27 steps automated" — it is that the Revit session is
spent on behaviour instead of on wiring. The report at the end of a run lists
every step with what is already pre-verified for it.

Usage:  python3 tools/kut_preflight.py [--verbose]
Exit:   0 = all checks pass, 1 = at least one FAIL.
"""

import csv
import json
import os
import re
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERBOSE = "--verbose" in sys.argv

FAILURES = []
WARNINGS = []
PASSES = []


def p(msg):
    PASSES.append(msg)
    if VERBOSE:
        print("  PASS  " + msg)


def warn(msg):
    WARNINGS.append(msg)
    print("  WARN  " + msg)


def fail(msg):
    FAILURES.append(msg)
    print("  FAIL  " + msg)


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8", errors="replace") as fh:
        return fh.read()


def read_json(rel):
    return json.loads(read(rel))


def exists(rel):
    return os.path.exists(os.path.join(ROOT, rel))


# ── the pack under test ──────────────────────────────────────────────────────

# Phase 192 shared parameters (+ the Phase 191 scheme param the pack depends on).
KUT_PARAMS = [
    "ASS_TAG_SCHEME_TXT",
    "ASS_LOD_VERIFIED_TXT",
    "CSI_SECTION_TXT",
    "CSI_TITLE_TXT",
    "FOHLIO_REF_TXT",
    "LTG_HOIST_WEIGHT_KG",
    "LTG_HOIST_MOTOR_TXT",
    "LTG_HOIST_DROP_MM",
]

# Every command tag the KUT pack dispatches, and where each must be wired.
KUT_TAGS = [
    "TokenConfidenceAudit",
    "TagScheme_Render", "TagScheme_Inspect", "TagScheme_Audit",
    "LOD_Verify", "LOD_Stamp",
    "Program_Audit", "OwnerStandards_Audit", "DeviceCoord_Audit",
    "CSI_Assign", "SpecLink_Reconcile",
    "Fohlio_Export", "Fohlio_Import", "Fohlio_Audit",
    "ReviewComments_Import", "ReviewComments_Dashboard", "ReviewComments_Export",
    "ComCheck_Export", "Hvac_LifeCycleCompare", "PrototypeDrift_Report",
    "Niagara_ExportPoints", "Niagara_Reconcile", "KUT_KpiDashboard",
    # ACC Model Coordination is the clash system of record for this engagement
    # (KUT README §4): STING pulls ACC's clash results, triages them, and pushes
    # the top-ranked back as ACC Issues.
    "AccPullClashes", "AccSyncIssueStatus",
]

# Tags the same command answers to on different surfaces. Both spellings must be
# reachable everywhere, or a workflow JSON written with the tag copied off a
# panel button fails to resolve at run time.
TAG_ALIASES = {
    "AccPullClashes": "ACC_PullClashes",
    "AccSyncIssueStatus": "ACC_SyncIssueStatus",
    "ComCheck_Export": "Lite_ComCheck",   # Electrical panel button spelling
}

# Every UI surface that can declare a button Tag. A command is "reachable" if
# any one of them offers it — the main dock panel is not the only surface
# (the Electrical/Plumbing/HVAC panels and the BIM Coordination Center each
# build their own action rows).
UI_SURFACES = [
    "StingTools/UI/StingDockPanel.xaml",
    "StingTools/UI/StingElectricalPanel.xaml",
    "StingTools/UI/StingHvacPanel.xaml",
    "StingTools/UI/Plumbing/StingPlumbingPanel.xaml",
    "StingTools/UI/BIMCoordinationCenter.cs",
]

KUT_WORKFLOWS = [
    "StingTools/Data/WORKFLOW_GateAudit.json",
    "StingTools/Data/WORKFLOW_KUT_Mobilisation.json",
    "StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json",
    "StingTools/Data/WORKFLOW_KUT_DeliverableD.json",
    "StingTools/Data/WORKFLOW_KUT_MonthlyReport.json",
    "StingTools/Data/WORKFLOW_KUT_FFESync.json",
]

# data file -> C# file whose POCOs define the accepted key set.
JSON_CONTRACTS = [
    ("StingTools/Data/STING_TAG_SCHEMES.json", "StingTools/Core/TagSchemeEngine.cs"),
    ("project-templates/KUT/_BIM_COORD/tag_schemes.json", "StingTools/Core/TagSchemeEngine.cs"),
    ("docs/examples/KUT/tag_schemes.json", "StingTools/Core/TagSchemeEngine.cs"),
    ("StingTools/Data/STING_LOD_MATRIX.json", "StingTools/Core/Validation/LodVerificationEngine.cs"),
    ("project-templates/KUT/_BIM_COORD/lod_matrix.json", "StingTools/Core/Validation/LodVerificationEngine.cs"),
    ("StingTools/Data/STING_OWNER_STANDARDS_PACK.json", "StingTools/Core/Validation/OwnerStandardsPack.cs"),
    ("project-templates/KUT/_BIM_COORD/owner_standards.json", "StingTools/Core/Validation/OwnerStandardsPack.cs"),
    ("project-templates/KUT/_BIM_COORD/fohlio_map.json", "StingTools/ExLink/FohlioLink.cs"),
    ("StingTools/Data/STING_DEVICE_COORD_RULES.json",
     "StingTools/Core/Validation/DeviceCoordination.cs"),
]

# CsiMasterFormat.LoadCsv splits each row into exactly 6 positional fields —
# column ORDER is the contract, not just presence.
CSI_COLUMNS = ["category", "familyregex", "typeregex", "sys", "section", "title"]

CANONICAL_TOKENS = {"DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ",
                    "STATUS", "REV"}
SEGMENT_KINDS = {"token", "projectinfo", "literal"}
SEVERITIES = {"BLOCK", "WARN", "INFO"}

# Keys that are documentation, not data — Newtonsoft ignores them by design.
COMMENT_KEYS = {"_meta", "_comment", "_note", "_readme", "$schema"}


# ── C# introspection ─────────────────────────────────────────────────────────

RE_JSONPROP = re.compile(r'\[JsonProperty\("([^"]+)"\)\]')
RE_PROP = re.compile(r"public\s+[\w<>,\[\]\?\.\(\) ]+?\s+(\w+)\s*\{\s*get;\s*set;")
# A Dictionary-valued property has free-form keys by design (a scheme's value
# `map`, a category rule's `checks` keyed by LOD). Its children are data, not
# property names, so key validation must stop there.
RE_DICT_PROP = re.compile(
    r'(?:\[JsonProperty\("([^"]+)"\)\]\s*)?public\s+(?:readonly\s+)?'
    r'Dictionary<[^>]*>\s+(\w+)\s*\{\s*get;\s*set;')


def csharp_keys(cs_rel):
    """Every JSON key Newtonsoft would bind in this file: explicit
    [JsonProperty] names plus bare property names (matched case-insensitively,
    which is Newtonsoft's default). Returns (bindable, opaque) where `opaque`
    names Dictionary-valued properties whose sub-keys are free-form."""
    txt = read(cs_rel)
    keys = set(RE_JSONPROP.findall(txt)) | set(RE_PROP.findall(txt))
    opaque = set()
    for alias, prop in RE_DICT_PROP.findall(txt):
        opaque.add((alias or prop).lower())
        opaque.add(prop.lower())
    return {k.lower() for k in keys}, opaque


def walk_keys(node, opaque, path="$"):
    """Yield (path, key) for every object key in the tree, not descending
    below a key named in `opaque`."""
    if isinstance(node, dict):
        for k, v in node.items():
            yield path, k
            if k.lower() in opaque:
                continue
            yield from walk_keys(v, opaque, path + "." + k)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from walk_keys(v, opaque, path + "[%d]" % i)


# ── checks ───────────────────────────────────────────────────────────────────

def check_param_registration():
    """Smoke step 3 — a param must be in all four registries or it binds to
    nothing. ParamRegistry constant, PARAMETER_REGISTRY.json, MR_PARAMETERS.txt,
    MR_PARAMETERS.csv."""
    print("\n[1] Shared-parameter registration (smoke step 3)")
    registry_cs = read("StingTools/Core/ParamRegistry.cs")
    registry_json = read("StingTools/Data/PARAMETER_REGISTRY.json")
    mr_txt = read("StingTools/Data/MR_PARAMETERS.txt")
    mr_csv = read("StingTools/Data/MR_PARAMETERS.csv")

    # GUID per param from MR_PARAMETERS.txt, to catch a duplicate GUID.
    guids = {}
    for line in mr_txt.splitlines():
        f = line.rstrip("\n").split("\t")
        if len(f) >= 8 and f[0] == "PARAM":
            guids[f[2]] = f[1]

    for name in KUT_PARAMS:
        missing = []
        if '"%s"' % name not in registry_cs:
            missing.append("ParamRegistry.cs")
        if '"%s"' % name not in registry_json:
            missing.append("PARAMETER_REGISTRY.json")
        if name not in guids:
            missing.append("MR_PARAMETERS.txt")
        if name not in mr_csv:
            missing.append("MR_PARAMETERS.csv")
        if missing:
            fail("%s not registered in: %s" % (name, ", ".join(missing)))
        else:
            p("%s registered in all 4 (guid %s)" % (name, guids[name]))

    dupes = {}
    for name, g in guids.items():
        dupes.setdefault(g, []).append(name)
    for g, names in dupes.items():
        if len(names) > 1 and any(n in KUT_PARAMS for n in names):
            fail("GUID %s shared by %s" % (g, ", ".join(sorted(names))))


def check_command_wiring():
    """Smoke steps 4-25 prerequisite — a tag must be in the dispatch handler,
    the workflow known-tags list, and ResolveCommand, or the button/chain
    silently no-ops."""
    print("\n[2] Command wiring (dispatch / known-tags / ResolveCommand)")
    handler = read("StingTools/UI/StingCommandHandler.cs")
    elec_handler = read("StingTools/UI/StingElectricalCommandHandler.cs")
    hvac_handler = read("StingTools/UI/StingHvacCommandHandler.cs")
    wf = read("StingTools/Core/WorkflowEngine.cs")
    surfaces = "\n".join(read(s) for s in UI_SURFACES if exists(s))

    known_block = wf.split("_allKnownCommandTags", 1)[1]
    known_block = known_block[:known_block.index("};")]
    resolve_block = wf[wf.index("private static IExternalCommand ResolveCommand"):]

    for tag in KUT_TAGS:
        spellings = [tag] + ([TAG_ALIASES[tag]] if tag in TAG_ALIASES else [])
        where = []
        if not any('"%s"' % s in handler or '"%s"' % s in elec_handler
                   or '"%s"' % s in hvac_handler for s in spellings):
            where.append("no dispatch-handler case")
        # Every spelling must resolve, not just one — the whole point of an
        # alias is that either can be written into a project workflow.
        unresolved = [s for s in spellings if '"%s"' % s not in resolve_block]
        if unresolved:
            where.append("not in ResolveCommand: " + ", ".join(unresolved))
        if where:
            fail("%s: %s" % (tag, "; ".join(where)))
        else:
            p("%s dispatches and resolves%s"
              % (tag, " (incl. alias %s)" % TAG_ALIASES[tag] if tag in TAG_ALIASES else ""))

        # A duplicated case label in ResolveCommand is CS0152 — a hard compile
        # error. Worth asserting here because alias registration is exactly the
        # edit that risks it, and this repo is often edited without an SDK.
        for s in spellings:
            n = len(re.findall(r'case\s+"%s"\s*:' % re.escape(s), resolve_block))
            if n > 1:
                fail("ResolveCommand has %d 'case \"%s\":' labels — CS0152 "
                     "duplicate case label" % (n, s))

        # _allKnownCommandTags only feeds the Levenshtein "did you mean" hint
        # for a mistyped tag — absence degrades that hint, it does not break
        # the command. Warn, don't fail.
        for s in spellings:
            if '"%s"' % s not in known_block:
                warn("%s missing from _allKnownCommandTags — a typo of this tag "
                     "gets no suggestion (cosmetic; one-line fix)" % s)

        if not any('"%s"' % s in surfaces for s in spellings):
            warn("%s has no button on any UI surface — reachable only via a "
                 "workflow preset or NLP" % tag)


def check_workflow_tags():
    """Smoke step 25 — every commandTag in a KUT preset must resolve, or the
    chain dies mid-run."""
    print("\n[3] Workflow presets resolve (smoke step 25)")
    wf = read("StingTools/Core/WorkflowEngine.cs")
    resolve_block = wf[wf.index("private static IExternalCommand ResolveCommand"):]

    for rel in KUT_WORKFLOWS:
        if not exists(rel):
            fail("%s missing" % rel)
            continue
        try:
            doc = read_json(rel)
        except Exception as exc:
            fail("%s does not parse: %s" % (rel, exc))
            continue
        steps = doc.get("steps") or []
        if not steps:
            fail("%s has no steps[]" % rel)
            continue
        bad = [s.get("commandTag") for s in steps
               if '"%s"' % (s.get("commandTag") or "") not in resolve_block]
        if bad:
            fail("%s: unresolvable commandTag(s) %s" % (os.path.basename(rel), bad))
        else:
            p("%s: %d/%d steps resolve" % (os.path.basename(rel), len(steps), len(steps)))


def check_json_key_contracts():
    """The silent-default class of bug (CLAUDE.md §7) — a key Newtonsoft does
    not recognise is dropped without error."""
    print("\n[4] JSON key contracts vs C# POCOs (silent-default guard)")
    for data_rel, cs_rel in JSON_CONTRACTS:
        if not exists(data_rel):
            warn("%s not present — skipped" % data_rel)
            continue
        try:
            doc = read_json(data_rel)
        except Exception as exc:
            fail("%s does not parse: %s" % (data_rel, exc))
            continue
        allowed, opaque = csharp_keys(cs_rel)
        unknown = []
        for path, key in walk_keys(doc, opaque):
            if key in COMMENT_KEYS or key.startswith("_"):
                continue
            if key.lower() not in allowed:
                unknown.append("%s.%s" % (path, key))
        if unknown:
            fail("%s: %d key(s) no POCO in %s binds — Newtonsoft drops these "
                 "silently: %s" % (data_rel, len(unknown),
                                   os.path.basename(cs_rel), ", ".join(unknown[:8])))
        else:
            p("%s: every key binds" % data_rel)


def check_domain_values():
    """Enum-ish values that are strings in JSON and switch cases in C#."""
    print("\n[5] Domain values (rule types, segment kinds, tokens, severities)")
    cs = read("StingTools/Core/Validation/OwnerStandardsPack.cs")
    rule_types = set(re.findall(r'case "(\w+)":', cs))

    for rel in ["StingTools/Data/STING_OWNER_STANDARDS_PACK.json",
                "project-templates/KUT/_BIM_COORD/owner_standards.json"]:
        if not exists(rel):
            continue
        for rule in read_json(rel).get("rules", []):
            rid = rule.get("id", "?")
            if rule.get("type") not in rule_types:
                fail("%s rule '%s': type '%s' has no evaluator case (valid: %s)"
                     % (os.path.basename(rel), rid, rule.get("type"),
                        ", ".join(sorted(rule_types))))
            sev = (rule.get("severity") or "WARN").upper()
            if sev not in SEVERITIES:
                fail("%s rule '%s': severity '%s' not in %s"
                     % (os.path.basename(rel), rid, sev, sorted(SEVERITIES)))
            for key in ("pattern",):
                if rule.get(key):
                    try:
                        re.compile(rule[key])
                    except re.error as exc:
                        fail("%s rule '%s': %s is not a valid regex (%s)"
                             % (os.path.basename(rel), rid, key, exc))
        p("%s: rule types/severities/regexes valid" % os.path.basename(rel))

    for rel in ["StingTools/Data/STING_TAG_SCHEMES.json",
                "project-templates/KUT/_BIM_COORD/tag_schemes.json",
                "docs/examples/KUT/tag_schemes.json"]:
        if not exists(rel):
            continue
        for scheme in read_json(rel).get("schemes", []):
            sid = scheme.get("id", "?")
            for i, seg in enumerate(scheme.get("segments", [])):
                kind = (seg.get("kind") or "token").lower()
                if kind not in SEGMENT_KINDS:
                    fail("%s scheme '%s' seg[%d]: kind '%s' not in %s"
                         % (os.path.basename(rel), sid, i, kind, sorted(SEGMENT_KINDS)))
                if kind == "token":
                    tok = (seg.get("token") or "").upper()
                    if tok not in CANONICAL_TOKENS:
                        fail("%s scheme '%s' seg[%d]: token '%s' is not a "
                             "canonical STING token %s"
                             % (os.path.basename(rel), sid, i, tok,
                                sorted(CANONICAL_TOKENS)))
                if kind == "projectinfo" and not seg.get("param"):
                    fail("%s scheme '%s' seg[%d]: projectInfo segment has no param"
                         % (os.path.basename(rel), sid, i))
            tp = scheme.get("targetParam")
            if tp and tp not in read("StingTools/Data/MR_PARAMETERS.txt"):
                fail("%s scheme '%s': targetParam %s is not a registered shared "
                     "parameter" % (os.path.basename(rel), sid, tp))
        p("%s: segment kinds/tokens/targets valid" % os.path.basename(rel))


def check_csi_map():
    """Smoke step 14 — CsiMasterFormat.LoadCsv is positional (f[0]..f[5]) and
    compiles FamilyRegex/TypeRegex inside `try { } catch { }`. A malformed
    regex is therefore swallowed, leaving the matcher null, and the rule then
    matches on category alone — assigning the WRONG CSI section rather than
    failing. That has to be caught here."""
    print("\n[6] CSI MasterFormat map (smoke step 14)")
    for rel in ["StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv",
                "project-templates/KUT/_BIM_COORD/csi_map.csv"]:
        if not exists(rel):
            continue
        rows = [r for r in read(rel).splitlines() if r.strip()
                and not r.lstrip().startswith("#")]
        if not rows:
            fail("%s has no data rows" % rel)
            continue
        hdr = [h.strip().lower() for h in rows[0].split(",")]
        if hdr[:6] != CSI_COLUMNS:
            fail("%s: column order is %s but the positional loader requires %s"
                 % (rel, hdr[:6], CSI_COLUMNS))
            continue
        bad_rx, short, bad_sec = [], 0, []
        sec_re = re.compile(r"^\d{2} \d{2} \d{2}(\.\d+)?$")
        for i, row in enumerate(rows[1:], start=2):
            f = row.split(",", 5)
            if len(f) < 6:
                short += 1          # LoadCsv silently `continue`s on these
                continue
            for col, val in (("FamilyRegex", f[1].strip()), ("TypeRegex", f[2].strip())):
                if val:
                    try:
                        re.compile(val)
                    except re.error as exc:
                        bad_rx.append("line %d %s=%r (%s)" % (i, col, val, exc))
            sec = f[4].strip()
            if sec and not sec_re.match(sec):
                bad_sec.append("line %d %r" % (i, sec))
        if bad_rx:
            fail("%s: %d regex(es) will not compile — CsiMasterFormat swallows "
                 "the failure and the rule then matches on category alone, "
                 "assigning the wrong section: %s"
                 % (rel, len(bad_rx), "; ".join(bad_rx[:4])))
        if short:
            fail("%s: %d row(s) have fewer than 6 fields — LoadCsv skips these "
                 "silently" % (rel, short))
        if bad_sec:
            warn("%s: %d section code(s) are not MasterFormat 'NN NN NN': %s"
                 % (rel, len(bad_sec), "; ".join(bad_sec[:4])))
        if not (bad_rx or short):
            p("%s: %d rules, column order correct, every regex compiles"
              % (rel, len(rows) - 1))


def check_device_coord_rules():
    """Smoke step 13 — rule severities and category lists must be usable."""
    print("\n[7] Device-coordination rules (smoke step 13)")
    rel = "StingTools/Data/STING_DEVICE_COORD_RULES.json"
    if not exists(rel):
        fail("%s missing" % rel)
        return
    rules = read_json(rel).get("rules", [])
    if not rules:
        fail("%s defines no rules" % rel)
        return
    for r in rules:
        rid = r.get("id", "?")
        sev = (r.get("severity") or "WARN").upper()
        if sev not in SEVERITIES:
            fail("%s rule '%s': severity '%s' not in %s"
                 % (os.path.basename(rel), rid, sev, sorted(SEVERITIES)))
        if not r.get("deviceCategories"):
            fail("%s rule '%s': deviceCategories is empty — the rule can never "
                 "select an element" % (os.path.basename(rel), rid))
    p("%s: %d rules, severities and device categories valid" % (rel, len(rules)))


def check_lod_matrix():
    """Smoke steps 9-10 — milestone ids and inherit chains must resolve."""
    print("\n[8] LOD matrix integrity (smoke steps 9-10)")
    base = read_json("StingTools/Data/STING_LOD_MATRIX.json")
    ids = {m["id"] for m in base.get("milestones", [])}
    lods = {str(m["lod"]) for m in base.get("milestones", [])}
    if not ids:
        fail("STING_LOD_MATRIX.json defines no milestones")
        return
    p("corporate matrix: %d milestones (%s)" % (len(ids), ", ".join(sorted(ids))))

    overlay_rel = "project-templates/KUT/_BIM_COORD/lod_matrix.json"
    if exists(overlay_rel):
        ov = read_json(overlay_rel)
        ov_ids = {m["id"] for m in ov.get("milestones", [])}
        new = ov_ids - ids
        if new:
            warn("overlay introduces milestone id(s) absent from the corporate "
                 "baseline: %s (intended? they will not exist for other projects)"
                 % ", ".join(sorted(new)))
        else:
            p("overlay milestones all activate existing baseline ids")

    for rel in ["StingTools/Data/STING_LOD_MATRIX.json", overlay_rel]:
        if not exists(rel):
            continue
        doc = read_json(rel)
        for rule in doc.get("categoryRules", []):
            cat = rule.get("category", "?")
            checks = rule.get("checks", {}) or {}
            for lod_key, chk in checks.items():
                if lod_key not in lods:
                    fail("%s '%s': checks key '%s' matches no milestone lod (%s)"
                         % (os.path.basename(rel), cat, lod_key, sorted(lods)))
                inh = (chk or {}).get("inherit")
                if inh and inh not in checks:
                    fail("%s '%s': check '%s' inherits '%s' which is not defined "
                         "on the same category" % (os.path.basename(rel), cat,
                                                   lod_key, inh))
        for pat in doc.get("placeholderFamilyPatterns", []):
            try:
                re.compile(pat)
            except re.error as exc:
                fail("%s: placeholder pattern %r invalid (%s)"
                     % (os.path.basename(rel), pat, exc))
        p("%s: category rules + inherit chains + patterns resolve"
          % os.path.basename(rel))


def check_overlay_merge():
    """The pack activates disabled corporate baselines by id. An id typo means
    the overlay adds a second entry instead of enabling the first."""
    print("\n[9] Project overlay merges onto corporate baseline by id")
    pairs = [
        ("project-templates/KUT/_BIM_COORD/tag_schemes.json",
         "StingTools/Data/STING_TAG_SCHEMES.json", "schemes"),
        ("project-templates/KUT/_BIM_COORD/owner_standards.json",
         "StingTools/Data/STING_OWNER_STANDARDS_PACK.json", "rules"),
    ]
    for ov_rel, base_rel, coll in pairs:
        if not exists(ov_rel):
            warn("%s missing" % ov_rel)
            continue
        base_ids = {x.get("id") for x in read_json(base_rel).get(coll, [])}
        for item in read_json(ov_rel).get(coll, []):
            iid = item.get("id")
            if iid in base_ids:
                p("%s: '%s' overrides the baseline entry" % (os.path.basename(ov_rel), iid))
            else:
                # Legitimate when the overlay genuinely adds a project rule;
                # a defect when the id was meant to activate a baseline entry
                # and was mistyped. Only a human can tell — so surface it.
                warn("%s: '%s' adds a NEW entry rather than overriding a "
                     "baseline one — confirm that is intended and not an id "
                     "typo" % (os.path.basename(ov_rel), iid))


def check_fixtures():
    """Smoke steps 11, 15, 19 — the fixtures must carry the columns the
    header-forgiving parsers look for."""
    print("\n[10] Test fixtures parse with the columns the parsers expect")
    specs = [
        ("Tests/fixtures/kut/speclink_toc_sample.csv", {"section", "title"}, "SpecLink_Reconcile"),
        ("Tests/fixtures/kut/bluebeam_comments_sample.csv",
         {"subject", "page label", "author", "date", "status"}, "ReviewComments_Import"),
    ]
    for rel, required, cmd in specs:
        if not exists(rel):
            fail("%s missing (smoke test references it)" % rel)
            continue
        with open(os.path.join(ROOT, rel), encoding="utf-8-sig") as fh:
            hdr = next(csv.reader(fh))
        got = {h.strip().lower() for h in hdr}
        missing = required - got
        if missing:
            fail("%s: %s needs column(s) %s" % (rel, cmd, sorted(missing)))
        else:
            p("%s: all columns %s present for %s" % (rel, sorted(required), cmd))

    xlsx = "Tests/fixtures/kut/program_template_sample.xlsx"
    if not exists(xlsx):
        fail("%s missing (smoke step 11 references it)" % xlsx)
        return
    try:
        # Cell text may sit in sharedStrings.xml OR inline in the sheet
        # (ClosedXML writes inline for small files) — scan both.
        text = []
        with zipfile.ZipFile(os.path.join(ROOT, xlsx)) as z:
            for name in z.namelist():
                if name == "xl/sharedStrings.xml" or name.startswith("xl/worksheets/"):
                    text.append(z.read(name).decode("utf-8", "replace"))
        low = "\n".join(text).lower()
        need = ["room name", "required area"]
        missing = [n for n in need if n not in low]
        if missing:
            fail("%s: no cell text matching %s — Program_Audit join will find "
                 "no header" % (xlsx, missing))
        else:
            p("%s: header cells for %s present" % (xlsx, need))
    except Exception as exc:
        fail("%s is not a readable xlsx: %s" % (xlsx, exc))


def check_deployment_pack():
    """The files the BIM Manager copies on day one must all be there."""
    print("\n[11] Deployment pack completeness")
    for rel in [
        "project-templates/KUT/README.md",
        "project-templates/KUT/_BIM_COORD/owner_standards.json",
        "project-templates/KUT/_BIM_COORD/lod_matrix.json",
        "project-templates/KUT/_BIM_COORD/tag_schemes.json",
        "project-templates/KUT/_BIM_COORD/fohlio_map.json",
        "docs/examples/KUT/project_config.json",
        "docs/examples/KUT/climate_data.json",
        "docs/examples/KUT/fohlio_connection.json.example",
        "docs/examples/KUT/REVIT_SMOKE_TEST.md",
    ]:
        if exists(rel):
            p("%s present" % rel)
        else:
            fail("%s missing" % rel)

    # No live Fohlio credentials may ship.
    ex = "docs/examples/KUT/fohlio_connection.json.example"
    if exists(ex):
        conn = read_json(ex)
        key = (conn.get("apiKey") or conn.get("ApiKey") or "")
        if key and not re.match(r"^(__|<|REPLACE|YOUR|\s*$)", key, re.I):
            fail("%s appears to carry a real apiKey — must be a placeholder" % ex)
        else:
            p("%s carries a placeholder key only" % ex)
    if exists("project-templates/KUT/_BIM_COORD/fohlio_connection.json"):
        fail("a real fohlio_connection.json is committed — remove it and "
             "gitignore the filename")

    # ACC credentials live in %APPDATA%\Planscape\acc_credentials.json by
    # design (AccIssueSync.LoadCredentials) — one must never reach the repo.
    for hit in ("project-templates/KUT/_BIM_COORD/acc_credentials.json",
                "docs/examples/KUT/acc_credentials.json"):
        if exists(hit):
            fail("%s is committed — ACC credentials belong in "
                 "%%APPDATA%%\\Planscape only" % hit)
    p("no ACC/Fohlio credential files committed")

    # Kampala must resolve for the climate auto-stamp.
    climate = read_json("StingTools/Data/STING_CLIMATE_DATA.json")
    sites = json.dumps(climate).lower()
    if "kampala" in sites:
        p("STING_CLIMATE_DATA.json carries a Kampala entry")
    else:
        fail("no Kampala entry in STING_CLIMATE_DATA.json — PRJ_CLIMATE_SITE_ID "
             "will fall back to london")


# Every step of the checklist that needs a running Revit session. This is
# nearly all of them, and saying so is the point: the pre-flight does NOT
# "cover" steps, it clears the *preconditions* those steps depend on — the
# registration, wiring and data-contract failures that would otherwise waste
# the Revit session or, worse, pass it silently with empty results.
MANUAL_ONLY = [
    (1, "Deploy build; dock panel loads clean", "needs Revit startup"),
    (2, "Copy _BIM_COORD overlays; set PRJ_ORG_*", "manual project setup"),
    (3, "Load Params binds all new params", "registration pre-verified; "
        "binding itself needs Revit"),
    (4, "Scheme Inspect shows enabled + valid", "scheme JSON pre-verified"),
    (5, "Batch Tag a sample area", "needs model elements"),
    (6, "Render Scheme back-fill", "needs elements with tokens"),
    (7, "Scheme Audit reports 0 mismatches", "needs a rendered model"),
    (8, "TokenConfidenceAudit ScopeBox provenance", "needs STING-LOC scope boxes"),
    (9, "LOD_Verify against real geometry", "needs geometry + params"),
    (10, "LOD_Stamp writes ASS_LOD_VERIFIED_TXT", "needs a transaction"),
    (11, "Program_Audit joins the Owner template", "fixture pre-verified; "
         "join needs placed rooms"),
    (12, "OwnerStandards_Audit RAG summary", "rules pre-verified; firing "
         "needs a model"),
    (13, "DeviceCoord_Audit door-swing case", "needs hosted families"),
    (14, "CSI_Assign writes section/title", "map pre-verified; assignment "
         "needs elements"),
    (15, "SpecLink_Reconcile gap report", "fixture pre-verified; run needs "
         "assigned CSI params"),
    (16, "Fohlio_Export column output", "needs FF&E elements"),
    (17, "Fohlio_Import diff dialog + ES snapshot", "needs WPF + ExtensibleStorage"),
    (18, "Fohlio_Audit stale detection", "needs an ES snapshot round-trip"),
    (19, "ReviewComments_Import upserts", "fixture pre-verified; import needs "
         "a project folder"),
    (20, "ReviewComments_Dashboard + KPI export", "needs WPF"),
    (21, "ComCheck per-space CSV", "needs spaces + luminaires"),
    (22, "LCC XLSX crossover year", "needs the HVAC panel input dialog"),
    (23, "PrototypeDrift_Report", "needs two models"),
    (24, "LPS NFPA 780 INFO note", "needs the LPS report path"),
    (25, "Gate Audit workflow runs end to end", "every tag pre-verified to "
         "resolve; the run itself needs Revit"),
    (26, "Lighting schedule hoist columns", "needs schedule creation"),
    (27, "Build Seeds baptismal font", "needs the family document API"),
]


def main():
    print("KUT Phase 192 pre-flight — offline verification of "
          "docs/examples/KUT/REVIT_SMOKE_TEST.md\n" + "=" * 74)
    check_param_registration()
    check_command_wiring()
    check_workflow_tags()
    check_json_key_contracts()
    check_domain_values()
    check_csi_map()
    check_device_coord_rules()
    check_lod_matrix()
    check_overlay_merge()
    check_fixtures()
    check_deployment_pack()

    print("\n" + "=" * 74)
    print("RESULT: %d pass, %d warn, %d fail" % (len(PASSES), len(WARNINGS), len(FAILURES)))
    if FAILURES:
        print("\nFailures:")
        for f in FAILURES:
            print("  - " + f)
    print("\nThis gate clears PRECONDITIONS; it does not execute any command.")
    print("All %d of the 27 checklist steps still need a Revit session — the "
          "annotations say\nwhat is already pre-verified, so the session is "
          "spent on behaviour, not on wiring:" % len(MANUAL_ONLY))
    for num, what, why in MANUAL_ONLY:
        print("  step %-2d  %-42s (%s)" % (num, what, why))
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
