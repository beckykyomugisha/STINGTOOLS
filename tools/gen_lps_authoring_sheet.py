import collections
import glob
import io
import re

FAM = re.compile(r"^Tag\s+Family\s*#\d+\s*:\s*(?P<n>.+?)\s*$", re.I)


def split(line):
    out, cur, q = [], "", False
    for ch in line:
        if ch == '"':
            q = not q
        elif ch == "," and not q:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return out


# ── LPS-specific rows, from the declarations ────────────────────────────────
rows = collections.OrderedDict()
owners = collections.defaultdict(set)
warns = collections.OrderedDict()

for p in sorted(glob.glob("StingTools/Data/STING_TAG_CONFIG_v5_0_*.csv")):
    if "DesignConstruction" in p:
        continue
    cur = None
    for line in io.open(p, encoding="utf-8-sig"):
        s = line.rstrip("\n").strip()
        m = FAM.match(s)
        if m:
            cur = m.group("n")
            continue
        if s.startswith("TAG_FAMILY,"):
            cur = None
            continue
        if not cur or "LPS" not in cur:
            continue
        f = split(s)
        if len(f) > 3 and f[0].isdigit():
            if f[1] in ("HIGH", "MEDIUM", "CRITICAL", "MED", "LOW"):
                warns.setdefault(f[2], (f[1], f[3], f[4] if len(f) > 4 else ""))
            elif f[1] in ("T1", "T2", "T3"):
                owners[f[2]].add(cur)
                rows.setdefault(f[2], (f[1], f[3], f[4] if len(f) > 4 else ""))

# ── shared rows, verbatim from the universal sheet ──────────────────────────
shared = []
for line in io.open("docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md", encoding="utf-8"):
    m = re.match(r"^\|\s*\d+\s*\|\s*(T\d+)\s*\|(.*)$", line.rstrip("\n"))
    if m and m.group(1) not in ("T1", "T2", "T3"):
        shared.append((m.group(1), "|" + m.group(2)))


# ── numeric parameters cannot appear in a Text label formula ────────────────
# Revit has no number-to-string conversion in family formulas, so
# `if(BOOL, <NUMBER>, "")` raises "Inconsistent Units" - the two branches are
# different types. Measured 2026-09-22 while hand-building the master: it
# blocked ELC_LPS_AIR_TERMINAL_COUNT_NR, _PROTECTION_ANGLE_DEG and
# _CONDUCTOR_CROSS_SECT_MM2, and nine more would have followed.
#
# Every one of them already has a TEXT twin - the _NR / _TXT pairing this
# library uses throughout - so the label reads the twin.
_dt = {}
for _line in io.open("StingTools/Data/MR_PARAMETERS.txt", encoding="utf-8-sig"):
    _f = _line.rstrip("\n").split("\t")
    if _f[0] == "PARAM" and len(_f) > 3:
        _dt.setdefault(_f[2], _f[3])


def text_source(param):
    """The TEXT parameter a label row should read, and why it differs."""
    if _dt.get(param, "TEXT") == "TEXT":
        return param, None
    # Both spellings are in use: ELC_LPS_PROTECTION_ANGLE_DEG pairs with
    # ..._ANGLE_TXT (unit dropped), while ELC_LPS_INSPECTION_INTERVAL_MONTHS
    # pairs with ..._MONTHS_TXT (unit kept). Trying only the first reported the
    # second as having no twin, which would have removed a real row.
    stem = re.sub(r"_(NR|BOOL|MM2|MM|DEG|OHM|MONTHS|M|YRS|KG|PCT)$", "", param)
    for twin in (param + "_TXT", stem + "_TXT"):
        if _dt.get(twin) == "TEXT":
            return twin, _dt.get(param)
    # No twin: say so in the sheet rather than emitting a formula that cannot
    # be entered. Silence here would be found one dialog at a time.
    return None, _dt.get(param)


def pad(v):
    """Render a cell so markdown cannot eat what matters.

    Two hazards, both silent:

    A markdown cell pads with a space either side, so `| - |` is the same
    whether the prefix is "-" or " - ". Eighty prefixes across the tag config
    carry padding that separates two values sharing a line.

    And a raw pipe ENDS the cell. Three prefixes begin with one - "| A4:",
    "| B6:", "| " - and printed unescaped they became an empty Prefix and a
    Suffix holding what should have been the prefix. That is exactly how they
    were typed into the master.
    """
    if not v:
        return ""
    lead = len(v) - len(v.lstrip(" "))
    trail = len(v) - len(v.rstrip(" "))
    core = v.strip(" ")
    out = ("\u2423" * len(v)) if not core else (
        "\u2423" * lead + core + "\u2423" * trail)
    return out.replace("|", "\\|")


