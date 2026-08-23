#!/usr/bin/env python3
"""Gate the three issued KUT documents against each other and against the data.

    python tools/check_kut_documents.py            # exits 0 or 1
    python tools/check_kut_documents.py --verbose  # also print what passed

WHY THIS EXISTS
`tools/check_smoke_test.py` proves the smoke-test checklist against the code it
describes. The issued pack had no equivalent. Its cross-document consistency was
verified exactly once, by an ad-hoc script that was never committed, and those
checks would rot the moment somebody edited one generator and not the others.

The pack is three documents that restate the same facts for three audiences:

    KUT_BIM_Execution_Plan.docx              what the project requires
    KUT_Project_Delivery_Playbook.docx       how a task team satisfies it
    KUT_Master_Information_Delivery_Plan.xlsx  when each deliverable lands

Restating a fact three times is a drift generator. A stage LOD corrected in the
BEP and not the playbook leaves two documents both claiming to be authoritative,
and the consultant reads whichever they were sent.

WHAT IT PROVES -- and what it cannot
It proves the pack is INTERNALLY CONSISTENT and that it MATCHES THE
CONFIGURATION the gate actually enforces:

  1. every document is a current regeneration and has not been hand-edited;
  2. stages, LODs, suitability codes, volumes, roles and document references
     agree across all three;
  3. the asset tier tables agree with project-templates/KUT/_BIM_COORD/
     lod_matrix.json, which is what the LOD gate runs against;
  4. no tooling is named in a document a client or consultant reads;
  5. the [FILL] placeholder count is not rising.

It proves NOTHING about whether the requirements are the RIGHT requirements, and
nothing about a real Revit model. A green run here does not mean the pack has
been validated by anyone; it means the pack does not contradict itself. The tier
membership, the clash tolerances and the originator code length are project
decisions and this gate deliberately takes no view on them.

NO PYTHON DEPENDENCIES. stdlib only, so it runs on a bare runner -- the same
constraint check_smoke_test.py works under. A .docx and a .xlsx are OPC zips of
XML; reading them needs no library.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kut_docs_lib as K  # noqa: E402

BEP = "KUT_BIM_Execution_Plan.docx"
PLAYBOOK = "KUT_Project_Delivery_Playbook.docx"
MIDP = "KUT_Master_Information_Delivery_Plan.xlsx"

OVERLAY = "project-templates/KUT/_BIM_COORD/lod_matrix.json"
BASELINE = "docs/examples/KUT/placeholder_baseline.json"


class Findings:
    def __init__(self):
        self.errors: list[str] = []
        self.notes: list[str] = []
        self.checked = 0

    def fail(self, where: str, msg: str):
        self.errors.append("%s: %s" % (where, msg))

    def ok(self, n: int = 1):
        self.checked += n

    def note(self, msg: str):
        self.notes.append(msg)


# -- helpers ----------------------------------------------------------------

def find_table(tables, *header):
    """The first table whose header row starts with `header`.

    Located by header signature rather than by index so that inserting a table
    earlier in the document does not silently repoint a check at the wrong one.
    Returns None if absent, which every caller reports as a failure: the table
    this gate reads having been renamed or removed is itself the finding.
    """
    want = [h.strip().lower() for h in header]
    for t in tables:
        if not t or not t[0]:
            continue
        got = [c.strip().lower() for c in t[0]]
        if got[:len(want)] == want:
            return t
    return None


def norm_lod(v: str):
    """A LOD cell as an int, or None where the stage declares none."""
    v = (v or "").strip()
    if v in ("", "-", "—", "n/a", "N/A", "--"):
        return None
    m = re.match(r"^(\d{3})$", v)
    return int(m.group(1)) if m else None


def stage_key(v: str):
    """The stage number out of any of the three spellings the pack uses.

    '2.1', '2.1 Deliverable A' and '2.1  Basis of Design (Deliverable A)' are
    the same stage. 'Mobilisation' and '0' are stage 0.
    """
    v = (v or "").strip()
    if not v:
        return None
    if v.lower().startswith("mobilisation") or v == "0":
        return "0"
    m = re.match(r"^(\d\.\d)", v)
    return m.group(1) if m else None


def cell(row, i):
    return row[i].strip() if i < len(row) else ""


# -- 1. every document is a current, un-edited regeneration ------------------

def check_freshness(root: Path, f: Findings, verbose: bool):
    for name in K.ISSUED:
        path = root / name
        if not path.exists():
            f.fail(name, "missing from the repository root")
            continue

        want = K.inputs_digest(root, name)
        got = K.read_inputs_stamp(path)
        if got is None:
            f.fail(name, "carries no inputs stamp. Regenerate it: "
                         "python %s" % K.GENERATED[name][0])
        elif got != want:
            f.fail(name,
                   "is STALE -- it was built from a different version of its "
                   "generator. Regenerate it: python %s\n"
                   "        stamped %s\n"
                   "        sources %s" % (K.GENERATED[name][0], got[:16], want[:16]))
        else:
            f.ok()

        want_parts = K.parts_digest(path)
        got_parts = K.read_parts_stamp(path)
        if got_parts is None:
            f.fail(name, "carries no content stamp. Regenerate it: "
                         "python %s" % K.GENERATED[name][0])
        elif got_parts != want_parts:
            f.fail(name,
                   "has been EDITED since it was generated -- its content no "
                   "longer matches its own stamp. A hand-edit to a generated "
                   "document is lost at the next regeneration; put the change "
                   "in %s and regenerate." % K.GENERATED[name][0])
        else:
            f.ok()
        if verbose:
            print("  fresh + unedited: %s" % name)


# -- 2. the three documents agree with each other ----------------------------

def check_stages(bep_t, pb_t, midp, f: Findings, verbose: bool):
    """Stage -> LOD must be one answer across BEP, playbook and MIDP."""
    sources: dict[str, dict[str, int]] = {}

    t = find_table(bep_t, "Milestone", "Stage", "LOD")
    if t is None:
        f.fail(BEP, 'no "Milestone / Stage / LOD" table -- the stage programme '
                    "this gate cross-checks is gone or renamed")
    else:
        got = {}
        for row in t[1:]:
            k, lod = stage_key(cell(row, 1)), norm_lod(cell(row, 2))
            if k and lod is not None:
                got[k] = lod
        sources[BEP + " (programme)"] = got

    t = find_table(bep_t, "Stage", "Geometry (LOD)")
    if t is not None:
        got = {}
        for row in t[1:]:
            k, lod = stage_key(cell(row, 0)), norm_lod(cell(row, 1))
            if k and lod is not None:
                got[k] = lod
        sources[BEP + " (LOD definition)"] = got

    t = find_table(pb_t, "Stage", "Name", "Months", "LOD")
    if t is None:
        f.fail(PLAYBOOK, 'no "Stage / Name / Months / LOD" table')
    else:
        got = {}
        for row in t[1:]:
            k, lod = stage_key(cell(row, 0)), norm_lod(cell(row, 3))
            if k and lod is not None:
                got[k] = lod
        sources[PLAYBOOK] = got

    rows, idx = midp_register(midp, f)
    if rows is not None:
        got = {}
        for r in rows:
            k, lod = stage_key(cell(r, idx["Stage"])), norm_lod(cell(r, idx["LOD"]))
            if k and lod is not None:
                # The register carries one row per deliverable, so a stage
                # appears many times. Disagreement WITHIN the register is a
                # finding in its own right.
                if k in got and got[k] != lod:
                    f.fail(MIDP, "stage %s carries LOD %d on one row and %d on "
                                 "another" % (k, got[k], lod))
                got[k] = lod
        sources[MIDP] = got

    all_keys = sorted({k for s in sources.values() for k in s})
    for k in all_keys:
        claims = {src: s[k] for src, s in sources.items() if k in s}
        if len(set(claims.values())) > 1:
            detail = "; ".join("%s says LOD %d" % (src, v) for src, v in sorted(claims.items()))
            f.fail("stage %s" % k,
                   "the documents disagree on the level of development. %s. "
                   "A consultant reads whichever document they were sent." % detail)
        else:
            f.ok()
    if verbose:
        print("  stage/LOD agreement: %d stages across %d sources"
              % (len(all_keys), len(sources)))


def check_suitability(bep_t, pb_t, midp, f: Findings, verbose: bool):
    """The suitability vocabulary must be the same in all three."""
    def codes(table, col):
        out = set()
        for row in table[1:]:
            v = cell(row, col)
            m = re.match(r"^([SAB]\d)", v)
            if m:
                out.add(m.group(1))
        return out

    t = find_table(bep_t, "Code", "Meaning", "State")
    b = codes(t, 0) if t is not None else None
    if t is None:
        f.fail(BEP, 'no "Code / Meaning / State" suitability table')

    t = find_table(pb_t, "Suitability", "Meaning", "CDE state")
    p = codes(t, 0) if t is not None else None
    if t is None:
        f.fail(PLAYBOOK, 'no "Suitability / Meaning / CDE state" table')

    rows, idx = midp_register(midp, f)
    m = set()
    if rows is not None:
        for r in rows:
            v = cell(r, idx["Suitability"])
            if v:
                m.add(v)

    if b and p and b != p:
        f.fail("suitability codes",
               "the BEP defines %s and the playbook defines %s"
               % (sorted(b), sorted(p)))
    elif b and p:
        f.ok()

    if b and m:
        unknown = m - b
        if unknown:
            f.fail(MIDP, "uses suitability code(s) %s that the BEP does not "
                         "define" % sorted(unknown))
        else:
            f.ok()
    if verbose and b:
        print("  suitability vocabulary: %s" % sorted(b))


def check_volumes(bep_t, pb_t, f: Findings, verbose: bool):
    """The volume register must be identical in the BEP and the playbook."""
    def rows_of(table):
        return [(cell(r, 0), cell(r, 1), cell(r, 2)) for r in table[1:] if cell(r, 0)]

    a = find_table(bep_t, "Volume code", "Volume", "Numbering value")
    b = find_table(pb_t, "Volume code", "Volume", "Numbering value")
    if a is None:
        f.fail(BEP, 'no "Volume code / Volume / Numbering value" table')
        return
    if b is None:
        f.fail(PLAYBOOK, 'no "Volume code / Volume / Numbering value" table')
        return
    ra, rb = rows_of(a), rows_of(b)
    if ra != rb:
        only_a = [x for x in ra if x not in rb]
        only_b = [x for x in rb if x not in ra]
        f.fail("volume register",
               "the BEP and the playbook disagree. Only in the BEP: %s. "
               "Only in the playbook: %s. The volume is the second field of "
               "every container name, so a mismatch misnumbers files."
               % (only_a or "none", only_b or "none"))
    else:
        f.ok()
        if verbose:
            print("  volume register: %d volumes, identical in both" % len(ra))


def check_references(root: Path, f: Findings, verbose: bool):
    """A document reference cited anywhere must resolve to the document that
    claims it."""
    claimed = {}
    for name in K.ISSUED:
        text = read_text(root / name)
        t = re.search(r"Document reference\s*(KUT-[A-Z0-9\-]+)", text)
        if not t:
            # The .docx tables put the label and value in separate cells.
            t = re.search(r"(KUT-PLN-[A-Z0-9\-]+)", text)
        if t:
            claimed[name] = t.group(1)
        else:
            f.fail(name, "states no document reference of its own")

    for name in K.ISSUED:
        text = read_text(root / name)
        for ref in set(re.findall(r"KUT-PLN-[A-Z0-9\-]{10,}", text)):
            owners = [n for n, c in claimed.items() if c == ref]
            if ref == claimed.get(name):
                continue
            if not owners:
                # Not every reference names a document in this pack -- the
                # playbook cites container names as worked examples. Only a
                # reference in the report/schedule series is expected to
                # resolve; the rest are illustrations.
                if re.search(r"-(RP|SC)-", ref):
                    f.fail(name, "cites %s, which no document in the pack "
                                 "claims as its own reference" % ref)
                continue
            f.ok()
    if verbose:
        print("  document references: %s"
              % ", ".join("%s=%s" % (n.split("_")[1], r) for n, r in claimed.items()))


def check_roles(bep_t, pb_t, midp, f: Findings, verbose: bool):
    """Every party the register makes responsible must be defined in the pack.

    Defined ANYWHERE in the pack, not just in the playbook: the BEP project
    team table is a definition too, and the three documents are one set. A role
    that appears in the BEP but not the playbook is reported as an advisory
    rather than a failure -- the playbook is what a task team works from, so the
    omission is worth seeing, but adding a role to an issued document is an
    editorial decision and not a gate's to force.
    """
    pb = find_table(pb_t, "Role", "Held by", "Responsible for")
    if pb is None:
        f.fail(PLAYBOOK, 'no "Role / Held by / Responsible for" table')
        return
    playbook_roles = {cell(r, 0) for r in pb[1:] if cell(r, 0)}

    bep = find_table(bep_t, "Role", "Organisation", "Name", "Contact")
    bep_roles = {cell(r, 0) for r in bep[1:] if cell(r, 0)} if bep is not None else set()
    # "Task Team Manager - Architecture and interiors" defines "Task Team Manager".
    bep_roles |= {re.split(r"\s+[-–—(]", r)[0].strip() for r in bep_roles}
    defined = playbook_roles | bep_roles

    rows, idx = midp_register(midp, f)
    if rows is None:
        return
    responsible = {cell(r, idx["Responsible"]) for r in rows if cell(r, idx["Responsible"])}

    # The register names discipline leads ("MEP lead") as well as formal roles.
    # Only the formal ISO 19650 roles must be defined verbatim; a discipline
    # lead is a task team member, covered by the generic entry.
    formal = {r for r in responsible
              if r in defined or re.search(r"(Party|Manager|Surveyor|Contractor|Coordinator|Designer)$", r)}
    missing = sorted(r for r in formal if r not in defined)
    if missing:
        f.fail(MIDP, "makes %s responsible for deliverables, but no document in "
                     "the pack defines %s"
                     % (missing, "them" if len(missing) > 1 else "it"))
    else:
        f.ok()

    for r in sorted(formal - playbook_roles):
        f.note("%r is assigned deliverables in the register and defined in the "
               "BEP, but the playbook role table does not list it. The playbook "
               "is the document a task team works from. Advisory, not a failure "
               "- see check_roles() for why a gate does not edit an issued "
               "document." % r)

    if verbose:
        print("  roles: %d defined across the pack, %d distinct responsible "
              "parties in the register" % (len(defined), len(responsible)))


# -- 3. the tier tables agree with the data the gate runs on -----------------

# The issued documents deliberately never name a parameter, so something has to
# bridge the plain-English row label to the field the LOD gate checks. The
# bridge belongs here, in internal tooling, rather than in the documents.
#
# Only rows that become a rung-500 requirement are listed. 'Asset identifier',
# 'Product code' and 'Manufacturer and model reference' are required from
# earlier rungs for every tier and are not tier-distinguishing, so they are not
# cross-checked here -- build_kut_lod_overlay.py owns them.
DATA_ROW_TO_PARAM = {
    "Unique asset reference": ("ASS_ASSET_ID_TXT",),
    "Serial number": ("ASS_SERIAL_NR_TXT",),
    "Loop and address": ("FLS_SFTY_DEV_LOOP_TXT", "FLS_SFTY_DEV_ADDRESS_TXT"),
    "Installation date": ("ASS_INSTALLATION_DATE_TXT",),
    "Supplier": ("ASS_SUPPLIER_TXT",),
    "Warranty guarantor": ("ASS_WARRANTY_PARTS_TXT",),
    "Warranty duration": ("ASS_WARRANTY_DURATION_PARTS_YRS",),
    "Warranty expiry date": ("MNT_WARRANTY_EXPIRY_TXT",),
    "Expected service life": ("ASS_EXPECTED_LIFE_YEARS_YRS",),
    "Maintenance interval": ("ASS_MAINTENANCE_FREQUENCY_MONTHS",),
    "Recommended spares": ("MNT_SPARE_PARTS_TXT",),
    "Commissioning date": ("COMM_DATE_TXT",),
    "FF&E reference": ("FOHLIO_REF_TXT",),
}

# The tier a document row describes, keyed by the leading token of its label.
TIER_OF_LABEL = {"a": "A", "b": "B", "c": "C", "ff&e": "FF&E", "d": "D"}

ALL_TRACKED = {p for ps in DATA_ROW_TO_PARAM.values() for p in ps}


def overlay_tiers(root: Path, f: Findings):
    """category -> set of parameters required at rung 500, from the overlay.

    Read from the committed JSON rather than from the generator's tier lists,
    because the JSON is what the LOD gate actually runs against. If the
    documents and the JSON disagree, a contractor is failed for a field no
    document asked them for -- which is the failure this check exists to stop.
    """
    path = root / OVERLAY
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        f.fail(OVERLAY, "cannot be read: %s" % exc)
        return None
    out = {}
    for rule in doc.get("categoryRules") or []:
        c500 = (rule.get("checks") or {}).get("500") or {}
        out[rule.get("category")] = {p.lstrip("+") for p in c500.get("requiredParams") or []}
    return out


def names_category(prose: str, category: str) -> bool:
    """Does `prose` name `category`?

    The documents write "Lighting fixtures" where the overlay says "Lighting
    Fixtures", and compress "Curtain Panels" and "Curtain Wall Mullions" into
    "curtain panels and mullions". So the full name is tried first and the head
    noun second. Deliberately not fuzzier than that: a loose match would let a
    category drift between tiers unnoticed, which is the whole point of the
    check.
    """
    prose = prose.lower()
    cat = category.lower()
    if cat in prose:
        return True
    tail = cat.split()[-1]
    return len(tail) > 4 and tail in prose


def document_tiers(tier_tbl, f: Findings, doc_name: str, per_cat):
    """category -> tier, as the DOCUMENT assigns it.

    Membership comes from the document and the required fields come from the
    JSON. Deriving both from the JSON would be circular -- an earlier version of
    this check did exactly that, inferring the tier from the parameters and then
    checking the parameters against the tier, so moving a category between tiers
    changed its inferred tier too and the contradiction cancelled out. It passed
    a deliberate break. Membership must come from the side being checked.
    """
    assigned = {}
    for row in tier_tbl[1:]:
        label = cell(row, 0)
        if not label.split():
            continue
        tier = TIER_OF_LABEL.get(label.split()[0].lower())
        if tier is None:
            f.fail(doc_name, "tier row %r does not begin with a tier letter" % label)
            continue
        if tier == "D":
            continue                      # "every other category", named by exclusion
        prose = cell(row, 2)
        for cat in per_cat:
            if names_category(prose, cat):
                if cat in assigned and assigned[cat] != tier:
                    f.fail(doc_name, "places %r in both tier %s and tier %s"
                                     % (cat, assigned[cat], tier))
                assigned[cat] = tier
    return assigned


def expected_params(data_tbl, tier: str, category: str, f: Findings, doc_name: str):
    """What the document's requirement table asks for, for one category."""
    header = [c.strip() for c in data_tbl[0]]
    if tier not in header:
        return None
    col = header.index(tier)
    want = set()
    for row in data_tbl[1:]:
        params = DATA_ROW_TO_PARAM.get(cell(row, 0))
        if params is None:
            continue                      # required from an earlier rung
        v = cell(row, col).lower()
        if v.startswith("yes"):
            want.update(params)
        elif "only" in v:
            # "Fire alarm devices only" -- required for the named subset alone.
            subset = v.replace("only", "").strip()
            if subset and names_category(subset, category):
                want.update(params)
    return want


