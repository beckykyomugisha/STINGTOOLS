# -*- coding: utf-8 -*-
# TAGFAM-6 timing, headless:
#   pyrevit run tools/pyrevit/headless/time_add_shared_params.py --revit=2025 --purge
# Calls the LOADED StingTools add-in's CreateTagFamiliesCommand.AddSharedParameters
# (private; reached by reflection) on a throwaway family made from Revit's Metric Door Tag
# template, with the parameter list Create Tag Fams uses for the Door Tag. Also times the
# two lookup strategies alone (old: walk the file per name; new: index once) against the
# same shared-parameter file. Nothing is saved; the shared-parameter setting is restored.
# Log: %TEMP%\sting_tagfam6_timing.log
import io, os, time, traceback
import System
from System.Reflection import BindingFlags
from System.Collections.Generic import List

LOG = os.path.join(os.environ.get("TEMP", "."), "sting_tagfam6_timing.log")
lines = []
def out(m):
    lines.append(u"%s" % m)
    with io.open(LOG, "w", encoding="utf-8") as f:
        f.write(u"\n".join(lines) + u"\n")

app = __revit__.Application
out("start %s  Revit %s" % (time.strftime("%H:%M:%S"), app.VersionNumber))
orig_sp = app.SharedParametersFilename
fam = None
try:
    asm = [a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "StingTools"]
    if asm:
        asm = asm[0]
    else:
        # pyrevit run does not start add-ins; load a build explicitly. Env STING_DLL, else the
        # smoke-test build in this checkout (what tools/run_revit_smoke.ps1 compiles).
        here = os.path.dirname(os.path.abspath(__file__))
        dll = os.environ.get("STING_DLL") or os.path.join(
            here, "..", "..", "..", "StingTools.Revit.SmokeTests", "bin", "Debug", "net8.0-windows", "StingTools.dll")
        dll = os.path.normpath(dll)
        if not os.path.exists(dll):
            raise Exception("StingTools add-in not loaded and no build at %s (set STING_DLL)" % dll)
        asm = System.Reflection.Assembly.LoadFrom(dll)
    out("StingTools.dll: %s" % asm.Location)
    sp_file = os.path.join(os.path.dirname(asm.Location), "data", "MR_PARAMETERS.txt")
    out("shared parameter file: %s (exists %s)" % (sp_file, os.path.exists(sp_file)))

    cfg = asm.GetType("StingTools.Tags.TagFamilyConfig")
    names = cfg.GetMethod("GetAllFamilyParams").Invoke(None, System.Array[System.Object](["Doors", "STING - Door Tag"]))
    names = list(names)
    out("parameters for STING - Door Tag: %d" % len(names))

    # Lookup strategies alone, same file, same names.
    app.SharedParametersFilename = sp_file
    df = app.OpenSharedParameterFile()
    t0 = time.time()
    found_old = 0
    for n in names:
        hit = None
        for g in df.Groups:
            for d in g.Definitions:
                if d.Name == n:
                    hit = d
                    break
            if hit is not None:
                break
        if hit is not None:
            found_old += 1
    t_old = time.time() - t0
    t0 = time.time()
    idx = {}
    total_defs = 0
    for g in df.Groups:
        for d in g.Definitions:
            total_defs += 1
            if d.Name not in idx:
                idx[d.Name] = d
    found_new = sum(1 for n in names if n in idx)
    t_new = time.time() - t0
    out("lookup only, %d definitions: per-name walk %.2f s (%d found) | index once %.2f s (%d found)"
        % (total_defs, t_old, found_old, t_new, found_new))
    app.SharedParametersFilename = orig_sp

    # The plugin method itself, end to end (lookup + AddParameter), on a throwaway family.
    tpl = r"C:\ProgramData\Autodesk\RVT %s\Family Templates\English\Annotations\Metric Door Tag.rft" % app.VersionNumber
    fam = app.NewFamilyDocument(tpl)
    cmd_t = asm.GetType("StingTools.Tags.CreateTagFamiliesCommand")
    m = cmd_t.GetMethod("AddSharedParameters", BindingFlags.NonPublic | BindingFlags.Instance)
    inst = System.Activator.CreateInstance(cmd_t)
    lst = List[System.String]()
    for n in names:
        lst.Add(n)
    t0 = time.time()
    ok = m.Invoke(inst, System.Array[System.Object]([fam, sp_file, app, lst]))
    t_add = time.time() - t0
    n_params = fam.FamilyManager.Parameters.Size
    out("AddSharedParameters (plugin, end to end): %.2f s, returned %s, family now has %d parameters"
        % (t_add, ok, n_params))
except Exception:
    out("EXCEPTION:\n" + traceback.format_exc())
finally:
    try:
        if fam is not None:
            fam.Close(False)
    except Exception as ex:
        out("close: %s" % ex)
    try:
        app.SharedParametersFilename = orig_sp
    except Exception:
        pass
    out("end %s" % time.strftime("%H:%M:%S"))
