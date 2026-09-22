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
w("**Break is a suggestion here** - one per tier boundary. The declarations do not")
w("record line breaks, so unlike the T4-T10 block below these are not verified.")
w("Adjust as the label reads.")
w("")
w("| # | Tier | Calc Value Name | Formula | Prefix | Suffix | Break | used |")
w("|---|---|---|---|---|---|---|---|")

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
        formula = '`if(TAG_PARA_STATE_%s_BOOL, %s, "")`' % (tier[1:], k)
    w("| %d | %s | %s | %s | %s | %s | %s | %d/9 |"
      % (n, tier, name, formula, pre or "", suf or "", brk, len(owners[k])))

w("")
w("## STEP 2 - Shared rows (T4-T10, %d rows)" % len(shared))
w("")
w("Copied verbatim from `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`. Revit blocks")
w("cross-category label paste, so they must be re-typed - but nothing here needs")
w("a decision, and the Break values are known-good.")
w("")
w("| # | Tier | Calc Value Name | Formula | Prefix | Suffix | Break |")
w("|---|---|---|---|---|---|---|")
for i, (tier, rest) in enumerate(shared, start=n + 1):
    w("| %d | %s %s" % (i, tier, rest))

total = n + len(shared)
w("")
w("**Total: %d rows** (%d LPS + %d shared)." % (total, n, len(shared)))
w("")
w("## STEP 3 - Warning rows (%d)" % len(warns))
w("")
w("Same pattern as the universal sheet's warning block.")
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
