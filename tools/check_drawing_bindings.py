#!/usr/bin/env python3
"""Drawing-layer parameters must be bound where the drawing layer writes them.

WHY THIS EXISTS
===============
A shared parameter that is not bound to a category does not exist on elements of
that category: ``LookupParameter`` returns null, the write returns false, and --
because every stamp write in the drawing layer is best-effort -- nothing says so.
Measured 2026-10-01 (DTW-55..59):

  * twelve+ view stamps (STING_DRAWING_TYPE_ID_TXT, STING_VIEW_CONTEXT_TAG_TXT,
    STING_PRODUCTION_RULE_IDX_INT, ...) were bound ``<ALL>``. ``<ALL>`` is the 139
    ELEMENT categories in PARAMETER_REGISTRY.json universal_categories plus Sheets;
    it does not contain Views. Every write to a view or view template was a no-op,
    so DrawingProducer.FindExistingView (keyed on those stamps) never found a view
    it had produced, and every re-run duplicated the views.
  * the three match-line stamps are written to detail curves (OST_Lines) and were
    bound ``<ALL>`` too, so re-runs duplicated match lines.
  * 46 healthcare AEC filters named a rule parameter that was not bound to the
    categories the filter targets (CLN_PRESS_REGIME_TXT on equipment, filter on
    Rooms), so ParameterFilterElement.Create refused every one of them.

WHAT IT CHECKS
==============
  (a) every parameter in tools/drawing_binding_contract.json "views" / "sheets" /
      "lines" (and each "categories" entry, by category name) is bound (in StingTools/Data/RESOLVED_BINDINGS.csv, as the loader
      resolves it) to Views / Sheets / Lines;
  (b) a grep over StingTools/**/*.cs: every STING parameter read or written on a
      view-typed receiver (view, v, template, tmpl, ...) is in the contract's
      "views" list or its allow-list, and every one written to a match-line detail
      curve is in "lines" -- so a new view stamp cannot be added to the code and
      forgotten in the data;
  (c) every STING parameter used in a rule of StingTools/Data/STING_AEC_FILTERS.json
      is bound to every category that filter targets (Revit only offers a filter the
      parameters common to all of its categories), apart from the explicit, reasoned
      exceptions in the contract.

It checks binding DATA, not a live model: it cannot tell whether Revit accepts a
category (Category.AllowsBoundParameters). That is an in-Revit check.

USAGE
=====
    python tools/check_drawing_bindings.py          # exit 1 on any finding
    python tools/check_drawing_bindings.py -v       # also print what passed
"""
import csv
import json
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
DATA = os.path.join(REPO, 'StingTools', 'Data')
CONTRACT = os.path.join(REPO, 'tools', 'drawing_binding_contract.json')
VERBOSE = '-v' in sys.argv