def check_tiers(root: Path, bep_t, pb_t, f: Findings, verbose: bool):
    per_cat = overlay_tiers(root, f)
    if per_cat is None:
        return
    pinned = {c: p for c, p in per_cat.items() if p}

    for doc_name, tables in ((BEP, bep_t), (PLAYBOOK, pb_t)):
        tier_tbl = find_table(tables, "Tier", "What it covers", "Categories")
        if tier_tbl is None:
            f.fail(doc_name, 'no "Tier / What it covers / Categories" table')
            continue
        data_tbl = find_table(tables, "Data", "A", "B", "C", "FF&E")
        if data_tbl is None:
            f.fail(doc_name, 'no "Data / A / B / C / FF&E" requirement table')
            continue

        assigned = document_tiers(tier_tbl, f, doc_name, pinned)

        unclaimed = sorted(c for c in pinned if c not in assigned)
        if unclaimed:
            f.fail(doc_name,
                   "the gate requires close-out data for %s, but section 14 "
                   "places %s in no tier. A contractor would be failed for a "
                   "field no document asked them for."
                   % (", ".join(unclaimed), "them" if len(unclaimed) > 1 else "it"))
        else:
            f.ok()

        for cat in sorted(assigned):
            tier = assigned[cat]
            want = expected_params(data_tbl, tier, cat, f, doc_name)
            if want is None:
                continue                  # the document has no column for this tier
            got = pinned[cat] & ALL_TRACKED
            if want == got:
                f.ok()
                continue
            missing = sorted(want - got)   # document asks, gate does not check
            extra = sorted(got - want)     # gate checks, document never asked
            bits = []
            if extra:
                bits.append("the gate requires %s, which section 14 does not ask "
                            "for at tier %s" % (", ".join(extra), tier))
            if missing:
                bits.append("section 14 requires %s at tier %s, which the gate "
                            "does not check" % (", ".join(missing), tier))
            f.fail(doc_name,
                   "%s: %s. The documents tell a contractor what to capture and "
                   "the gate decides whether they passed; when the two disagree "
                   "somebody is failed for a field nobody asked them for, with "
                   "no remedy available." % (cat, "; and ".join(bits)))
    if verbose:
        print("  tier requirements: %d pinned categories cross-checked against "
              "the overlay" % len(pinned))


