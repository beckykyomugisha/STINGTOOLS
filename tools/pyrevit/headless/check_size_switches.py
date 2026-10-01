# -*- coding: utf-8 -*-
# Headless check of the specialist tag families after Size Copies:
#  - each size label / box is tied to its TXT switch
#  - each family type turns on exactly its own size switch
#  - a PNG of the family view per type (visual proof)
# Read-only: every document is closed without saving.
import io, os, time, traceback
# Usage (Revit closed):
#   pyrevit run tools/pyrevit/headless/check_size_switches.py --revit=2025 --purge
# Folder of the .rfa files: env STING_TAG_BUILD, default %USERPROFILE%\Documents\STING_TAG_BUILD.
# Results go to a .log in that folder (pyrevit run shows no console).
from Autodesk.Revit.DB import (FilteredElementCollector, TextElement, TextNote, CurveElement, View,
                               BuiltInParameter, ImageExportOptions, ExportRange, ImageFileType,
                               ImageResolution, ZoomFitType, Transaction, ElementId, Element)
from System.Collections.Generic import List

BUILD = os.environ.get("STING_TAG_BUILD") or os.path.join(os.path.expanduser("~"), "Documents", "STING_TAG_BUILD")
OUT = os.path.join(BUILD, "verify")
NAMES = ["Fire Door", "Accessible Door", "Room Finish", "Fire Compartment"]
LOG = os.path.join(BUILD, "verify_run.log")
MM = 1.0 / 304.8
lines = []
def out(m):
    lines.append(u"%s" % m)
    with io.open(LOG, "w", encoding="utf-8") as f:
        f.write(u"\n".join(lines) + u"\n")

if not os.path.isdir(OUT):
    os.makedirs(OUT)
app = __revit__.Application
out("start %s" % time.strftime("%H:%M:%S"))
for n in NAMES:
    d = None
    try:
        d = app.OpenDocumentFile(os.path.join(BUILD, "STING - %s Tag.rfa" % n))
        fm = d.FamilyManager
        params = dict((p.Definition.Name, p) for p in fm.Parameters)
        out("== %s" % d.Title)
        for sw in ("TXT_2_5", "TXT_3_5"):
            fp = params.get(sw)
            elems = [ep.Element for ep in fp.AssociatedParameters] if fp else []
            labels = [e for e in elems if isinstance(e, TextElement) and not isinstance(e, TextNote)]
            curves = [e for e in elems if isinstance(e, CurveElement)]
            sizes = []
            for l in labels:
                t = d.GetElement(l.GetTypeId())
                sizes.append(round(t.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() / MM, 2))
            out("  %s -> labels %s, box lines %d" % (sw, sizes, len(curves)))
        view = [v for v in FilteredElementCollector(d).OfClass(View) if not v.IsTemplate][0]
        for ft in fm.Types:
            tname = ft.Name
            v25 = ft.AsInteger(params["TXT_2_5"]) if "TXT_2_5" in params else None
            v35 = ft.AsInteger(params["TXT_3_5"]) if "TXT_3_5" in params else None
            out("  type %s: TXT_2_5=%s TXT_3_5=%s" % (tname, v25, v35))
            t = Transaction(d, "verify switch type")
            t.Start()
            fm.CurrentType = ft
            t.Commit()
            o = ImageExportOptions()
            o.ExportRange = ExportRange.SetOfViews
            ids = List[ElementId](); ids.Add(view.Id)
            o.SetViewsAndSheets(ids)
            o.FilePath = os.path.join(OUT, ("%s__%s" % (n, tname)).replace(" ", "_"))
            o.HLRandWFViewsFileType = ImageFileType.PNG
            o.ImageResolution = ImageResolution.DPI_300
            o.ZoomType = ZoomFitType.FitToPage
            o.PixelSize = 1200
            d.ExportImage(o)
        out("  exported")
    except Exception:
        out("EXCEPTION %s:\n%s" % (n, traceback.format_exc()))
    finally:
        if d is not None:
            d.Close(False)
out("end %s" % time.strftime("%H:%M:%S"))
