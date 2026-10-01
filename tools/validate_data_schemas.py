#!/usr/bin/env python3
"""
H-5 / DSCH — schema validation for every shipped data file.

WHY THIS EXISTS
---------------
The Data/ tree is the real behaviour surface: ~232 DeserializeObject sites read
it, and both MissingMemberHandling settings in the repo are Ignore. So a
mistyped field name in a data file is not a compile error, not a JSON syntax
error, and not a runtime exception — Newtonsoft leaves the member at its default
and the feature silently does nothing. CI's only gate was
`json.load(open(f))`, which proves a file is well-formed JSON and nothing else.

That is how G-2 sat undetected across 13,212 rows. And for CSV the same class
of defect is a column inserted, renamed or split by an unquoted comma: every
reader that indexes by position then reads its neighbour's value. D6 inserted
PROD into cost_rates_5d.csv; this validator noticed, but ran in no workflow,
so the drift — and the 5D Cost Trace it silently broke — sat on main unseen.

ONE SOURCE OF TRUTH
-------------------
Every schema lives in tools/data_schemas.json. This file holds no schema of its
own: it reads that registry, and so do the C# tests that hold a reader to the
columns it declares (StingTools.Boq.Tests/CostRateCsvTests). Change a data
file and its schema in the same commit — see docs/DATA_SCHEMAS.md.

For JSON bound to a Newtonsoft POCO the allowed key set is still DERIVED FROM
THE C# SOURCE at validation time, by reading the auto-properties off the named
class. Add a property to the POCO and the validator accepts it immediately;
delete one and every data file still using it fails. The registry only names
the class.

COVERAGE IS ENFORCED
--------------------
Every file under the registry's roots must be registered — with a full schema,
as structural-only (format checks, with the reason it has no deeper schema), or
as non-data (binary / documentation). A new file nobody registered FAILS, with
the command that prints a stub for it. A registered file that no longer exists
fails too, so the registry cannot outlive its data.

WHAT EVERY REGISTERED TEXT FILE GETS
------------------------------------
  * UTF-8, no mixed CRLF/LF line endings, not empty
  * JSON: strict parse, and NO DUPLICATE KEYS — Python's json and Newtonsoft
    both keep the last duplicate silently, so the first value is lost
  * CSV table: header matches the declared columns (order, case, whitespace),
    no duplicate header names, every row has exactly the header's field count,
    typed / required / enum / unique cells
  * CSV sections (multi-table files): encoding and line endings only

CONVENTIONS HONOURED
--------------------
  * JSON keys beginning with "_" are comments; Newtonsoft ignores them, so do we.
  * Newtonsoft matches property names case-insensitively, so key matching here
    is case-insensitive too.
  * CSV files may carry leading `#` comment lines before the header, and `#`
    comment rows in the body.

USAGE
  python3 tools/validate_data_schemas.py             # validate, exit 1 on error
  python3 tools/validate_data_schemas.py --self-test # prove every check fires
  python3 tools/validate_data_schemas.py --scaffold PATH   # print a registry stub
  python3 tools/validate_data_schemas.py --describe PATH   # print a file's schema
  python3 tools/validate_data_schemas.py --list      # show resolved JSON key sets
"""

import csv
import fnmatch
import io
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REGISTRY_PATH = os.path.join(REPO, "tools", "data_schemas.json")
SUPPORTED_REGISTRY_VERSION = 1

errors = []
warnings = []
checked_files = 0

# Where a relative registry path is resolved. The self-test points this at a
# temp copy so it can mutate files without touching the tree.
ROOT = REPO


def err(msg):
    errors.append(msg)


def warn(msg):
    # De-duplicated: a missing POCO would otherwise warn once per array
    # element and bury the real findings under 90 identical lines.
    if msg not in warnings:
        warnings.append(msg)


def load_registry(path=REGISTRY_PATH):
    with io.open(path, "r", encoding="utf-8") as fh:
        reg = json.load(fh, object_pairs_hook=_no_dupes(path))
    v = reg.get("registryVersion")
    if v != SUPPORTED_REGISTRY_VERSION:
        raise SystemExit(f"{path}: registryVersion {v!r} is not supported by this "
                         f"validator (expects {SUPPORTED_REGISTRY_VERSION}).")
    return reg


# ─────────────────────────────────────────────────────────────────────────────
#  C# POCO property extraction
# ─────────────────────────────────────────────────────────────────────────────

# What Newtonsoft can bind on a class: public instance properties (auto or with a
# body - a body with a setter still binds) and public instance fields. Not static,
# const, or expression-bodied (`Name => ...`, no setter), and not [JsonIgnore].
# A [JsonProperty("x")] attribute renames the JSON key to "x".
#
# Deliberately conservative in the direction that matters: a member this misses
# turns into a FALSE "unknown key" failure, which the self-test guards against; a
# member it wrongly accepts only weakens the check for that one key.
_MEMBER = re.compile(
    r"\bpublic\s+(?!static\b|const\b|class\b|enum\b|struct\b|record\b|interface\b"
    r"|event\b|delegate\b|abstract\b|partial\b|sealed\b)"
    r"(?:(?:virtual|override|new|readonly|required|unsafe|volatile)\s+)*"
    r"[\w<>,\[\]\?\.\s:]+?\s+(\w+)\s*(\{|=(?!>)|;)"
)
_CLASS = re.compile(r"\b(?:class|record|struct)\s+(\w+)")
_JSON_PROP = re.compile(
    r"JsonProperty\s*\(\s*(?:PropertyName\s*=\s*)?@?\"((?:[^\"\\]|\\.)*)\"")

