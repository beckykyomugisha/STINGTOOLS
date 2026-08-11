#!/usr/bin/env python3
"""
H-5 — schema validation for the shipped StingTools/Data files.

WHY THIS EXISTS
---------------
The Data/ tree is the real behaviour surface: ~232 DeserializeObject sites read
it, and both MissingMemberHandling settings in the repo are Ignore. So a
mistyped field name in a data file is not a compile error, not a JSON syntax
error, and not a runtime exception — Newtonsoft leaves the member at its default
and the feature silently does nothing. CI's only gate was
`json.load(open(f))`, which proves a file is well-formed JSON and nothing else.

That is how G-2 sat undetected across 13,212 rows.

This validator fails the build on an UNKNOWN KEY, which is the specific thing
Newtonsoft will not tell you about.

HOW THE SCHEMAS ARE KEPT HONEST
-------------------------------
A hand-maintained key list is just a second thing to drift. So for files bound
to a Newtonsoft POCO, the allowed key set is DERIVED FROM THE C# SOURCE at
validation time by reading the auto-properties off the named class. Add a
property to the POCO and the validator accepts it immediately; delete one and
every data file still using it fails. The schema cannot rot because there is no
schema — there is the POCO.

Files read through JObject/JArray with explicit field reads (no POCO) carry a
hand-declared key list, marked as such below, because there is no type to
derive from.

CONVENTIONS HONOURED
--------------------
  * Keys beginning with "_" are comments (`_note`, `_comment`, `_description`).
    Newtonsoft ignores them; so do we.
  * Newtonsoft matches property names case-insensitively, so key matching here
    is case-insensitive too.
  * CSV files may carry leading `#` comment lines before the header.

USAGE
  python3 tools/validate_data_schemas.py             # validate, exit 1 on error
  python3 tools/validate_data_schemas.py --list      # show resolved key sets
"""

import csv
import io
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(REPO, "StingTools", "Data")

errors = []
warnings = []
checked_files = 0


def err(msg):
    errors.append(msg)


def warn(msg):
    # De-duplicated: a missing POCO would otherwise warn once per array
    # element and bury the real findings under 90 identical lines.
    if msg not in warnings:
        warnings.append(msg)


# ─────────────────────────────────────────────────────────────────────────────
#  C# POCO property extraction
# ─────────────────────────────────────────────────────────────────────────────

# Auto-properties only: `public string Foo { get; set; }`. Deliberately narrow —
# a property with a body is not a Newtonsoft binding target we care about here,
# and a loose regex that mis-parses would produce false CI failures, which is
# worse than a gap.
_PROP = re.compile(
    r"public\s+(?:virtual\s+|override\s+|new\s+)?"
    r"[\w<>,\[\]\?\.\s]+?\s+(\w+)\s*\{\s*get\s*;\s*set\s*;\s*\}"
)
_CLASS = re.compile(r"\b(?:class|record)\s+(\w+)")

_source_cache = {}


def _read_source(rel_path):
    if rel_path in _source_cache:
        return _source_cache[rel_path]
    full = os.path.join(REPO, rel_path)
    if not os.path.isfile(full):
        _source_cache[rel_path] = None
        return None
    with io.open(full, "r", encoding="utf-8-sig", errors="replace") as fh:
        _source_cache[rel_path] = fh.read()
    return _source_cache[rel_path]


def poco_properties(rel_path, class_name):
    """
    Auto-property names declared on `class_name` in `rel_path`.

    Scans from the class declaration to its matching closing brace, so sibling
    classes in the same file (DrawingType.cs holds ~20) do not bleed into each
    other. Returns None when the class cannot be located — the caller treats
    that as a warning, not a failure, because a missing POCO means the check
    could not run, not that the data is wrong.
    """
    src = _read_source(rel_path)
    if src is None:
        return None

    for m in _CLASS.finditer(src):
        if m.group(1) != class_name:
            continue
        brace = src.find("{", m.end())
        if brace < 0:
            continue
        depth, i, n = 0, brace, len(src)
        while i < n:
            ch = src[i]
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        body = src[brace:i]
        # Strip nested class bodies so an inner type's properties are not
        # attributed to the outer one.
        return {p.group(1) for p in _PROP.finditer(_strip_nested_classes(body))}
    return None


