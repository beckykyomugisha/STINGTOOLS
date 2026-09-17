#!/usr/bin/env python3
"""
Every parameter in a trimmed/kit shared-parameter file must agree with
MR_PARAMETERS.txt on BOTH GUID and TYPE.

WHY THIS EXISTS
---------------
On 2026-09-17 STING_Tag_Universal.rfa refused to load into a project with 12
"cannot be added ... because it conflicts with the existing name and type"
errors. Revit keys a shared parameter on its GUID; two files giving the same
GUID two different types is a load-blocking conflict, and nothing detected it
until a human hit it in the UI.

Two of the twelve came from docs/UNIVERSAL_TAG_MASTER_PARAMS.txt, which typed
ASS_CRITICALITY_RATING_NR as TEXT (MR says NUMBER) and ASS_CST_STALE_BOOL as
TEXT (MR says YESNO) -- same GUIDs. A family built from that kit could never
load into a project bound from MR_PARAMETERS.txt.

The fix for those two is a _DISP_TXT display mirror, which is the pattern the
other 24 numeric label rows already use. This gate stops the class returning.

Exit 0 = clean. Exit 1 = a kit file disagrees with MR.
"""
import io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MR   = os.path.join(ROOT, 'StingTools', 'Data', 'MR_PARAMETERS.txt')
KITS = [os.path.join(ROOT, 'docs', 'UNIVERSAL_TAG_MASTER_PARAMS.txt')]

# FAMILY_PARAMETER_BINDINGS.csv is read by BatchAddFamilyParamsCommand, which runs
# inside ProjectSetupCommand and MasterSetupCommand -- so a wrong DataType here
# becomes a real family parameter, and a load conflict, on any set-up project.
# It carries a large pre-existing disagreement with MR_PARAMETERS.txt. Fixing all
# of it is its own job (ROADMAP PARAMTYPE-3); this gate BASELINES it so it cannot
# grow, the same contract as docs/BINDING_SCOPE_BASELINE.json: may shrink, never grow.
FPB      = os.path.join(ROOT, 'StingTools', 'Data', 'FAMILY_PARAMETER_BINDINGS.csv')
FPB_BASELINE = 332


def load(path):
    """name -> (guid, type). Revit shared-param format is tab separated:
    PARAM <guid> <name> <datatype> <datacategory> <group> <visible> ..."""
    out = {}
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        for line in fh:
            if not line.startswith('PARAM\t'):
                continue
            f = line.rstrip('\n').split('\t')
            if len(f) > 3:
                out[f[2]] = (f[1].lower(), f[3])
    return out


def main():
    if not os.path.exists(MR):
        print('FAIL: MR_PARAMETERS.txt not found at %s' % MR)
        return 1
    mr = load(MR)
    if not mr:
        # An empty baseline would make every assertion below pass vacuously.
        print('FAIL: MR_PARAMETERS.txt parsed to zero parameters')
        return 1

    problems = []
    checked = 0
    for kit_path in KITS:
        if not os.path.exists(kit_path):
            print('FAIL: kit file not found: %s' % kit_path)
            return 1
        kit = load(kit_path)
        if not kit:
            print('FAIL: %s parsed to zero parameters' % os.path.basename(kit_path))
            return 1
        name = os.path.basename(kit_path)
        for p, (guid, typ) in sorted(kit.items()):
            checked += 1
            if p not in mr:
                problems.append('%s: %s is not in MR_PARAMETERS.txt' % (name, p))
                continue
            mguid, mtyp = mr[p]
            if guid != mguid:
                problems.append('%s: %s GUID %s != MR %s' % (name, p, guid, mguid))
            if typ != mtyp:
                problems.append(
                    '%s: %s type %s != MR %s%s'
                    % (name, p, typ, mtyp,
                       '  <-- SAME GUID + DIFFERENT TYPE = Revit load conflict'
                       if guid == mguid else ''))

    if problems:
        print('FAIL: %d shared-parameter disagreement(s) across %d checked:\n'
              % (len(problems), checked))
        for x in problems:
            print('  ' + x)
        print('\nA numeric or Yes/No parameter cannot be retyped to TEXT for a label.')
        print('Use a <SOURCE>_DISP_TXT display mirror instead -- see the 24 existing')
        print('mirrors in Data/FORMULAS_WITH_DEPENDENCIES.csv.')
        return 1

    # ── FAMILY_PARAMETER_BINDINGS.csv: baselined, must not grow ──
    import csv as _csv
    n_fpb = 0
    if os.path.exists(FPB):
        with io.open(FPB, encoding='utf-8', errors='replace') as fh:
            rows = [l for l in fh if not l.startswith('#')]
        seen = set()
        for row in _csv.DictReader(rows):
            name = (row.get('ParameterName') or '').strip()
            typ  = (row.get('DataType') or '').strip()
            if not name or not typ or name not in mr:
                continue
            if typ != mr[name][1]:
                seen.add(name)
        n_fpb = len(seen)
        if n_fpb > FPB_BASELINE:
            print('FAIL: FAMILY_PARAMETER_BINDINGS.csv now disagrees with '
                  'MR_PARAMETERS.txt on %d parameters (baseline %d).' % (n_fpb, FPB_BASELINE))
            print('Each is a same-GUID type conflict that BatchAddFamilyParams can bake into')
            print('a family and that Revit will refuse on load. Fix the new one, or lower the')
            print('baseline if you fixed others.')
            return 1

    print('OK - %d kit parameters agree with MR_PARAMETERS.txt on GUID and type.' % checked)
    print('OK - FAMILY_PARAMETER_BINDINGS.csv disagreements: %d (baseline %d, must not grow).'
          % (n_fpb, FPB_BASELINE))
    return 0


if __name__ == '__main__':
    sys.exit(main())
