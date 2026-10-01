# -*- coding: utf-8 -*-
# Headless wrapper for `pyrevit run`: open the specialist tag families, run the
# Size Copies pushbutton code unchanged against them, log to a file, close.
import io, os, sys, time, traceback
# Usage (Revit closed):
#   pyrevit run tools/pyrevit/headless/run_size_copies.py --revit=2025 --purge
# Folder of the .rfa files: env STING_TAG_BUILD, default %USERPROFILE%\Documents\STING_TAG_BUILD.
# Results go to a .log in that folder (pyrevit run shows no console).

BUILD = os.environ.get("STING_TAG_BUILD") or os.path.join(os.path.expanduser("~"), "Documents", "STING_TAG_BUILD")
NAMES = ["Fire Door", "Accessible Door", "Room Finish", "Fire Compartment"]
SCRIPT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                      "STINGFamilyTools.extension", "STING Families.tab", "Tag Labels.panel",
                      "Size Copies.pushbutton", "script.py")
LOG = os.path.join(BUILD, "size_copies_run.log")

lines = []
def out(msg):
    lines.append(u"%s" % msg)
    with io.open(LOG, "w", encoding="utf-8") as f:
        f.write(u"\n".join(lines) + u"\n")

out("start %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
app = __revit__.Application
out("Revit %s %s" % (app.VersionNumber, app.VersionBuild))
opened = []
try:
    for n in NAMES:
        p = os.path.join(BUILD, "STING - %s Tag.rfa" % n)
        d = app.OpenDocumentFile(p)
        opened.append(d)
        out("opened %s" % d.Title)

    # Run the pushbutton script with its print() captured into our log.
    class Cap(object):
        def write(self, s):
            s = s.rstrip("\n")
            if s:
                out(s)
        def flush(self):
            pass
    old = sys.stdout
    sys.stdout = Cap()
    try:
        g = {"__revit__": __revit__, "__name__": "__main__"}
        with io.open(SCRIPT, "r", encoding="utf-8") as f:
            code = f.read()
        exec(compile(code, SCRIPT, "exec"), g)
    finally:
        sys.stdout = old
except Exception:
    out("EXCEPTION:\n" + traceback.format_exc())
finally:
    for d in opened:
        try:
            t = d.Title
            d.Close(False)   # the script saved what it changed
            out("closed %s" % t)
        except Exception as ex:
            out("close failed: %s" % ex)
    out("end %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
