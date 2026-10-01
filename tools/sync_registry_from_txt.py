#!/usr/bin/env python3
"""
sync_registry_from_txt.py
Generate every "description" in PARAMETER_REGISTRY.json from MR_PARAMETERS.txt.

DSCH-47. The registry's descriptions were a second, hand-kept copy of the parameter
descriptions, and about 1,014 of them had drifted from the .txt. Two of them also
carried BEHAVIOUR: a warning_thresholds description was the text the plugin printed,
and its wording ("minimum" / "limit") decided which side of the threshold warned
(ParamRegistry.EvaluateWarning); a description beginning "DEPRECATED" hid a parameter
from pickers. Re-wording a tooltip could invert a check.

So the behaviour now lives in fields of its own, and the descriptions have one owner:

  * MR_PARAMETERS.txt owns the description. It is what Revit shows as the tooltip,
    and the CSV and binding views are generated from it. Every registry entry that
    names a parameter takes its "description" from here -- this script writes them.
    Better text found in the registry was moved INTO the .txt once (DSCH-47 part 2);
    after that, a description is changed in the .txt and regenerated, never edited
    in the registry.
  * warning_thresholds entries carry "direction" ("min" = warn below, "max" = warn
    above) and "message" (the printed text). The message is NOT the tooltip and is
    not generated: it is authored here, in the registry, because it is shown in tag
    labels where the tooltip's standard tags and type hints do not belong.
  * A deprecated parameter carries "deprecated": true and "replaced_by": <name>.

This script also FAILS (exit 1) on any of the following, which the plugin would
otherwise meet only at runtime:
  * a registry entry naming a parameter MR_PARAMETERS.txt does not define
  * a warning_thresholds entry without a direction of min / max, or without a message
    (ParamRegistry refuses such an entry and does not evaluate it)
  * "deprecated" that is not true/false, or a deprecated entry whose replaced_by is
    missing, undefined in the .txt, or itself deprecated

param-csv-drift.yml runs it and fails if the committed registry differs from what
it writes, so the two files cannot drift again.

Usage:  python tools/sync_registry_from_txt.py           # rewrite the registry
        python tools/sync_registry_from_txt.py --check   # write nothing; exit 1 on drift
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TXT = ROOT / 'StingTools/Data/MR_PARAMETERS.txt'
REG = ROOT / 'StingTools/Data/PARAMETER_REGISTRY.json'


def load_txt():
    out = {}
    for line in TXT.read_text(encoding='utf-8-sig').splitlines():
        if not line.startswith('PARAM\t'):
            continue
        f = line.split('\t')
        while len(f) < 9:
            f.append('')
        out[f[2]] = f[7]
    return out


def _no_dupes(pairs):
    keys = [k for k, _ in pairs]
    dup = {k for k in keys if keys.count(k) > 1}
    if dup:
        raise SystemExit('PARAMETER_REGISTRY.json: duplicate key(s) %s' % sorted(dup))
    return dict(pairs)


def entries(node, path='$'):
    """Every dict that names a parameter, with its JSON path."""
    if isinstance(node, dict):
        if 'param_name' in node:
            yield path, node
        for k, v in node.items():
            yield from entries(v, '%s.%s' % (path, k))
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from entries(v, '%s[%d]' % (path, i))


def main():
    check = '--check' in sys.argv[1:]
    txt = load_txt()
    raw = REG.read_text(encoding='utf-8-sig')
    reg = json.loads(raw, object_pairs_hook=_no_dupes)

    errors, changed = [], 0
    deprecated = {}
    for path, e in entries(reg):
        name = e.get('param_name')
        if 'description' in e:
            if name not in txt:
                errors.append('%s: %r is not defined in MR_PARAMETERS.txt, so it has no '
                              'description to take' % (path, name))
            elif e['description'] != txt[name]:
                e['description'] = txt[name]
                changed += 1
        if 'deprecated' in e:
            if not isinstance(e['deprecated'], bool):
                errors.append('%s: "deprecated" must be true or false' % path)
            elif e['deprecated']:
                deprecated[name] = (path, e.get('replaced_by'))

    for name, (path, by) in deprecated.items():
        if not by:
            errors.append('%s: %s is deprecated with no "replaced_by"' % (path, name))
        elif by not in txt:
            errors.append('%s: %s is replaced_by %r, which MR_PARAMETERS.txt does not define'
                          % (path, name, by))
        elif by in deprecated:
            errors.append('%s: %s is replaced_by %r, which is itself deprecated' % (path, name, by))

    for i, w in enumerate(reg.get('warning_thresholds') or []):
        where = 'warning_thresholds[%d] %s' % (i, w.get('param_name'))
        if w.get('direction') not in ('min', 'max'):
            errors.append('%s: "direction" must be "min" or "max", got %r' % (where, w.get('direction')))
        if not str(w.get('message') or '').strip():
            errors.append('%s: no "message" (the text the warning prints)' % where)

    if errors:
        print('PARAMETER_REGISTRY.json: %d problem(s):' % len(errors))
        for e in errors:
            print('  - ' + e)
        return 1

    out = json.dumps(reg, indent=2, ensure_ascii=True)
    current = raw.replace('\r\n', '\n')
    if out == current:
        print('PARAMETER_REGISTRY.json is current: every description matches MR_PARAMETERS.txt.')
        return 0
    if check:
        print('PARAMETER_REGISTRY.json is not what sync_registry_from_txt.py produces '
              '(%d description(s) differ from MR_PARAMETERS.txt). Change descriptions in '
              'MR_PARAMETERS.txt and run: python tools/sync_registry_from_txt.py' % changed)
        return 1
    # Pinned to LF, like the other generators: Path.write_text would translate to the
    # platform newline and a Windows run would look like a whole-file change.
    with open(REG, 'w', encoding='utf-8', newline='\n') as f:
        f.write(out)
    print('PARAMETER_REGISTRY.json: %d description(s) regenerated from MR_PARAMETERS.txt.' % changed)
    return 0


if __name__ == '__main__':
    sys.exit(main())
