# -*- coding: utf-8 -*-
# TAGFAM-6 profile, headless:  pyrevit run tools/pyrevit/headless/profile_add_params.py --revit=2025 --purge
# Where does adding ~160 shared parameters to a tag family spend its time? Times, on fresh
# throwaway families from the Metric Door Tag template (nothing saved):
#   A  fm.AddParameter only, one transaction
#   B  A + a full fm.Parameters scan before each add (what TagParamInjector.EnsureFamilyParam does)
#   C  A with the SAME parameters added in chunks of 20 per transaction
# Log: %TEMP%\sting_tagfam6_profile.log
import io, os, time, traceback
import System
from Autodesk.Revit.DB import Transaction, GroupTypeId

LOG = os.path.join(os.environ.get("TEMP", "."), "sting_tagfam6_profile.log")
lines = []
def out(m):
    lines.append(u"%s" % m)
    with io.open(LOG, "w", encoding="utf-8") as f:
        f.write(u"\n".join(lines) + u"\n")

app = __revit__.Application
orig_sp = app.SharedParametersFilename
here = os.path.dirname(os.path.abspath(__file__))
dll = os.environ.get("STING_DLL") or os.path.normpath(os.path.join(
    here, "..", "..", "..", "StingTools.Revit.SmokeTests", "bin", "Debug", "net8.0-windows", "StingTools.dll"))
tpl = r"C:\ProgramData\Autodesk\RVT %s\Family Templates\English\Annotations\Metric Door Tag.rft" % app.VersionNumber
out("start %s" % time.strftime("%H:%M:%S"))
try:
    asm = System.Reflection.Assembly.LoadFrom(dll)
    sp_file = os.path.join(os.path.dirname(dll), "data", "MR_PARAMETERS.txt")
    cfg = asm.GetType("StingTools.Tags.TagFamilyConfig")
    names = list(cfg.GetMethod("GetAllFamilyParams").Invoke(None, System.Array[System.Object](["Doors", "STING - Door Tag"])))
    app.SharedParametersFilename = sp_file
    df = app.OpenSharedParameterFile()
    idx = {}
    for g in df.Groups:
        for d in g.Definitions:
            if d.Name not in idx:
                idx[d.Name] = d
    defs = [idx[n] for n in names if n in idx]
    out("parameters: %d" % len(defs))

    def run(label, scan, chunk):
        fam = app.NewFamilyDocument(tpl)
        fm = fam.FamilyManager
        t_scan = 0.0; t_add = 0.0; slow = []
        t0 = time.time()
        i = 0
        while i < len(defs):
            part = defs[i:i + chunk]
            tx = Transaction(fam, "profile")
            tx.Start()
            for d in part:
                if scan:
                    s0 = time.time()
                    for p in fm.Parameters:
                        if p.Definition.Name == d.Name:
                            break
                    t_scan += time.time() - s0
                a0 = time.time()
                fm.AddParameter(d, GroupTypeId.General, True)
                dt = time.time() - a0
                t_add += dt
                slow.append((dt, d.Name))
            c0 = time.time()
            tx.Commit()
            t_add += time.time() - c0
            i += chunk
        total = time.time() - t0
        slow.sort(reverse=True)
        out("%s: total %.1f s | AddParameter+commit %.1f s | scans %.1f s | slowest: %s"
            % (label, total, t_add, t_scan, ", ".join("%s %.2fs" % (n, s) for s, n in slow[:3])))
        # growth: time of first vs last 20 adds
        fam.Close(False)

    run("A add only, 1 tx", False, 10000)
    run("B add + scan each, 1 tx", True, 10000)
    run("C add only, tx per 20", False, 20)
except Exception:
    out("EXCEPTION:\n" + traceback.format_exc())
finally:
    try:
        app.SharedParametersFilename = orig_sp
    except Exception:
        pass
    out("end %s" % time.strftime("%H:%M:%S"))
