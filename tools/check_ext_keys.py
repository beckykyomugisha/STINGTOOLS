#!/usr/bin/env python3
"""Every ParamRegistry.Ext("KEY") lookup resolves to a parameter some shared-parameter file defines.

WHY THIS EXISTS (DSCH-42)
-------------------------
ParamRegistry.Ext(key) maps a short key to a parameter name through _extendedParams
(code defaults in ParamRegistry.cs, overridden by PARAMETER_REGISTRY.json
"extended_params"). An unknown key returns "" with one log line, and a name that no
shared-parameter file defines is never bound. Both read as "no value" and write
nothing, with no error anywhere a user would see. When this gate was added:

    24 keys had no mapping at all (COBie type fields, TOKEN_LOCK, Excel-link columns,
       tag style ints) -- the batch tagger's token-lock check never fired;
    21 mapped names were defined in no shared-parameter file (fixture units, P-trap
       flag, pump duty, plumbing config on Project Information);
     5 TMV parameters likewise (DSCH-36).

The keys are ENUMERATED from the source, not listed here, so a new Ext("...") is
checked the moment it is written. Two rules:

  1. every Ext("KEY") literal in StingTools/**/*.cs (the property definitions in
     ParamRegistry.cs included) has a mapping, and the mapped name is a PARAM in one
     of the shipped shared-parameter .txt files;
  2. every _extendedParams default in ParamRegistry.cs maps to a defined name -- a
     mapping nothing can bind is a lookup waiting to be used.

Ext() called with anything but a string literal cannot be checked and is an error.

USAGE
    python tools/check_ext_keys.py              # report; non-zero on any violation
    python tools/check_ext_keys.py --self-test  # prove each rule fails when it should
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REGISTRY_CS = os.path.join(ROOT, 'StingTools', 'Core', 'ParamRegistry.cs')
REGISTRY_JSON = os.path.join(ROOT, 'StingTools', 'Data', 'PARAMETER_REGISTRY.json')
DATA = os.path.join(ROOT, 'StingTools', 'Data')

DEFAULT_RE = re.compile(r'_extendedParams\["([^"]+)"\]\s*=\s*"([^"]*)"')
EXT_LITERAL_RE = re.compile(r'(?<![\w.])(?:ParamRegistry\.)?Ext\("([^"]*)"\)')
EXT_ANY_RE = re.compile(r'(?<![\w.])(?:ParamRegistry\.)?Ext\(\s*(?!")([^)]*)\)')


def strip_comments(src):
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    return re.sub(r'//[^\n]*', '', src)


def load_defined():
    names = set()
    for f in glob.glob(os.path.join(DATA, '**', '*.txt'), recursive=True):
        with open(f, encoding='utf-8-sig', errors='replace') as fh:
            for line in fh:
                p = line.rstrip('\r\n').split('\t')
                if p[0] == 'PARAM' and len(p) > 2:
                    names.add(p[2])
    return names


def load_mapping(registry_cs_text, registry_json):
    defaults = dict(DEFAULT_RE.findall(strip_comments(registry_cs_text)))
    mapping = dict(defaults)
    ext = registry_json.get('extended_params') or {}
    if isinstance(ext, dict):
        for arr in ext.values():
            if isinstance(arr, list):
                for it in arr:
                    if isinstance(it, dict) and it.get('key') and it.get('param_name'):
                        mapping[it['key']] = it['param_name']
    return defaults, mapping


def scan_sources(sources):
    """sources: {path: text}. Returns ({key: [path:line, ...]}, [non-literal call sites])."""
    used, bad = {}, []
    for path, text in sources.items():
        clean = strip_comments(text)
        for i, line in enumerate(clean.split('\n'), 1):
            for m in EXT_LITERAL_RE.finditer(line):
                used.setdefault(m.group(1), []).append('%s:%d' % (path, i))
            if 'static string Ext(' in line:
                continue
            for m in EXT_ANY_RE.finditer(line):
                bad.append('%s:%d  Ext(%s)' % (path, i, m.group(1).strip()))
    return used, bad


def check(sources, registry_cs_text, registry_json, defined):
    errors = []
    defaults, mapping = load_mapping(registry_cs_text, registry_json)
    used, bad = scan_sources(sources)
    for site in bad:
        errors.append('non-literal Ext() call cannot be checked: ' + site)
    for key in sorted(used):
        where = ', '.join(used[key][:3]) + (' ...' if len(used[key]) > 3 else '')
        if key not in mapping:
            errors.append('Ext("%s") has no mapping -- it returns "" (%s)' % (key, where))
        elif mapping[key] not in defined:
            errors.append('Ext("%s") -> %s, which no shared-parameter file defines (%s)'
                          % (key, mapping[key], where))
    for key, name in sorted(defaults.items()):
        if key in used:
            continue  # already reported above if it is wrong
        if name not in defined:
            errors.append('_extendedParams["%s"] -> %s, which no shared-parameter file defines '
                          '(unused mapping: delete it, or define the parameter)' % (key, name))
    return errors, len(used)


def real_inputs():
    sources = {}
    for f in glob.glob(os.path.join(ROOT, 'StingTools', '**', '*.cs'), recursive=True):
        rel = os.path.relpath(f, ROOT).replace('\\', '/')
        if '/obj/' in rel or '/bin/' in rel:
            continue
        with open(f, encoding='utf-8', errors='replace') as fh:
            sources[rel] = fh.read()
    with open(REGISTRY_CS, encoding='utf-8') as fh:
        cs = fh.read()
    with open(REGISTRY_JSON, encoding='utf-8-sig') as fh:
        js = json.load(fh)
    return sources, cs, js, load_defined()


def self_test():
    sources, cs, js, defined = real_inputs()
    base, _ = check(sources, cs, js, defined)
    if base:
        print('self-test needs a clean tree; the real check already fails:')
        for e in base:
            print('  ' + e)
        return 1
    any_name = sorted(defined)[0]
    cases = {
        'unmapped key': ({**sources, 'X.cs': 'var a = ParamRegistry.Ext("NO_SUCH_KEY_DSCH42");'}, cs, js, defined),
        'mapped to an undefined name': ({**sources, 'X.cs': 'var a = ParamRegistry.Ext("DSCH42_K");'},
                                        cs + '\n_extendedParams["DSCH42_K"] = "NOT_A_PARAM_DSCH42";', js, defined),
        'unused default to an undefined name': (sources, cs + '\n_extendedParams["DSCH42_D"] = "NOT_A_PARAM_DSCH42";',
                                                js, defined),
        'non-literal call': ({**sources, 'X.cs': 'var a = ParamRegistry.Ext(someKey);'}, cs, js, defined),
        'name removed from the .txt files': (
            {**sources, 'X.cs': 'var a = ParamRegistry.Ext("DSCH42_OK");'},
            cs + '\n_extendedParams["DSCH42_OK"] = "%s";' % any_name, js, defined - {any_name}),
        'JSON override to an undefined name': (
            {**sources, 'X.cs': 'var a = ParamRegistry.Ext("DSCH42_OK");'},
            cs + '\n_extendedParams["DSCH42_OK"] = "%s";' % any_name,
            {**js, 'extended_params': {**(js.get('extended_params') or {}),
                                       'dsch42': [{'key': 'DSCH42_OK', 'param_name': 'NOT_A_PARAM_DSCH42'}]}},
            defined),
    }
    ok = True
    for label, args in cases.items():
        errs, _ = check(*args)
        caught = bool(errs)
        print('%s  %s' % ('caught ' if caught else 'MISSED ', label))
        ok &= caught
    errs, _ = check({**sources, 'X.cs': 'var a = ParamRegistry.Ext("DSCH42_OK");'},
                    cs + '\n_extendedParams["DSCH42_OK"] = "%s";' % any_name, js, defined)
    print('%s  valid mapping accepted' % ('ok     ' if not errs else 'WRONG  '))
    ok &= not errs
    print('OK - self-test: every rule fails when it should' if ok else 'FAIL - self-test')
    return 0 if ok else 1


def main():
    if '--self-test' in sys.argv:
        return self_test()
    errors, n = check(*real_inputs())
    if errors:
        print('FAIL - %d Ext() lookup problem(s) (%d keys used):' % (len(errors), n))
        for e in errors:
            print('  ' + e)
        print('\nAn unknown key returns "" and a name no shared-parameter file defines is never '
              'bound: either way the read finds nothing and the write does nothing, silently. '
              'Map the key to an existing parameter, define the parameter through the '
              'generators (StingTools/Data/MISSING_PARAMETERS.md), or delete the dead lookup.')
        return 1
    print('OK - %d Ext() keys used; every one resolves to a defined shared parameter, '
          'and every mapping default names one' % n)
    return 0


if __name__ == '__main__':
    sys.exit(main())
