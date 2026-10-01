# -*- coding: utf-8 -*-
# TAGFAM-9 evidence, headless:
#   pyrevit run tools/pyrevit/headless/audit_style_switches.py --revit=2025 --purge
# For every shipped tag family (StingTools/Data/TagFamilies, incl. _master): how many
# TAG_{size}{style}_{colour}_BOOL switches it carries, and how many family elements
# (labels, lines, ...) have any parameter associated with one. A switch nothing is
# associated with cannot change what the tag shows. Read-only: families closed unsaved.
# Log: %TEMP%\sting_style_switch_audit.log  (+ .csv)
import io, os, re, time, traceback

here = os.path.dirname(os.path.abspath(__file__))
LIB = os.environ.get("STING_TAG_LIB") or os.path.normpath(os.path.join(here, "..", "..", "..", "StingTools", "Data", "TagFamilies"))
LOG = os.path.join(os.environ.get("TEMP", "."), "sting_style_switch_audit.log")
CSV = LOG.replace(".log", ".csv")
SW = re.compile(r"^TAG_(2|2\.5|3|3\.5)(NOM|BOLD|ITALIC|BOLDITALIC)_[A-Z]+_BOOL$")
lines = []
def out(m):
    lines.append(u"%s" % m)
    with io.open(LOG, "w", encoding="utf-8") as f:
        f.write(u"\n".join(lines) + u"\n")

app = __revit__.Application
out("start %s  lib %s" % (time.strftime("%H:%M:%S"), LIB))
rows = [u"family,switches,associated_switches,associated_elements"]
files = []
for root, _, names in os.walk(LIB):
    for n in names:
        if n.lower().endswith(".rfa") and not re.search(r"\.\d{4}\.rfa$", n):
            files.append(os.path.join(root, n))
files.sort()
tot_fam = tot_with = tot_assoc_fam = 0
for p in files:
    d = None
    try:
        d = app.OpenDocumentFile(p)
        fm = d.FamilyManager
        sws = [fp for fp in fm.Parameters if SW.match(fp.Definition.Name or "")]
        assoc = [fp for fp in sws if fp.AssociatedParameters.Size > 0]
        elems = set()
        for fp in assoc:
            for ep in fp.AssociatedParameters:
                elems.add(ep.Element.Id.IntegerValue)
        tot_fam += 1
        if sws: tot_with += 1
        if assoc: tot_assoc_fam += 1
        rows.append(u'"%s",%d,%d,%d' % (os.path.relpath(p, LIB), len(sws), len(assoc), len(elems)))
        if assoc:
            out("ASSOCIATED %s: %d switch(es) drive %d element(s): %s" % (
                os.path.basename(p), len(assoc), len(elems), ", ".join(fp.Definition.Name for fp in assoc[:6])))
    except Exception:
        out("ERROR %s:\n%s" % (p, traceback.format_exc()))
    finally:
        if d is not None:
            d.Close(False)
with io.open(CSV, "w", encoding="utf-8") as f:
    f.write(u"\n".join(rows) + u"\n")
out("families %d | with switches %d | with any switch associated to an element %d" % (tot_fam, tot_with, tot_assoc_fam))
out("end %s  csv %s" % (time.strftime("%H:%M:%S"), CSV))