# -- 4. no tooling is named in a document a client reads ---------------------

LEAKS = [
    (re.compile(r"\bSting\s?Tools\b", re.I), "the product name"),
    (re.compile(r"\bSTING[_ ][A-Z]"), "an internal data file or constant"),
    (re.compile(r"\b(?:ASS|COM|MNT|FLS|PRJ|COMM|HVC|MGS|RAD|CLN|PLM|ELC)_[A-Z0-9_]{3,}"),
     "a shared-parameter name"),
    (re.compile(r"\.(?:json|py)\b", re.I), "a source or configuration file extension"),
    (re.compile(r"\b(?:tools|StingTools|Planscape\.Server)/"), "a repository path"),
    (re.compile(r"[A-Za-z]:\\\\|\\\\[A-Za-z]+\\\\"), "a filesystem path"),
    (re.compile(r"\b[A-Z][a-z]{2,}_[A-Z][A-Za-z]{2,}\b"), "a command tag"),
    (re.compile(r"\bIExternalCommand\b|\bRevit API\b"), "an implementation detail"),
]


def check_no_leakage(root: Path, f: Findings, verbose: bool):
    for name in K.ISSUED:
        text = read_text(root / name)
        hits = []
        for rx, what in LEAKS:
            for m in rx.finditer(text):
                frag = m.group(0)
                hits.append((frag, what))
        # De-duplicate, keep the first of each fragment.
        seen, unique = set(), []
        for frag, what in hits:
            if frag.lower() in seen:
                continue
            seen.add(frag.lower())
            unique.append((frag, what))
        if unique:
            detail = "; ".join('%r (%s)' % (fr, wh) for fr, wh in unique[:6])
            more = "" if len(unique) <= 6 else " and %d more" % (len(unique) - 6)
            f.fail(name,
                   "names the tooling: %s%s. The issued documents are read by "
                   "the client and by every consultant. Express the requirement "
                   "as an outcome or an obligation -- the Appointing Party is "
                   "entitled to require a check, not to specify the instrument."
                   % (detail, more))
        else:
            f.ok()
    if verbose:
        print("  tooling leakage: none in %d issued documents (%s exempt)"
              % (len(K.ISSUED), K.INTERNAL_DOC))