_source_cache = {}


def _read_source(rel_path):
    """(raw, code_only) for a source file. code_only has comments and string
    literals blanked to spaces, so offsets in the two are identical."""
    if rel_path in _source_cache:
        return _source_cache[rel_path]
    full = os.path.join(REPO, rel_path)
    if not os.path.isfile(full):
        _source_cache[rel_path] = None
        return None
    with io.open(full, "r", encoding="utf-8-sig", errors="replace") as fh:
        raw = fh.read()
    _source_cache[rel_path] = (raw, _code_only(raw))
    return _source_cache[rel_path]


_CODE_NOISE = re.compile(
    r'//[^\n]*'                        # line comment (incl. /// doc comments)
    r'|/\*.*?\*/'                      # block comment
    r'|@"(?:[^"]|"")*"'                # verbatim string
    r'|"(?:\\.|[^"\\\n])*"'            # regular string
    r"|'(?:\\.|[^'\\\n])'",            # char literal
    re.S)


def _code_only(src):
    """
    Blank out comments and string literals, keeping the text the same length.

    The class scanner counts braces and looks for `class Foo`. A doc comment saying
    "the wet zone class this fixture is rated for" matched as `class this`, and the
    nested-class stripper then deleted the properties after it - so WetZoneClass,
    BuildingType and WetZoneExclusion read as UNKNOWN on 81 placement rules that
    bind them correctly. A "{" inside a string would miscount the braces the same way.
    """
    return _CODE_NOISE.sub(lambda m: " " * len(m.group(0)), src)


def _match_brace(text, brace):
    depth, i, n = 0, brace, len(text)
    while i < n:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n


_poco_memo = {}


def poco_properties(rel_paths, class_name, _depth=0):
    """Memoised: allowed_keys asks once per array ELEMENT, and a scan per element
    took the run from seconds to two minutes."""
    key = (tuple([rel_paths]) if isinstance(rel_paths, str) else tuple(rel_paths), class_name)
    if key not in _poco_memo:
        _poco_memo[key] = _poco_properties(rel_paths, class_name, _depth)
    r = _poco_memo[key]
    return set(r) if r is not None else None


def _poco_properties(rel_paths, class_name, _depth=0):
    """
    JSON key names Newtonsoft binds on `class_name`, found in `rel_paths` (one
    path or a list - a partial class is the union of its parts).

    Scans from each class declaration to its matching closing brace, so sibling
    classes in the same file (DrawingType.cs holds ~20) do not bleed into each
    other, and blanks nested types so their members are not attributed to the
    outer one. Returns None when the class cannot be located - the caller treats
    that as a warning, not a failure, because a missing POCO means the check
    could not run, not that the data is wrong.
    """
    paths = [rel_paths] if isinstance(rel_paths, str) else list(rel_paths)
    found, names = False, set()
    for rel in paths:
        src = _read_source(rel)
        if src is None:
            continue
        raw, code = src
        for m in _CLASS.finditer(code):
            if m.group(1) != class_name:
                continue
            brace = code.find("{", m.end())
            semi = code.find(";", m.end())
            if brace < 0 or (0 <= semi < brace):
                continue                     # a primary-ctor record or a declaration only
            end = _match_brace(code, brace)
            found = True
            # Inherited members bind too (LccOptionConfig : LccOption). The first
            # base type is followed wherever it is declared; an interface or a
            # framework type is simply not found and contributes nothing.
            decl = code[m.end():brace]
            if ":" in decl and _depth < 4:
                base = re.match(r"\s*([A-Za-z_]\w*)", decl.split(":", 1)[1].split("where")[0])
                if base and base.group(1) != class_name:
                    inherited = poco_properties(_files_declaring(base.group(1)), base.group(1),
                                                _depth + 1)
                    if inherited:
                        names |= inherited
            body = _blank_nested_types(code[brace + 1:end])
            raw_body = raw[brace + 1:end]
            prev = 0
            for mm in _MEMBER.finditer(body):
                # Attributes sit between the previous member's end and this one.
                lead_start = max(body.rfind(";", 0, mm.start()), body.rfind("}", 0, mm.start()),
                                 body.rfind("{", 0, mm.start()), prev)
                lead_code = body[lead_start:mm.start()]
                lead_raw = raw_body[lead_start:mm.start()]
                prev = mm.end()
                if re.search(r"\bJsonIgnore\b", lead_code):
                    continue
                jp = _JSON_PROP.search(lead_raw) if "JsonProperty" in lead_code else None
                names.add(jp.group(1) if jp else mm.group(1))
            # Newtonsoft also binds a NON-public member that carries [JsonProperty] -
            # the legacy-alias idiom: `[JsonProperty("instance")] private bool
            # LegacyInstance { set => ... }`. Take every attributed name in the body.
            for am in re.finditer(r"\[\s*JsonProperty\b", body):
                jp = _JSON_PROP.match(raw_body, am.start() + raw_body[am.start():].find("JsonProperty"))
                if jp:
                    names.add(jp.group(1))
    return names if found else None


_decl_index = None