def nice(param, tier):
    """Calc Value Name, following the universal sheet's convention."""
    core = param.replace("ELC_LPS_", "")
    # Strip a TRAILING unit suffix only. Replacing "_M" anywhere turned
    # CONDUCTOR_MATERIAL into "Conductoraterial".
    core = re.sub(r"_(TXT|NR|BOOL|DT|MM2|MM|DEG|OHM|MONTHS|M)$", "", core)
    return "Show %s - LPS %s" % (tier, core.replace("_", " ").title())


out = []
w = out.append

w("# STING LPS Tag master — hand-authoring sheet")
w("")
w("Copy-paste companion to `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`, generated from")
w("`STING_TAG_CONFIG_v5_0_*.csv` and that sheet on 2026-09-22. Regenerate rather")
w("than hand-edit.")
w("")
w("**Build from `Multi-Category Tag.rft`, as a TENTH family named")
w("`STING_LPS_Tag_Universal`.** Not by editing one of the nine: a master that is")
w("also a target is overwritten by its own propagation, and the run still reports")
w("success.")
w("")
w("Same mechanics as the universal sheet. Every non-T1 row is a Calculated Value")
w("(fx): Name, Type=Text, paste Formula, then Prefix/Suffix, **Spaces=0**, Break.")
w("Set Spaces=0 BEFORE ticking Break; Spaces is only editable when the row ABOVE")
w("has no Break.")
w("")
w("---")
w("")
w("## Why T4-T10 are copied, not taken from the LPS declarations")
w("")
w("The LPS families declare 35 shared rows. The universal master carries **61**,")
w("and that is what the other 197 families now actually hold. The LPS")
w("declarations predate the universal pivot, so they are 26 rows short - missing")
w("cost, carbon, fabrication and clash rows that every other tag in the library")
w("has.")
w("")
w("Three of those 26 are arguably irrelevant here - ASS_CAPACITY_TXT,")
w("ASS_POWER_RATING_TXT and ASS_FLOW_RATE_TXT. An air terminal has no flow")
w("rate. They are kept anyway, because that is what the universal design")
w("already does everywhere else: a Door Tag carries the flow-rate row too and")
w("renders it blank. Dropping them would buy nothing at render time and cost a")
w("second label shape to reason about forever.")
w("")
w("So T4-T10 below are the UNIVERSAL master's rows, verbatim, Break values and")
w("all. They are already verified by the 197-family run. Using the LPS")
w("declarations instead would make these nine the only tags in the library with a")
w("different shared label.")
w("")
w("---")
w("")
w("## STEP 1 - LPS rows (T1-T3, %d rows)" % len(rows))
w("")
w("`used` is how many of the nine declare the row. A row used by one family still")
w("belongs in the master: it renders blank on the other eight, exactly as every")
w("tier already does.")
w("")
w("**Spaces is 0 on every row**, and the column is printed rather than stated")
w("once: Revit defaults it to 1, and an extra gap before every value is subtle")
w("enough to survive review.")
w("")
w("**\u2423 marks a space that matters.** A markdown cell pads with a space either")
w("side, so `| - |` reads the same whether the prefix is `-` or ` - `. Type a real")
w("space wherever you see \u2423.")
w("")
w("**Numeric parameters read their TEXT twin.** Revit has no number-to-string")
w("conversion in family formulas, so `if(BOOL, <NUMBER>, \"\")` is rejected as")
w("\"Inconsistent Units\" - the branches are different types. Twelve rows here are")
w("affected and each names the twin it reads, with the original type in italics.")
w("")
w("> The twins are NOT yet written by anything. A label pointed at")
w("> `ELC_LPS_PROTECTION_ANGLE_TXT` renders blank until that parameter holds a")
w("> value, whether typed by hand or mirrored from the numeric. Build the rows")
w("> now; the mirror is a separate job.")
w("")
w("**Break is a suggestion here** - one per tier boundary. The declarations do not")
w("record line breaks, so unlike the T4-T10 block below these are not verified.")
w("Adjust as the label reads.")
w("")
w("| # | Tier | Calc Value Name | Formula | Spaces | Prefix | Suffix | Break | used |")
w("|---|---|---|---|---|---|---|---|---|")

n = 0
order = [k for k in rows if rows[k][0] == "T1"] + \
        [k for k in rows if rows[k][0] == "T2"] + \
        [k for k in rows if rows[k][0] == "T3"]