# -- 5. placeholders are counted, not forbidden ------------------------------

def check_placeholders(root: Path, f: Findings, verbose: bool):
    """[FILL] is legitimate at Rev P01; a RISING count is not.

    Failing on any placeholder would be wrong -- the names, dates and
    procurement route genuinely are not known yet, and the pack says so. But
    left uncounted the pack can quietly acquire new holes while appearing to
    converge. So the count is baselined and only an increase fails.
    """
    counts = {}
    for name in K.ISSUED:
        text = read_text(root / name)
        counts[name] = len(re.findall(r"\[FILL", text))

    path = root / BASELINE
    if not path.exists():
        f.fail(BASELINE,
               "does not exist. Create it with the current counts:\n"
               "        %s" % json.dumps(counts, indent=2))
        return counts

    try:
        base = json.loads(path.read_text(encoding="utf-8"))
    except ValueError as exc:
        f.fail(BASELINE, "is not valid JSON: %s" % exc)
        return counts

    for name, n in sorted(counts.items()):
        was = base.get(name)
        if was is None:
            f.fail(BASELINE, "has no baseline for %s (now %d)" % (name, n))
        elif n > was:
            f.fail(name,
                   "has GAINED placeholders: %d, baseline %d. The pack is meant "
                   "to converge toward issue. If the new holes are deliberate, "
                   "raise the baseline in %s in the same commit." % (n, was, BASELINE))
        else:
            f.ok()
            if n < was:
                f.note("%s has closed %d placeholder(s) (%d -> %d). Lower the "
                       "baseline to lock the gain in." % (name, was - n, was, n))
    if verbose:
        print("  placeholders: %s" % ", ".join("%s=%d" % (n.split("_")[1], c)
                                               for n, c in sorted(counts.items())))
    return counts