def _files_declaring(class_name):
    """Every plugin source file that declares `class_name` (built once, lazily)."""
    global _decl_index
    if _decl_index is None:
        _decl_index = {}
        for dp, dns, fns in os.walk(os.path.join(REPO, "StingTools")):
            dns[:] = [d for d in dns if d not in ("obj", "bin")]
            for fn in fns:
                if not fn.endswith(".cs"):
                    continue
                rel = os.path.relpath(os.path.join(dp, fn), REPO).replace(os.sep, "/")
                src = _read_source(rel)
                if src:
                    for m in _CLASS.finditer(src[1]):
                        _decl_index.setdefault(m.group(1), []).append(rel)
    return _decl_index.get(class_name, [])


def _blank_nested_types(body):
    """Replace nested type bodies with spaces (offsets kept)."""
    out = list(body)
    i = 0
    while True:
        m = _CLASS.search(body, i)
        if not m:
            break
        brace = body.find("{", m.end())
        if brace < 0:
            break
        end = _match_brace(body, brace)
        for k in range(m.start(), min(end + 1, len(body))):
            out[k] = " "
        i = end + 1
    return "".join(out)



# ─────────────────────────────────────────────────────────────────────────────
#  Text-level checks shared by every registered text file
# ─────────────────────────────────────────────────────────────────────────────

def read_text(rel, where=None):
    """UTF-8 text of a registered file, or None after recording why not."""
    where = where or rel
    path = os.path.join(ROOT, rel)
    try:
        with io.open(path, "rb") as fh:
            raw = fh.read()
    except OSError as ex:
        err(f"{where}: cannot be read — {ex}")
        return None
    if not raw.strip():
        err(f"{where}: file is EMPTY. An empty data file loads as 'no rows' "
            f"everywhere it is read, which looks exactly like a working file "
            f"with nothing in it. Delete it, or give it content.")
        return None
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as ex:
        err(f"{where}: not UTF-8 (byte {ex.start}: {raw[ex.start:ex.start + 4]!r}). "
            f"The plugin reads data files as UTF-8, so a Windows-1252 byte becomes "
            f"U+FFFD and any key containing it stops matching. Re-save as UTF-8.")
        return None
    crlf = raw.count(b"\r\n")
    lf = raw.count(b"\n") - crlf
    if crlf and lf:
        want_cr = crlf < lf                 # report the first line of the minority kind
        lines = raw.split(b"\n")[:-1]
        first = next(i for i, ln in enumerate(lines, start=1)
                     if ln.endswith(b"\r") == want_cr)
        err(f"{where}:{first}: MIXED line endings ({crlf} CRLF, {lf} LF; line {first} "
            f"is the first {'CRLF' if want_cr else 'LF'} one). Pick one; a "
            f"reader that splits on '\\n' keeps a trailing '\\r' on the CRLF rows "
            f"only, so the same key matches on some rows and not others.")
    return text


def _no_dupes(where):
    def hook(pairs):
        seen = {}
        for k, v in pairs:
            if k in seen:
                ident = next((f"{n}={pairs_dict(pairs)[n]!r}" for n in
                              ("id", "name", "commandTag", "category", "key", "code")
                              if n in pairs_dict(pairs) and n != k), "an unnamed object")
                err(f"{where}: DUPLICATE KEY '{k}' in the object with {ident} - "
                    f"Newtonsoft and Python both keep the LAST value silently, so "
                    f"{seen[k]!r:.60} is discarded at load. Merge or rename one of them.")
            seen[k] = v
        return seen
    return hook


def pairs_dict(pairs):
    d = {}
    for k, v in pairs:
        d.setdefault(k, v)
    return d


# ─────────────────────────────────────────────────────────────────────────────
#  JSON
# ─────────────────────────────────────────────────────────────────────────────

def allowed_keys(schema, where):
    """Resolve a schema's allowed key set: the POCO's (or the explicit list) plus any
    declared docKeys - keys the type does not bind but that are kept on purpose,
    each with its reason in the registry (documentation, or a second reader)."""
    base = _allowed_keys(schema, where)
    if base is None:
        return None
    return base | {k.lower() for k in schema.get("docKeys", {})}


def _allowed_keys(schema, where):
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
    if expected == "int":
        return isinstance(value, int) and not isinstance(value, bool)
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
    children = {k.lower(): v for k, v in schema.get("children", {}).items()}

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
        ref = schema.get("valueRefersTo", {}).get(key)
        if ref and isinstance(value, str) and value:
            names = _ref_set(ref, where)
            if names is not None and value not in names and value not in ref.get("alsoAllowed", []):
                err(f"{where}.{key}: '{value}' does not exist in {ref['file']} ({ref['kind']}). "
                    f"A mapping to a name that resolves to nothing writes nothing, silently.")
        if key.lower() in children and value is not None:
            validate_node(value, children[key.lower()], f"{where}.{key}")

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
    elif kind == "dict-of-object":
        # Dictionary<string, T>: the keys are data, each value is a T.
        if not isinstance(node, dict):
            err(f"{where}: expected an object keyed by name, found {type(node).__name__}")
            return
        for k, item in node.items():
            if not k.startswith("_"):
                validate_object(item, schema, f"{where}[{k!r}]")
    else:
        validate_object(node, schema, where)


def validate_json(rel, schema):
    global checked_files
    text = read_text(rel)
    if text is None:
        return
    try:
        doc = json.loads(text, object_pairs_hook=_no_dupes(rel))
    except ValueError as ex:
        err(f"{rel}: not valid JSON — {ex}")
        return
    checked_files += 1
    if schema.get("kind") or schema.get("poco") or schema.get("keys"):
        validate_node(doc, schema, os.path.basename(rel))