def load_json(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


registry = load_json(os.path.join(DATA, 'PARAMETER_REGISTRY.json'))
ENUM = registry['category_enum_map']
# The loader's core set: universal_categories (Materials excluded) + OST_Sheets,
# which LoadSharedParamsCommand inserts by hand. Mirror it exactly.
CORE = {ENUM[c] for c in registry['universal_categories'] if c in ENUM and c != 'Materials'}
CORE.add('OST_Sheets')

declared = set()
with open(os.path.join(DATA, 'MR_PARAMETERS.txt'), encoding='utf-8', errors='replace') as f:
    for line in f:
        p = line.rstrip('\n').split('\t')
        if len(p) > 2 and p[0] == 'PARAM':
            declared.add(p[2])

spec = {}
with open(os.path.join(DATA, 'RESOLVED_BINDINGS.csv'), encoding='utf-8', newline='') as f:
    for row in csv.reader(f):
        if len(row) >= 2 and not row[0].startswith('#'):
            spec[row[0]] = row[1]


def bound_osts(name):
    """The OST_ categories the loader binds `name` to; empty = unbound."""
    cell = spec.get(name, '')
    out = set()
    for tok in (t for t in cell.split('|') if t):
        if tok == '<ALL>':
            out |= CORE
        elif tok in ENUM:
            out.add(ENUM[tok])
    return out


contract = load_json(CONTRACT)
failures = []
passes = []

# ── (a) the declared contract ────────────────────────────────────────────────
for key, ost in (('views', 'OST_Views'), ('sheets', 'OST_Sheets'), ('lines', 'OST_Lines')):
    if ost not in ENUM.values():
        failures.append(f"(a) PARAMETER_REGISTRY.json category_enum_map has no entry for {ost}, "
                        f"so nothing can bind to it")
    for name in contract.get(key, []):
        if name not in declared:
            failures.append(f"(a) {name}: in the '{key}' contract but not declared in MR_PARAMETERS.txt")
            continue
        got = bound_osts(name)
        if ost not in got:
            failures.append(f"(a) {name}: written to {key} but bound to "
                            f"'{spec.get(name, '') or 'NOTHING'}' -- no {ost}")
        else:
            passes.append(f"(a) {name} -> {ost}")

# DTW-76: other categories the drawing layer writes to, keyed by the
# category_enum_map display name ("Detail Items", "Generic Annotations", ...).
for cat, names in sorted(contract.get('categories', {}).items()):
    if cat.startswith('_'):
        continue
    ost = ENUM.get(cat)
    if ost is None:
        failures.append(f"(a) contract category '{cat}' is not in PARAMETER_REGISTRY.json category_enum_map")
        continue
    for name in names:
        if name not in declared:
            failures.append(f"(a) {name}: in the '{cat}' contract but not declared in MR_PARAMETERS.txt")
        elif ost not in bound_osts(name):
            failures.append(f"(a) {name}: written to {cat} but bound to "
                            f"'{spec.get(name, '') or 'NOTHING'}' -- no {ost}")
        else:
            passes.append(f"(a) {name} -> {ost}")

# ── (b) the code cross-check ─────────────────────────────────────────────────
src_consts = {}
for rel in ('StingTools/Core/ParamRegistry.cs', 'StingTools/Core/Drawing/DrawingTypeStamper.cs'):
    with open(os.path.join(REPO, rel), encoding='utf-8', errors='replace') as f:
        src_consts.update(re.findall(r'public const string (\w+)\s*=\s*"([A-Za-z0-9_]+)"', f.read()))

VIEW_RX = r'(?:view|v|vw|template|tmpl|tpl|av|activeView|sldView|draftingView|dep|viewTemplate|vt|legend)'
ARG_RX = r'(ParamRegistry\.\w+|DrawingTypeStamper\.\w+|PARAM_\w+|"[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+")'
VIEW_PATS = [
    re.compile(r'\b(?:ParameterHelpers\.)?(?:Set\w*|Get\w*|TryWriteViewParam|ReadStringParam)\(\s*'
               + VIEW_RX + r'\s*,\s*' + ARG_RX),
    re.compile(r'\b' + VIEW_RX + r'\??\.LookupParameter\(\s*' + ARG_RX),
]
LINE_PATS = [re.compile(r'\bTrySet\(\s*dc\s*,\s*' + ARG_RX),
             re.compile(r'\bdc\??\.LookupParameter\(\s*' + ARG_RX)]
LINE_FILES = ('MatchLineEngine.cs',)


def arg_name(a):
    if a.startswith('"'):
        return a.strip('"')
    return src_consts.get(a.split('.')[-1])


view_sites, line_sites = {}, {}
for dp, _, files in os.walk(os.path.join(REPO, 'StingTools')):
    if os.sep + 'obj' in dp or os.sep + 'bin' in dp:
        continue
    for fn in files:
        if not fn.endswith('.cs'):
            continue
        path = os.path.join(dp, fn)
        rel = os.path.relpath(path, REPO).replace(os.sep, '/')
        with open(path, encoding='utf-8', errors='replace') as f:
            text = f.read()
        # Whole-file matching: a call split over lines --
        #     SetString(
        #         template, "STING_DEFAULT_TAG_STYLE_TXT", ...)
        # -- is exactly how ManagedTemplateSyncer writes, and a per-line scan
        # never saw it.
        for rx in VIEW_PATS:
            for m in rx.finditer(text):
                n = arg_name(m.groups()[-1])
                # An undeclared STING_ name is the worst case (bound nowhere,
                # ever), so it is not filtered out here.
                if n and (n in declared or n.startswith('STING_')):
                    view_sites.setdefault(n, f'{rel}:{text.count(chr(10), 0, m.start()) + 1}')
        if fn in LINE_FILES:
            for rx in LINE_PATS:
                for m in rx.finditer(text):
                    n = arg_name(m.groups()[-1])
                    if n and n in declared:
                        line_sites.setdefault(n, f'{rel}:{text.count(chr(10), 0, m.start()) + 1}')

view_allow = contract.get('view_receiver_allow', {})
for n, site in sorted(view_sites.items()):
    if n in contract.get('views', []) or n in view_allow:
        passes.append(f"(b) {n} used on a view at {site}: in the contract")
        continue
    failures.append(f"(b) {n} is read/written on a view at {site} but is not in "
                    f"tools/drawing_binding_contract.json 'views' (bind it to Views, or add it "
                    f"to 'view_receiver_allow' with the reason it need not be)")
for n, site in sorted(line_sites.items()):
    if n not in contract.get('lines', []):
        failures.append(f"(b) {n} is written to a match-line detail curve at {site} but is not in "
                        f"the 'lines' contract")
for n in view_allow:
    if n not in view_sites:
        failures.append(f"(b) view_receiver_allow names {n}, which no view site uses any more -- "
                        f"remove the stale exception")

# ── (c) AEC filter rule parameters reach every filter category ──────────────
filters = load_json(os.path.join(DATA, 'STING_AEC_FILTERS.json'))['filters']
filter_allow = contract.get('filter_rule_allow', {})


def leaves(rule):
    if not isinstance(rule, dict):
        return
    if 'rules' in rule:
        for r in rule['rules']:
            yield from leaves(r)
    elif rule.get('param'):
        yield rule


seen_allow = set()
for flt in filters:
    cats = set(flt.get('categories') or [])
    for leaf in leaves(flt.get('rule')):
        kind = (leaf.get('kind') or '').lower()
        name = leaf['param']
        if kind in ('phase', 'workset', 'level') or name not in declared:
            continue            # built-in / external parameter: not ours to bind
        missing = sorted(cats - bound_osts(name))
        if not missing:
            passes.append(f"(c) {flt['id']}: {name} covers {len(cats)} categories")
            continue
        key = f"{flt['id']}|{name}"
        if key in filter_allow:
            seen_allow.add(key)
            continue
        failures.append(f"(c) filter {flt['id']}: rule parameter {name} is bound to "
                        f"'{spec.get(name, '') or 'NOTHING'}', not to {', '.join(missing)} -- "
                        f"ParameterFilterElement.Create refuses it")
for key in sorted(set(filter_allow) - seen_allow):
    failures.append(f"(c) filter_rule_allow names {key}, which no longer fails -- remove it")

if VERBOSE:
    for p in passes:
        print('  ok ', p)
if failures:
    print(f"check_drawing_bindings: {len(failures)} finding(s)")
    for f in failures:
        print('  FAIL', f)
    sys.exit(1)
print(f"check_drawing_bindings: OK ({len(passes)} checks: contract, "
      f"{len(view_sites)} view-site parameter(s), {len(line_sites)} match-line parameter(s), "
      f"{len(filters)} AEC filters)")