def _strip_nested_classes(body):
    out, i, n = [], 0, len(body)
    while i < n:
        m = _CLASS.search(body, i)
        if not m:
            out.append(body[i:])
            break
        out.append(body[i:m.start()])
        brace = body.find("{", m.end())
        if brace < 0:
            break
        depth, j = 0, brace
        while j < n:
            if body[j] == "{":
                depth += 1
            elif body[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        i = j + 1
    return "".join(out)


# ─────────────────────────────────────────────────────────────────────────────
#  Schema declarations
# ─────────────────────────────────────────────────────────────────────────────
#
# poco : (relative .cs path, class name)  -> allowed keys derived from source
# keys : explicit set                     -> for JObject readers with no POCO
# req  : keys that must be present
# kind : "object" | "array-of-object"
# children : { json-key : schema } for nested objects / arrays

DRAWING_CS = "StingTools/Core/Drawing/DrawingType.cs"
NRM2_CS = "StingTools/BOQ/MeasurementStandard/MeasurementRules.cs"

JSON_SCHEMAS = {
    "STING_NRM2_MEASUREMENT_RULES.json": {
        "kind": "object",
        "poco": (NRM2_CS, "MeasurementRuleLibrary"),
        "req": ["rules"],
        "children": {
            "defaults": {"kind": "object", "poco": (NRM2_CS, "MeasurementDefaults")},
            "rules": {
                "kind": "array-of-object",
                "poco": (NRM2_CS, "MeasurementRule"),
                "req": ["id", "matchCategory", "unit", "measure"],
            },
        },
    },

    "STING_DRAWING_TYPES.json": {
        "kind": "object",
        "poco": (DRAWING_CS, "DrawingTypeLibrary"),
        "req": ["drawingTypes"],
        "children": {
            "drawingTypes": {
                "kind": "array-of-object",
                "poco": (DRAWING_CS, "DrawingType"),
                "req": ["id", "name"],
                "children": {
                    "crop": {"kind": "object", "poco": (DRAWING_CS, "DrawingCropStrategy")},
                    "sectionMarker": {"kind": "object", "poco": (DRAWING_CS, "SectionMarkerSpec")},
                    "print": {"kind": "object", "poco": (DRAWING_CS, "PrintOverride")},
                    "isoNaming": {"kind": "object", "poco": (DRAWING_CS, "IsoNaming")},
                    "slots": {"kind": "array-of-object", "poco": (DRAWING_CS, "DrawingSlot")},
                },
            },
            "routing": {
                "kind": "array-of-object",
                "poco": (DRAWING_CS, "DrawingRoutingRule"),
                "req": ["drawingTypeId"],
            },
        },
    },

    # No POCO — Temp/BOQTemplateLibrary.LoadBuiltin parses this with JArray and
    # reads fields explicitly in FromJson, so the contract is the reader, not a
    # type. Hand-declared, and that is called out rather than hidden.
    "BOQ_DESCRIPTIONS.json": {
        "kind": "array-of-object",
        "keys": ["category", "nrm2_section", "paragraph", "placeholders"],
        "req": ["category", "paragraph"],
        "types": {
            "category": "str",
            "nrm2_section": "str",
            "paragraph": "str",
            "placeholders": "list-of-str",
        },
    },
}

# CSV contracts. `header` is exact and ordered — a renamed or reordered column
# is precisely the silent break this gate is for. `types` are checked per cell;
# blank is allowed unless the column is in `req`.
CSV_SCHEMAS = {
    "cost_rates_5d.csv": {
        "header": ["Category", "MAT_CODE", "MAT_DISCIPLINE",
                   "Unit_Rate_USD", "Unit_Rate_UGX", "Unit", "Description"],
        "types": {"Unit_Rate_USD": "num", "Unit_Rate_UGX": "num"},
        "req": ["Category", "Unit_Rate_USD", "Unit_Rate_UGX", "Unit"],
    },
    "STING_DEFAULT_COST_RATES.csv": {
        "header": ["Category", "RatePerUnit_USD", "Unit", "Description"],
        "types": {"RatePerUnit_USD": "num"},
        "req": ["Category", "RatePerUnit_USD", "Unit"],
    },
}


# ─────────────────────────────────────────────────────────────────────────────
#  Validation
# ─────────────────────────────────────────────────────────────────────────────

def allowed_keys(schema, where):
    """Resolve a schema's allowed key set, from the POCO or the explicit list."""
    if "poco" in schema:
        rel, cls = schema["poco"]
        props = poco_properties(rel, cls)
        if props is None:
            warn(f"{where}: could not read class {cls} from {rel} — "
                 "unknown-key checking SKIPPED for this node.")
            return None
        if not props:
            warn(f"{where}: class {cls} in {rel} declares no auto-properties — "
                 "unknown-key checking SKIPPED for this node.")
            return None
        return {p.lower() for p in props}
    if "keys" in schema:
        return {k.lower() for k in schema["keys"]}
    return None


def type_ok(value, expected):
    if expected == "str":
        return isinstance(value, str)
    if expected == "num":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if expected == "bool":
        return isinstance(value, bool)
    if expected == "list-of-str":
        return isinstance(value, list) and all(isinstance(x, str) for x in value)
    return True


def validate_object(obj, schema, where):
    if not isinstance(obj, dict):
        err(f"{where}: expected an object, found {type(obj).__name__}")
        return

    allowed = allowed_keys(schema, where)
    types = schema.get("types", {})
    children = schema.get("children", {})

    for key, value in obj.items():
        if key.startswith("_"):
            continue                       # documented comment-key convention
        if allowed is not None and key.lower() not in allowed:
            err(f"{where}: UNKNOWN KEY '{key}' — nothing reads it, so its value "
                f"is silently discarded at load. Fix the spelling or add the "
                f"member to the type.")
            continue
        if key in types and value is not None and not type_ok(value, types[key]):
            err(f"{where}.{key}: expected {types[key]}, found {type(value).__name__}")
        if key in children and value is not None:
            validate_node(value, children[key], f"{where}.{key}")

    lower = {k.lower() for k in obj}
    for r in schema.get("req", []):
        if r.lower() not in lower:
            err(f"{where}: missing required key '{r}'")


def validate_node(node, schema, where):
    kind = schema.get("kind", "object")
    if kind == "array-of-object":
        if not isinstance(node, list):
            err(f"{where}: expected an array, found {type(node).__name__}")
            return
        for i, item in enumerate(node):
            validate_object(item, schema, f"{where}[{i}]")
    else:
        validate_object(node, schema, where)


def validate_json(name, schema):
    global checked_files
    path = os.path.join(DATA, name)
    if not os.path.isfile(path):
        err(f"{name}: MISSING from StingTools/Data — a schema is declared for it.")
        return
    try:
        with io.open(path, "r", encoding="utf-8-sig") as fh:
            doc = json.load(fh)
    except Exception as ex:
        err(f"{name}: not valid JSON — {ex}")
        return
    checked_files += 1
    validate_node(doc, schema, name)


def validate_csv(name, schema):
    global checked_files
    path = os.path.join(DATA, name)
    if not os.path.isfile(path):
        err(f"{name}: MISSING from StingTools/Data — a schema is declared for it.")
        return

    with io.open(path, "r", encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    checked_files += 1

    # Skip leading comment/blank lines; the first real row is the header.
    hdr_idx = None
    for i, row in enumerate(rows):
        if not row or not "".join(row).strip():
            continue
        if row[0].lstrip().startswith("#"):
            continue
        hdr_idx = i
        break
    if hdr_idx is None:
        err(f"{name}: no header row found.")
        return

    header = [c.strip() for c in rows[hdr_idx]]
    expected = schema["header"]
    if header != expected:
        err(f"{name}: header mismatch.\n"
            f"      expected: {expected}\n"
            f"      found   : {header}")
        return                              # per-cell checks would be meaningless

    types = schema.get("types", {})
    req = set(schema.get("req", []))
    idx = {c: i for i, c in enumerate(header)}

    for ln, row in enumerate(rows[hdr_idx + 1:], start=hdr_idx + 2):
        if not row or not "".join(row).strip():
            continue
        if row[0].lstrip().startswith("#"):
            continue
        if len(row) != len(expected):
            err(f"{name}:{ln}: expected {len(expected)} columns, found {len(row)}")
            continue
        for col, i in idx.items():
            cell = (row[i] or "").strip()
            if not cell:
                if col in req:
                    err(f"{name}:{ln}: '{col}' is required but empty")
                continue
            if types.get(col) == "num":
                try:
                    float(cell.replace(",", ""))
                except ValueError:
                    err(f"{name}:{ln}: '{col}' = '{cell}' is not a number")


def self_test():
    """
    Prove the gate actually fires.

    A validator that reports OK because its own matching is broken is the exact
    failure mode this file exists to close — an empty finding list standing in
    for an error. So CI runs this first: each case mutates a real shipped file
    in a temp copy and asserts the validator rejects it. If any case passes
    validation, the gate is not working and the build fails on that alone.
    """
    global DATA, errors, warnings, checked_files
    import shutil
    import tempfile

    real_data = DATA
    cases = []

    def case(name, label, mutate):
        cases.append((name, label, mutate))

    def add_unknown_json(doc, path):
        node = doc
        for p in path:
            node = node[p]
        node["thisKeyDoesNotExist"] = "x"
        return doc

    case("STING_NRM2_MEASUREMENT_RULES.json", "unknown key on a rule",
         lambda d: add_unknown_json(d, ["rules", 0]))
    case("STING_NRM2_MEASUREMENT_RULES.json", "unknown key at the root",
         lambda d: add_unknown_json(d, []))
    case("STING_DRAWING_TYPES.json", "unknown key on a drawing type",
         lambda d: add_unknown_json(d, ["drawingTypes", 0]))
    case("STING_DRAWING_TYPES.json", "unknown key on a nested crop block",
         lambda d: add_unknown_json(d, ["drawingTypes", 0, "crop"]))
    case("BOQ_DESCRIPTIONS.json", "unknown key on a description",
         lambda d: add_unknown_json(d, [0]))
    case("STING_NRM2_MEASUREMENT_RULES.json", "missing required key",
         lambda d: (d["rules"][0].pop("unit"), d)[1])

    def csv_rename_header(text):
        lines = text.split("\n")
        for i, ln in enumerate(lines):
            if ln.strip() and not ln.lstrip().startswith("#"):
                lines[i] = ln.replace("Unit", "Units", 1)
                break
        return "\n".join(lines)

    def csv_break_number(text):
        lines = text.split("\n")
        seen_header = False
        for i, ln in enumerate(lines):
            if not ln.strip() or ln.lstrip().startswith("#"):
                continue
            if not seen_header:
                seen_header = True
                continue
            parts = ln.split(",")
            if len(parts) > 1:
                parts[1] = "not-a-number"
                lines[i] = ",".join(parts)
            break
        return "\n".join(lines)

    csv_cases = [
        ("cost_rates_5d.csv", "renamed column", csv_rename_header),
        ("STING_DEFAULT_COST_RATES.csv", "non-numeric rate", csv_break_number),
    ]

    failures = []
    tmp = tempfile.mkdtemp(prefix="sting_schema_selftest_")
    try:
        for name, label, mutate in cases:
            errors, warnings, checked_files = [], [], 0
            with io.open(os.path.join(real_data, name), encoding="utf-8-sig") as fh:
                doc = json.load(fh)
            doc = mutate(doc)
            DATA = tmp
            with io.open(os.path.join(tmp, name), "w", encoding="utf-8") as fh:
                json.dump(doc, fh)
            validate_json(name, JSON_SCHEMAS[name])
            if not errors:
                failures.append(f"{name}: '{label}' was NOT caught")

        for name, label, mutate in csv_cases:
            errors, warnings, checked_files = [], [], 0
            with io.open(os.path.join(real_data, name), encoding="utf-8-sig") as fh:
                text = fh.read()
            DATA = tmp
            with io.open(os.path.join(tmp, name), "w", encoding="utf-8", newline="") as fh:
                fh.write(mutate(text))
            validate_csv(name, CSV_SCHEMAS[name])
            if not errors:
                failures.append(f"{name}: '{label}' was NOT caught")
    finally:
        DATA = real_data
        errors, warnings, checked_files = [], [], 0
        shutil.rmtree(tmp, ignore_errors=True)

    total = len(cases) + len(csv_cases)
    if failures:
        print(f"SELF-TEST FAILED - the gate does not fire on {len(failures)} of {total} case(s):")
        for f in failures:
            print(f"  [FAIL] {f}")
        print("\nA gate that cannot fail is worse than no gate: it reports green")
        print("over broken data forever.")
        return 1
    print(f"OK - self-test: all {total} deliberate defects were caught")
    return 0


def main():
    if "--self-test" in sys.argv:
        return self_test()

    if "--list" in sys.argv:
        for name, schema in JSON_SCHEMAS.items():
            keys = allowed_keys(schema, name)
            print(f"{name}: {sorted(keys) if keys else '(unchecked)'}")
        return 0

    for name, schema in sorted(JSON_SCHEMAS.items()):
        validate_json(name, schema)
    for name, schema in sorted(CSV_SCHEMAS.items()):
        validate_csv(name, schema)

    for w in warnings:
        print(f"::warning::{w}")

    if errors:
        print(f"\n{len(errors)} schema error(s) across {checked_files} file(s):\n")
        for e in errors:
            print(f"  [FAIL] {e}")
        print("\nAn unknown key is not cosmetic: Newtonsoft's MissingMemberHandling")
        print("is Ignore everywhere in this repo, so the value is dropped at load")
        print("and the feature reading it does nothing, with no error anywhere.")
        return 1

    print(f"OK - {checked_files} data file(s) validated against their schemas, "
          f"{len(warnings)} warning(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