# ─────────────────────────────────────────────────────────────────────────────
#  CSV
# ─────────────────────────────────────────────────────────────────────────────

def _is_comment_or_blank(row):
    return (not row or not "".join(row).strip()
            or row[0].lstrip("﻿").lstrip().startswith("#"))


def _csv_cell_ok(cell, col):
    t = col.get("type", "str")
    if t in ("num", "int"):
        try:
            v = float(cell.replace(",", "")) if t == "num" else int(cell)
        except ValueError:
            return f"is not {'a number' if t == 'num' else 'an integer'}"
        if "min" in col and v < col["min"]:
            return f"is below the minimum {col['min']}"
    if "enum" in col and cell not in col["enum"]:
        return f"is not one of {col['enum']}"
    if "pattern" in col and not re.fullmatch(col["pattern"], cell):
        return f"does not match /{col['pattern']}/"
    return None


def validate_csv_table(rel, schema):
    global checked_files
    text = read_text(rel)
    if text is None:
        return
    checked_files += 1
    rows = list(csv.reader(io.StringIO(text)))

    # Skip leading comment/blank lines; the first real row is the header.
    hdr_idx = next((i for i, r in enumerate(rows) if not _is_comment_or_blank(r)), None)
    if hdr_idx is None:
        err(f"{rel}: no header row found.")
        return

    raw_header = rows[hdr_idx]
    header = [c.lstrip("﻿") for c in raw_header]
    hdr_line = hdr_idx + 1
    for i, c in enumerate(header):
        if c != c.strip():
            err(f"{rel}:{hdr_line}: header column {i + 1} {c!r} has surrounding "
                f"whitespace — a reader that looks it up by name will not find it.")
    seen = {}
    for c in header:
        k = c.strip().lower()
        if k in seen:
            err(f"{rel}:{hdr_line}: header column '{c.strip()}' appears twice — a "
                f"name lookup returns only one of them.")
        seen[k] = True

    cols = schema["columns"]
    declared = [c["name"] for c in cols]
    present = [c.strip() for c in header]
    # The header must be the declared columns, in the declared order, with only
    # columns marked optional allowed to be absent. A column that is not declared
    # is an error even when it is "extra": silent extras are how a reader ends up
    # indexing the wrong field.
    expected = [n for n in declared if n in present or not _col(cols, n).get("optional")]
    if present != expected:
        undeclared = [c for c in present if c not in declared]
        missing = [c for c in expected if c not in present]
        detail = []
        if undeclared:
            detail.append(f"undeclared column(s) {undeclared} — declare them in "
                          f"tools/data_schemas.json (and bump schemaVersion)")
        if missing:
            detail.append(f"missing required column(s) {missing}")
        if not detail:
            detail.append("same columns, different order")
        err(f"{rel}:{hdr_line}: header does not match schema v{schema.get('schemaVersion', 1)} — "
            + "; ".join(detail) + f".\n      expected: {expected}\n      found   : {present}")
        return                              # per-cell checks would be meaningless

    idx = {c: i for i, c in enumerate(present)}
    unique = [tuple(u) for u in schema.get("unique", [])]
    seen_keys = {u: {} for u in unique}
    n = len(present)
    # minFields: a DECLARED allowance for rows that stop early because their reader
    # treats the trailing columns as optional (it says why in "minFieldsReason").
    # Never an allowance for MORE fields than the header: that is always a split.
    min_fields = schema.get("minFields", n)
    refs = {c["name"]: _ref_set(c["refersTo"], rel) for c in cols if "refersTo" in c}

    for ln, row in enumerate(rows[hdr_idx + 1:], start=hdr_idx + 2):
        if _is_comment_or_blank(row):
            continue
        if len(row) > n or len(row) < min_fields:
            cause = ("an unquoted comma inside a field split it in two - quote the field"
                     if len(row) > n else
                     f"trailing fields are missing - this schema requires at least "
                     f"{min_fields} fields per row")
            err(f"{rel}:{ln}: row has {len(row)} fields, header has {n}: {cause}. "
                f"Every column after the split is read from its neighbour.")
            continue
        row = row + [""] * (n - len(row))
        for name, i in idx.items():
            col = _col(cols, name)
            cell = row[i].strip()
            if name in refs and cell and refs[name] is not None:
                ref = col["refersTo"]
                parts = [x.strip() for x in cell.split(ref["split"])] if ref.get("split") else [cell]
                for part in parts:
                    if part and part not in refs[name] and part not in ref.get("alsoAllowed", []):
                        err(f"{rel}:{ln}: column '{name}' names '{part}', which does not "
                            f"exist in {ref['file']} ({ref['kind']}). A name that resolves to "
                            f"nothing is skipped at runtime without an error.")
            if not cell:
                if col.get("required"):
                    err(f"{rel}:{ln}: column '{name}' is required but empty")
                continue
            problem = _csv_cell_ok(cell, col)
            if problem:
                err(f"{rel}:{ln}: column '{name}' = '{cell}' {problem} "
                    f"(schema type {col.get('type', 'str')})")
        for u in unique:
            if not all(c in idx for c in u):
                continue
            key = tuple(row[idx[c]].strip() for c in u)
            if any(not k for k in key):
                continue                    # an empty part does not form a key
            if key in seen_keys[u]:
                err(f"{rel}:{ln}: duplicate {'/'.join(u)} = {'/'.join(key)} "
                    f"(first at line {seen_keys[u][key]}). Readers keep one and "
                    f"drop the other.")
            else:
                seen_keys[u][key] = ln