for idx, k in enumerate(order):
    tier, pre, suf = rows[k]
    n += 1
    last_of_tier = (idx + 1 == len(order)) or rows[order[idx + 1]][0] != tier
    brk = "YES" if last_of_tier else "no"
    if tier == "T1":
        # T1 has no gate - added directly, like the universal sheet's row 1.
        formula = "_(add parameter directly - no fx)_"
        # Name the PARAMETER: the universal sheet prints "---" for its
        # single T1 row, but there are seven here and a column of dashes
        # cannot be copied from.
        name = "`%s`" % k
        brk = "YES"
    else:
        name = nice(k, tier)
        src, was = text_source(k)
        if src is None:
            formula = ("**cannot be a label row** - `%s` is %s and has no _TXT twin; "
                       "Revit rejects a number in a Text formula" % (k, was))
        elif was:
            formula = ('`if(TAG_PARA_STATE_%s_BOOL, %s, "")` <br>_(%s is %s - reads its TEXT twin)_'
                       % (tier[1:], src, k, was))
        else:
            formula = '`if(TAG_PARA_STATE_%s_BOOL, %s, "")`' % (tier[1:], src)
    w("| %d | %s | %s | %s | 0 | %s | %s | %s | %d/9 |"
      % (n, tier, name, formula, pad(pre), pad(suf), brk, len(owners[k])))

w("")
w("## STEP 2 - Shared rows (T4-T10, %d rows)" % len(shared))
w("")
w("Copied verbatim from `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`. Revit blocks")
w("cross-category label paste, so they must be re-typed - but nothing here needs")
w("a decision, and the Break values are known-good.")
w("")
w("| # | Tier | Calc Value Name | Formula | Spaces | Prefix | Suffix | Break |")
w("|---|---|---|---|---|---|---|---|")
for i, (tier, rest) in enumerate(shared, start=n + 1):
    w("| %d | %s %s" % (i, tier, rest))

total = n + len(shared)
w("")
w("**Total: %d rows** (%d LPS + %d shared)." % (total, n, len(shared)))
w("")
w("## STEP 3 - Warnings (%d) - NOT label rows" % len(warns))
w("")
w("**Nothing to author here.** The universal master contains zero warning label")
w("rows, and neither should this one. They are listed so the set is visible, not")
w("so it can be typed in.")
w("")
w("Warnings reach a tag by a different route entirely:")
w("")
w("1. They are DECLARED in `STING_TAG_CONFIG_v5_0_*.csv` - already done, all %d." % len(warns))
w("2. The plugin evaluates each against its `warning_thresholds` entry in")
w("   `PARAMETER_REGISTRY.json` (`EvaluateAndPopulateWarnings`) and writes the")
w("   `WARN_*` parameters onto the element.")
w("3. The text is concatenated into the **TAG7 narrative**, which reaches the")
w("   label through the `ASS_TAG_7A..7F_TXT` rows - already in STEP 2, so the")
w("   master gets them with the shared block.")
w("4. Optionally, the green/amber/red **badges** read")
w("   `STING_GATE_DATA_STATUS_INT` / `STING_GATE_QA_STATUS_INT`. Those are")
w("   family glyphs, not label rows - see STEP 4 of the universal sheet.")
w("")
w("Two switches control all of it at tag level, and both are already shared")
w("parameters: `TAG_WARN_VISIBLE_BOOL` (master on/off) and")
w("`TAG_WARN_SEVERITY_FILTER_TXT`.")
w("")
w("> `TAG_WARN_VISIBLE_BOOL` is YESNO - write it BARE in a formula. Comparing it")
w("> to \"Yes\" fails with the same \"Inconsistent Units\" that blocked the twelve")
w("> numeric rows.")
w("")
w("The set, for reference:")
w("")
w("| severity | parameter | condition |")
w("|---|---|---|")
for k, (sev, txt, std) in warns.items():
    w("| %s | `%s` | %s |" % (sev, k, txt.replace("|", "/")[:92]))

w("")
w("## STEP 4 - After the master is built")
w("")
w("1. Load it into the project.")
w("2. **Propagate Universal Tag**, master = `STING_LPS_Tag_Universal`.")
w("3. The confirmation must say it is propagating the **LPS** master and that 197")
w("   families belong to `universal` and will be skipped. If it says it is")
w("   propagating the universal master, the master's own declaration is not being")
w("   read - stop, because it would then skip all nine targets and report a clean")
w("   run having done nothing.")
w("4. Expect `9 propagated, 0 failed, 197 skipped (declared)`.")

io.open("docs/LPS_TAG_MASTER_BUILD_SHEET.md", "w", encoding="utf-8", newline="").write("\n".join(out) + "\n")
print("wrote docs/LPS_TAG_MASTER_BUILD_SHEET.md — %d LPS + %d shared = %d rows, %d warnings"
      % (n, len(shared), total, len(warns)))