# -- reading -----------------------------------------------------------------

_TEXT_CACHE: dict = {}


def read_text(path: Path) -> str:
    key = str(path)
    if key not in _TEXT_CACHE:
        if path.suffix == ".xlsx":
            _TEXT_CACHE[key] = K.xlsx_text(path)
        else:
            _TEXT_CACHE[key] = K.docx_text(path)
    return _TEXT_CACHE[key]


def midp_register(midp_path: Path, f: Findings):
    """(data rows, column-name -> index) for the MIDP register sheet."""
    key = "register:" + str(midp_path)
    if key in _TEXT_CACHE:
        return _TEXT_CACHE[key]
    sheets = K.xlsx_sheets(midp_path)
    rows = sheets.get("MIDP")
    if not rows:
        f.fail(MIDP, 'has no "MIDP" sheet')
        _TEXT_CACHE[key] = (None, None)
        return None, None
    header = [c.strip() for c in rows[0]]
    idx = {name: i for i, name in enumerate(header)}
    for req in ("Ref", "Stage", "LOD", "Suitability", "Responsible"):
        if req not in idx:
            f.fail(MIDP, 'register has no %r column (found %s)' % (req, header))
            _TEXT_CACHE[key] = (None, None)
            return None, None
    data = [r for r in rows[1:] if any(c.strip() for c in r)]
    _TEXT_CACHE[key] = (data, idx)
    return data, idx