def _col(cols, name):
    return next((c for c in cols if c["name"] == name), {})


_ref_cache = {}


def check_stale_allowances(reg):
    """An alsoAllowed name that now exists is a stale exception: it hides the
    next time that name goes missing. Fail until it is removed from the list."""
    def walk(node, where):
        if isinstance(node, dict):
            for k, v in node.items():
                if k in ("refersTo", "valueRefersTo"):
                    refs = [v] if k == "refersTo" else list(v.values())
                    for ref in refs:
                        names = _ref_set(ref, where)
                        for a in ref.get("alsoAllowed", []):
                            if names is not None and a in names:
                                err(f"{where}: '{a}' is in alsoAllowed but now exists in "
                                    f"{ref['file']} - remove it from the list in "
                                    f"tools/data_schemas.json (and close its ROADMAP item).")
                else:
                    walk(v, where)
        elif isinstance(node, list):
            for v in node:
                walk(v, where)
    for rel, schema in reg.get("schemas", {}).items():
        walk(schema, rel)


def _ref_set(ref, where):
    """The set of names a refersTo points at. Kinds:
         shared-param-names : column 3 of the PARAM rows of a Revit shared-parameter file
         csv-column         : every value of `column` in a registered CSV
    """
    key = (ref.get("kind"), ref.get("file"), ref.get("column"))
    if key in _ref_cache:
        return _ref_cache[key]
    path = os.path.join(REPO, ref["file"])
    names = None
    try:
        with io.open(path, encoding="utf-8-sig", newline="") as fh:
            text = fh.read()
        if ref["kind"] == "shared-param-names":
            names = {f[2] for f in (ln.split("	") for ln in text.splitlines())
                     if len(f) > 2 and f[0] == "PARAM"}
        elif ref["kind"] == "csv-column":
            rows = [r for r in csv.reader(io.StringIO(text)) if not _is_comment_or_blank(r)]
            i = [c.strip() for c in rows[0]].index(ref["column"])
            names = {r[i].strip() for r in rows[1:] if len(r) > i}
        else:
            err(f"{where}: refersTo kind {ref['kind']!r} is not one this validator knows.")
    except (OSError, ValueError, IndexError) as ex:
        err(f"{where}: refersTo target {ref['file']} could not be read - {ex}")
    _ref_cache[key] = names
    return names


def validate_structural(rel, fmt):
    """Registered without a deeper schema: format-level checks only."""
    global checked_files
    if fmt == "json":
        validate_json(rel, {})
        return
    text = read_text(rel)
    if text is None:
        return
    checked_files += 1
    if fmt in ("csv-sections", "csv-table"):
        try:
            list(csv.reader(io.StringIO(text)))
        except csv.Error as ex:
            err(f"{rel}: CSV does not parse — {ex}")


# ─────────────────────────────────────────────────────────────────────────────
#  Coverage
# ─────────────────────────────────────────────────────────────────────────────

def tracked_files(reg):
    """Every file under the registry's roots (git-tracked when git is available)."""
    out = []
    try:
        import subprocess
        roots = [r["path"] for r in reg["roots"]]
        listing = subprocess.check_output(
            ["git", "-C", REPO, "ls-files", "-z", "--"] + roots,
            stderr=subprocess.DEVNULL).decode("utf-8")
        out = [p for p in listing.split("\0") if p]
    except Exception:
        for r in reg["roots"]:
            for dp, _, fns in os.walk(os.path.join(REPO, r["path"])):
                for fn in fns:
                    out.append(os.path.relpath(os.path.join(dp, fn), REPO).replace(os.sep, "/"))
    result = []
    for p in out:
        for r in reg["roots"]:
            if p.startswith(r["path"].rstrip("/") + "/"):
                exts = r.get("extensions")
                if exts is None or os.path.splitext(p)[1].lower() in exts:
                    result.append(p)
                break
    return sorted(set(result))


def classify(reg, rel):
    """('schema', schema) | ('structural', fmt) | ('nondata', reason) | (None, None)"""
    if rel in reg.get("schemas", {}):
        return "schema", reg["schemas"][rel]
    if rel in reg.get("structural", {}):
        return "structural", reg["structural"][rel]
    for pat, reason in reg.get("nonData", {}).items():
        if fnmatch.fnmatchcase(rel, pat):
            return "nondata", reason
    return None, None


def check_coverage(reg):
    files = tracked_files(reg)
    unregistered = [f for f in files if classify(reg, f)[0] is None]
    for f in unregistered:
        err(f"{f}: NO SCHEMA. Every data file must be registered in "
            f"tools/data_schemas.json.\n      Run: python tools/validate_data_schemas.py "
            f"--scaffold \"{f}\"\n      and add the printed entry under \"schemas\" "
            f"(or under \"structural\" with the reason it has no column/key schema).")
    on_disk = set(files)
    for section in ("schemas", "structural"):
        for rel in reg.get(section, {}):
            if rel not in on_disk and not os.path.isfile(os.path.join(REPO, rel)):
                err(f"{rel}: registered under \"{section}\" in tools/data_schemas.json "
                    f"but the file does not exist. Remove the entry with the file.")
    for r in reg["roots"]:
        if not r.get("uniqueBasenames"):
            continue
        seen = {}
        for f in files:
            if not f.startswith(r["path"].rstrip("/") + "/"):
                continue
            if any(fnmatch.fnmatchcase(f, "*" + g.lstrip("*")) for g in r.get("uniqueBasenamesIgnore", [])):
                continue
            seen.setdefault(os.path.basename(f).lower(), []).append(f)
        for name, paths in sorted(seen.items()):
            if len(paths) > 1:
                err(f"{paths[1]}: has the same file name as {paths[0]}. "
                    f"{r.get('uniqueBasenamesReason', 'Names must be unique under this root.')} "
                    f"Rename one of them.")
    both = set(reg.get("schemas", {})) & set(reg.get("structural", {}))
    for rel in sorted(both):
        err(f"{rel}: registered under both \"schemas\" and \"structural\" — pick one.")
    return files


