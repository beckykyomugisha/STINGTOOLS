import io
import re
import subprocess

BEFORE, AFTER = "8f812bfc3", "db912c04d"
SHEET = "docs/LPS_TAG_MASTER_BUILD_SHEET.md"


def rows(ref):
    txt = subprocess.run(["git", "show", "%s:%s" % (ref, SHEET)],
                         capture_output=True).stdout.decode("utf-8", "replace")
    out = {}
    for line in txt.split("\n"):
        m = re.match(r"^\|\s*(\d+)\s*\|\s*(T\d+)\s*\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|", line)
        if m:
            n, tier, name, f, pre, suf = m.groups()
            out[(tier, name.strip())] = (n.strip(), f.strip(), pre.strip(), suf.strip())
    return out


old, new = rows(BEFORE), rows(AFTER)
changed = [(k, old[k], new[k]) for k in new if k in old and old[k][1] != new[k][1]]
changed.sort(key=lambda x: int(x[2][0]))


def param(formula):
    m = re.search(r"BOOL,\s*([A-Z0-9_]+)", formula)
    return m.group(1) if m else "?"


o = []
w = o.append
w("# LPS master — corrections only")
w("")
w("**%d rows changed. Nothing else did.** Everything you have already entered is" % len(changed))
w("still correct; only these need editing, and only the Formula field.")
w("")
w("## Why")
w("")
w("Revit has no number-to-string conversion in family formulas, so a Text")
w("calculated value cannot reference a NUMBER, LENGTH or YESNO parameter -")
w("`if(BOOL, <NUMBER>, \"\")` has two differently-typed branches and is rejected as")
w("**Inconsistent Units**. Each of these now reads the parameter's TEXT twin,")
w("which already existed.")
w("")
w("Rows **42** and **70** are in the shared block, so they were wrong in the")
w("universal sheet too - worth checking how they were authored in the universal")
w("master, which is already built.")
w("")
w("## What to change")
w("")
w("For each row: open its Calculated Value (fx), replace the Formula, OK. The")
w("Name, Prefix, Suffix and Break stay exactly as they are.")
w("")
w("| # | Tier | Calc Value Name | Replace formula with | Prefix | Suffix |")
w("|---|---|---|---|---|---|")
for (tier, name), (on, of, opre, osuf), (nn, nf, npre, nsuf) in changed:
    clean = re.sub(r"\s*<br>.*$", "", nf)
    w("| %s | %s | %s | %s | %s | %s |" % (nn, tier, name, clean, npre or "", nsuf or ""))

w("")
w("## Just the swaps, if you prefer to read it that way")
w("")
w("```")
for (tier, name), (on, of, _a, _b), (nn, nf, _c, _d) in changed:
    w("#%-3s %-36s -> %s" % (nn, param(of), param(nf)))
w("```")
w("")
w("## One thing to know")
w("")
w("The `_TXT` twins are not written by anything yet. These rows will render BLANK")
w("until each twin holds a value. That is expected, not a mistake in the rows -")
w("the mirror that fills them from the numerics is a separate piece of work.")

io.open("docs/LPS_MASTER_CORRECTIONS.md", "w", encoding="utf-8", newline="").write("\n".join(o) + "\n")
print("wrote docs/LPS_MASTER_CORRECTIONS.md — %d rows" % len(changed))