# -- main --------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo-root", default=None)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    root = Path(args.repo_root) if args.repo_root else Path(__file__).resolve().parent.parent
    f = Findings()

    missing = [n for n in K.ISSUED if not (root / n).exists()]
    if missing:
        print("KUT document gate FAILED.\n")
        for n in missing:
            print("  %s: missing from the repository root" % n)
        return 1

    check_freshness(root, f, args.verbose)

    bep_t = K.docx_tables(root / BEP)
    pb_t = K.docx_tables(root / PLAYBOOK)
    midp_path = root / MIDP

    check_stages(bep_t, pb_t, midp_path, f, args.verbose)
    check_suitability(bep_t, pb_t, midp_path, f, args.verbose)
    check_volumes(bep_t, pb_t, f, args.verbose)
    check_references(root, f, args.verbose)
    check_roles(bep_t, pb_t, midp_path, f, args.verbose)
    check_tiers(root, bep_t, pb_t, f, args.verbose)
    check_no_leakage(root, f, args.verbose)
    counts = check_placeholders(root, f, args.verbose)

    if f.errors:
        print("KUT document gate FAILED -- %d finding(s).\n" % len(f.errors))
        for e in f.errors:
            print("  %s" % e)
        print("\nNever hand-edit a generated document. Edit the generator and")
        print("regenerate; a hand-edit is lost at the next build.")
        return 1

    print("KUT document gate OK.")
    print("  Issued documents gated              : %d" % len(K.ISSUED))
    print("  Assertions passed                   : %d" % f.checked)
    print("  Provenance + content stamps         : both match on all three")
    print("  Placeholders (legitimate at P01)    : %s"
          % ", ".join("%s=%d" % (n.split("_")[1], c) for n, c in sorted(counts.items())))
    for n in f.notes:
        print("\n  Note: %s" % n)
    print("\n  This gate proves the pack is INTERNALLY CONSISTENT and matches the")
    print("  configuration the LOD gate enforces. It proves nothing about whether")
    print("  those requirements are the right ones, and nothing about a real Revit")
    print("  model. A green run is not a validated pack.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