def validate_registered(reg, rel):
    kind, s = classify(reg, rel)
    if kind == "schema":
        fmt = s.get("format")
        if fmt == "csv-table":
            validate_csv_table(rel, s)
        elif fmt == "json":
            validate_json(rel, s)
        else:
            err(f"{rel}: schema format {fmt!r} is not one this validator knows.")
    elif kind == "structural":
        fmt = s if isinstance(s, str) else s.get("format")
        validate_structural(rel, fmt)


# ─────────────────────────────────────────────────────────────────────────────
#  Scaffold / describe — the schema is the documentation
# ─────────────────────────────────────────────────────────────────────────────

def scaffold(rel):
    rel = rel.replace("\\", "/")
    ext = os.path.splitext(rel)[1].lower()
    path = os.path.join(REPO, rel)
    if ext == ".csv":
        with io.open(path, encoding="utf-8-sig", newline="") as fh:
            rows = list(csv.reader(fh))
        hi = next(i for i, r in enumerate(rows) if not _is_comment_or_blank(r))
        header = [c.strip() for c in rows[hi]]
        body = [r for r in rows[hi + 1:] if not _is_comment_or_blank(r)]
        cols = []
        for i, name in enumerate(header):
            vals = [r[i].strip() for r in body if i < len(r) and r[i].strip()]
            col = {"name": name, "type": "str", "description": "TODO"}
            if vals and all(_is_num(v) for v in vals):
                col["type"] = "num"
            cols.append(col)
        entry = {"format": "csv-table", "schemaVersion": 1,
                 "description": "TODO — what the file is and who reads it",
                 "readers": ["TODO path/to/Reader.cs"], "columns": cols}
    elif ext == ".json":
        entry = {"format": "json", "schemaVersion": 1,
                 "description": "TODO", "readers": ["TODO"],
                 "kind": "object", "poco": ["TODO path/to/Poco.cs", "TODO ClassName"]}
    else:
        entry = "TODO reason this file has no schema"
    print(json.dumps({rel: entry}, indent=2, ensure_ascii=False))
    return 0


def _is_num(v):
    try:
        float(v.replace(",", ""))
        return True
    except ValueError:
        return False


def describe(reg, rel):
    rel = rel.replace("\\", "/")
    kind, s = classify(reg, rel)
    if kind is None:
        print(f"{rel}: not registered.")
        return 1
    if kind != "schema":
        print(f"{rel}: {kind} — {s}")
        return 0
    print(f"{rel}  (format {s.get('format')}, schema v{s.get('schemaVersion', 1)})")
    print(f"  {s.get('description', '')}")
    for r in s.get("readers", []):
        print(f"  read by: {r}")
    for c in s.get("columns", []):
        flags = [c.get("type", "str")]
        if c.get("required"):
            flags.append("required")
        if c.get("optional"):
            flags.append("optional column")
        if "enum" in c:
            flags.append("one of " + "/".join(c["enum"]))
        print(f"  - {c['name']:<16} [{', '.join(flags)}] {c.get('description', '')}")
    for u in s.get("unique", []):
        print(f"  unique: {' + '.join(u)}")
    if "poco" in s:
        print(f"  keys derived from {s['poco'][1]} in {s['poco'][0]}")
    return 0


# ─────────────────────────────────────────────────────────────────────────────
#  Self-test — prove each check fires
# ─────────────────────────────────────────────────────────────────────────────

def self_test(reg):
    """
    Prove the gate actually fires.

    A validator that reports OK because its own matching is broken is the exact
    failure mode this file exists to close — an empty finding list standing in
    for an error. So CI runs this first: each case mutates a real shipped file
    in a temp copy and asserts the validator rejects it. If any case passes
    validation, the gate is not working and the build fails on that alone.
    """
    global ROOT, errors, warnings, checked_files
    import shutil
    import tempfile

    D = "StingTools/Data/"
    cases = []

    def json_case(rel, label, mutate):
        cases.append((rel, label, "json", mutate))

    def text_case(rel, label, mutate):
        cases.append((rel, label, "text", mutate))

    def add_unknown_json(path):
        def m(doc):
            node = doc
            for p in path:
                node = node[p]
            node["thisKeyDoesNotExist"] = "x"
            return doc
        return m

    json_case(D + "STING_NRM2_MEASUREMENT_RULES.json", "unknown key on a rule",
              add_unknown_json(["rules", 0]))
    json_case(D + "STING_NRM2_MEASUREMENT_RULES.json", "unknown key at the root",
              add_unknown_json([]))
    json_case(D + "STING_DRAWING_TYPES.json", "unknown key on a drawing type",
              add_unknown_json(["drawingTypes", 0]))
    json_case(D + "STING_DRAWING_TYPES.json", "unknown key on a nested crop block",
              add_unknown_json(["drawingTypes", 0, "crop"]))
    json_case(D + "BOQ_DESCRIPTIONS.json", "unknown key on a description",
              add_unknown_json([0]))
    json_case(D + "Placement/STING_PLACEMENT_RULES.json", "unknown key on a placement rule",
              add_unknown_json(["Rules", 0]))
    def bad_sting_param(doc):
        doc["property_mappings"][0]["sting_param"] = "NO_SUCH_PARAM_TXT"
        return doc

    json_case(D + "IFC/ARCHICAD_IFC_MAPPING.json", "mapping targets a parameter that does not exist",
              bad_sting_param)
    json_case(D + "STING_NRM2_MEASUREMENT_RULES.json", "missing required key",
              lambda d: (d["rules"][0].pop("unit"), d)[1])

    def first_data_line(text):
        lines = text.split("\n")
        seen_header = False
        for i, ln in enumerate(lines):
            if not ln.strip() or ln.lstrip("﻿").lstrip().startswith("#"):
                continue
            if not seen_header:
                seen_header = True
                hdr = i
                continue
            return lines, hdr, i
        return lines, None, None

    def csv_rename_header(text):
        lines, hdr, _ = first_data_line(text)
        lines[hdr] = lines[hdr].replace("Unit", "Units", 1)
        return "\n".join(lines)

    def csv_insert_column(text):
        # The D6 defect itself: a column inserted at index 1 of header and rows.
        lines, hdr, _ = first_data_line(text)
        out = []
        for i, ln in enumerate(lines):
            if i < hdr or not ln.strip() or ln.lstrip().startswith("#"):
                out.append(ln)
                continue
            first, _, rest = ln.partition(",")
            out.append(first + "," + ("NEWCOL" if i == hdr else "x") + "," + rest)
        return "\n".join(out)

    def csv_break_number(col_index):
        def m(text):
            lines, _, row = first_data_line(text)
            parts = lines[row].split(",")
            parts[col_index] = "not-a-number"
            lines[row] = ",".join(parts)
            return "\n".join(lines)
        return m

    def csv_unquoted_comma(text):
        lines, _, row = first_data_line(text)
        lines[row] = lines[row] + ", and a stray comma"
        return "\n".join(lines)

    def csv_duplicate_row(text):
        lines, _, row = first_data_line(text)
        lines.insert(row + 1, lines[row])
        return "\n".join(lines)

    def mixed_eol(text):
        lines = text.split("\n")
        return "\r\n".join(lines[:3]) + "\n" + "\n".join(lines[3:])

    def not_utf8(text):
        return text.replace("e", "\udce9", 1)     # surrogateescape -> lone 0xE9 byte

    COST = D + "cost_rates_5d.csv"
    text_case(COST, "renamed column", csv_rename_header)
    text_case(COST, "inserted (undeclared) column — the D6 drift", csv_insert_column)
    text_case(COST, "non-numeric UGX rate", csv_break_number(5))
    text_case(COST, "row split by an unquoted comma", csv_unquoted_comma)
    text_case(COST, "duplicate DISC|PROD key", csv_duplicate_row)
    text_case(COST, "mixed line endings", mixed_eol)
    text_case(COST, "not UTF-8", not_utf8)
    text_case(D + "STING_DEFAULT_COST_RATES.csv", "non-numeric rate", csv_break_number(1))

    FORM = D + "FORMULAS_WITH_DEPENDENCIES.csv"

    def short_row(text):
        # Below the declared minFields (4): the reader would drop it.
        lines, _, row = first_data_line(text)
        lines[row] = ",".join(next(csv.reader([lines[row]]))[:3])
        return "\n".join(lines)

    def bogus_input(text):
        lines, _, row = first_data_line(text)
        f = next(csv.reader([lines[row]]))
        f[5] = (f[5] + ", " if f[5] else "") + "NO_SUCH_PARAM_TXT"
        out = io.StringIO()
        csv.writer(out, lineterminator="").writerow(f)
        lines[row] = out.getvalue()
        return "\n".join(lines)

    text_case(FORM, "row shorter than minFields", short_row)
    text_case(FORM, "Input_Parameters names a parameter that does not exist", bogus_input)
    text_case(FORM, "row split by an unquoted comma (minFields file)", csv_unquoted_comma)
    text_case(D + "BOQ_DESCRIPTIONS.json", "duplicate JSON key",
              lambda t: t.replace('"category"', '"category": "dup", "category"', 1))
    text_case(D + "BOQ_DESCRIPTIONS.json", "empty file", lambda t: "")

    failures = []
    tmp = tempfile.mkdtemp(prefix="sting_schema_selftest_")
    try:
        for rel, label, mode, mutate in cases:
            errors, warnings, checked_files = [], [], 0
            ROOT = REPO
            src = os.path.join(REPO, rel)
            dst = os.path.join(tmp, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if mode == "json":
                with io.open(src, encoding="utf-8-sig") as fh:
                    doc = json.load(fh)
                with io.open(dst, "w", encoding="utf-8") as fh:
                    json.dump(mutate(doc), fh)
            else:
                with io.open(src, encoding="utf-8-sig", newline="") as fh:
                    text = fh.read().replace("\r\n", "\n")
                with io.open(dst, "wb") as fh:
                    fh.write(mutate(text).encode("utf-8", "surrogateescape"))
            ROOT = tmp
            validate_registered(reg, rel)
            if not errors:
                failures.append(f"{rel}: '{label}' was NOT caught")

        # Coverage: an unregistered file and a registered-but-missing file both fail.
        errors, warnings, checked_files = [], [], 0
        ROOT = REPO
        fake = dict(reg)
        fake["schemas"] = dict(reg["schemas"])
        fake["structural"] = dict(reg.get("structural", {}))
        victim = next(iter(fake["structural"]))
        fake["structural"].pop(victim)
        fake["schemas"]["StingTools/Data/__no_such_file__.csv"] = {"format": "csv-table", "columns": []}
        check_coverage(fake)
        if not any(victim in e and "NO SCHEMA" in e for e in errors):
            failures.append("coverage: an unregistered data file was NOT caught")
        if not any("__no_such_file__" in e for e in errors):
            failures.append("coverage: a registered file that does not exist was NOT caught")
        total = len(cases) + 3

        # The other direction: no FALSE unknown key. A doc comment "...the wet zone
        # class this fixture is rated for" once read as `class this` and hid the
        # properties after it, so correctly-bound keys failed. A gate that cries wolf
        # gets its findings ignored.
        props = poco_properties("StingTools/Core/Placement/PlacementRule.cs", "PlacementRule") or set()
        if not {"WetZoneClass", "BuildingType", "WetZoneExclusion"} <= props:
            failures.append("POCO scan: a comment containing 'class' truncated PlacementRule "
                            "- correctly-bound keys would be reported UNKNOWN")
        # Members bound other than as a public auto-property: an inherited one, and
        # a private [JsonProperty] alias. Missing either is a false UNKNOWN KEY.
        lcc = poco_properties("StingTools/Commands/Hvac/HvacLifeCycleCompareCommand.cs",
                              "LccOptionConfig") or set()
        if "CapitalCost" not in lcc:
            failures.append("POCO scan: inherited members (LccOptionConfig : LccOption) not followed")
        par = poco_properties("StingTools/Core/Symbols/SymbolDefinition.cs", "ParameterDefinition") or set()
        if not {"instance", "isInstance"} <= par:
            failures.append("POCO scan: a private [JsonProperty(\"instance\")] alias was not counted")
        total += 3

        # A stale exception: a name in alsoAllowed that now exists.
        import copy
        errors = []
        fake3 = copy.deepcopy(reg)
        ref = (fake3["schemas"]["StingTools/Data/IFC/ARCHICAD_IFC_MAPPING.json"]
               ["children"]["property_mappings"]["valueRefersTo"]["sting_param"])
        ref["alsoAllowed"] = ref.get("alsoAllowed", []) + ["PER_U_VALUE_W_M2K"]
        check_stale_allowances(fake3)
        if not any("now exists" in e for e in errors):
            failures.append("allowances: a stale alsoAllowed entry was NOT caught")
        total += 1

        # Two data files with one name: FindDataFile reads only the first.
        global tracked_files
        real_tracked = tracked_files
        try:
            dup = "StingTools/Data/Plumbing/cost_rates_5d.csv"
            tracked_files = lambda r: sorted(real_tracked(r) + [dup])
            errors = []
            fake2 = dict(reg)
            fake2["structural"] = dict(reg.get("structural", {}))
            fake2["structural"][dup] = "csv-sections"
            check_coverage(fake2)
            if not any("same file name" in e for e in errors):
                failures.append("coverage: two data files sharing a name were NOT caught")
        finally:
            tracked_files = real_tracked
        total += 1
    finally:
        ROOT = REPO
        errors, warnings, checked_files = [], [], 0
        shutil.rmtree(tmp, ignore_errors=True)

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
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    if "--scaffold" in sys.argv:
        return scaffold(sys.argv[sys.argv.index("--scaffold") + 1])

    reg = load_registry()

    if "--describe" in sys.argv:
        return describe(reg, sys.argv[sys.argv.index("--describe") + 1])

    if "--self-test" in sys.argv:
        return self_test(reg)

    if "--list" in sys.argv:
        for rel, schema in sorted(reg["schemas"].items()):
            if schema.get("format") == "json":
                keys = allowed_keys(schema, rel)
                print(f"{rel}: {sorted(keys) if keys else '(structural only)'}")
        return 0

    files = check_coverage(reg)
    check_stale_allowances(reg)
    for rel in files:
        validate_registered(reg, rel)

    for w in warnings:
        print(f"::warning::{w}")

    n_schema = sum(1 for f in files if classify(reg, f)[0] == "schema")
    n_struct = sum(1 for f in files if classify(reg, f)[0] == "structural")
    n_non = sum(1 for f in files if classify(reg, f)[0] == "nondata")

    if errors:
        print(f"\n{len(errors)} schema error(s) across {checked_files} file(s):\n")
        for e in errors:
            print(f"  [FAIL] {e}")
        print("\nAn unknown key or a shifted column is not cosmetic: Newtonsoft's")
        print("MissingMemberHandling is Ignore everywhere in this repo and most CSV")
        print("readers index by position, so the value is dropped or misread at load")
        print("and the feature reading it does nothing, with no error anywhere.")
        print("How to change a data file and its schema together: docs/DATA_SCHEMAS.md")
        return 1

    print(f"OK - {len(files)} file(s) under the registry roots: {n_schema} against a full "
          f"schema, {n_struct} structural-only, {n_non} non-data; "
          f"{checked_files} parsed, {len(warnings)} warning(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
