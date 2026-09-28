#!/usr/bin/env python3
"""Bind every _TXT mirror param to the same categories as the param it mirrors.

WHY THIS EXISTS
tools/transform_mr_params.py adds a _TXT mirror beside every native-typed
schedulable param, so a tag label formula -- which can only reference TEXT --
has something current to read. Defining the mirror in MR_PARAMETERS.txt is not
enough: a shared parameter that is bound to no category is invisible in Revit.
The mirrors would exist in the shared parameter file, be referenced by
LABEL_DEFINITIONS.json, and resolve to nothing on every element.

The original Phase 188 branch propagated these bindings by hand and committed
the result. That copy is now three months old and CATEGORY_BINDINGS.csv has had
six commits since, so merging it would revert them. This script derives the
bindings instead, which means it can be re-run whenever the source bindings
change.

THE RULE
A mirror binds exactly where its source binds, with the same binding type. It is
a display copy of one value; binding it more widely would put an empty text
parameter on categories that never carry the number, and binding it more
narrowly would leave a tag label unable to read a value the element has.

Idempotent: a mirror row that already exists is left alone, and the version
header is replaced rather than stacked.

    python tools/bind_txt_mirrors.py            # write
    python tools/bind_txt_mirrors.py --check    # report only, exit 1 if stale
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TXT = ROOT / 'StingTools/Data/MR_PARAMETERS.txt'
CSV = ROOT / 'StingTools/Data/CATEGORY_BINDINGS.csv'

HEADER_PREFIX = '# v3.9 |'
MIRROR_HEADER_MARK = '_TXT mirror params to their source categories'


def mirror_sources():
    """{mirror name: source name} for every _TXT mirror in the parameter file.

    A mirror is recognised by its source existing: FOO_LENGTH_MM has mirror
    FOO_TXT, so the mapping is built by walking the file once and pairing each
    _TXT param with the non-_TXT param that shares its stem. The suffix table
    lives in transform_mr_params.py; rather than restate it -- a second copy of
    a rule that has already caused drift once in this workstream -- the pairing
    is read back out of the generated file itself.
    """
    import re
    names = []
    for line in TXT.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('PARAM\t'):
            parts = line.split('\t')
            if len(parts) > 3:
                names.append((parts[2], parts[3]))
    by_name = dict(names)
    out = {}
    for name, ptype in names:
        if not name.endswith('_TXT') or ptype != 'TEXT':
            continue
        stem = name[:-len('_TXT')]
        # The source is the param this mirror was generated from: same stem,
        # a native (non-TEXT) type, and present in the same file.
        for cand, ctype in names:
            if cand == name or ctype == 'TEXT':
                continue
            if cand.startswith(stem + '_') or cand == stem:
                out[name] = cand
                break
    return out, by_name


def main():
    check_only = '--check' in sys.argv
    mirrors, _all = mirror_sources()

    lines = CSV.read_text(encoding='utf-8-sig').splitlines(True)
    comments = [l for l in lines if l.startswith('#')]
    data = [l for l in lines if not l.startswith('#') and l.strip()]
    header, rows = data[0], data[1:]

    existing = set()
    by_param = {}
    for r in rows:
        parts = r.rstrip('\n').split(',')
        if len(parts) < 4:
            continue
        existing.add((parts[0], parts[1], parts[2]))
        by_param.setdefault(parts[0], []).append(parts)

    added = []
    unbound_source = []
    for mirror, source in sorted(mirrors.items()):
        src_rows = by_param.get(source)
        if not src_rows:
            unbound_source.append((mirror, source))
            continue
        for parts in src_rows:
            key = (mirror, parts[1], parts[2])
            if key in existing:
                continue
            existing.add(key)
            added.append('%s,%s,%s,%s\n' % (mirror, parts[1], parts[2], parts[3]))

    if check_only:
        if added:
            print('CATEGORY_BINDINGS.csv is STALE: %d mirror binding(s) missing.' % len(added))
            print('Run: python tools/bind_txt_mirrors.py')
            return 1
        print('mirror bindings up to date (%d mirrors, %d unbound sources)'
              % (len(mirrors), len(unbound_source)))
        return 0

    # The header states what the FILE contains, not what this run did, and carries
    # no date: a date changes the file on any day the script runs, so it would not be
    # idempotent -- the same mistake sync_csv_from_txt.py made. It is found by its
    # wording, not its "# v3.9 |" prefix, which another history line shares (the
    # prefix match deleted that line), and it is replaced where it stands.
    total_rows = sum(1 for m in mirrors if any(k[0] == m for k in existing))
    total_bindings = sum(1 for k in existing if k[0] in mirrors)
    new_header = ('%s %d rows binding %d _TXT mirror params to their source '
                  'categories (Phase 188; maintained by tools/bind_txt_mirrors.py)\n'
                  % (HEADER_PREFIX, total_bindings, total_rows))
    mine = [i for i, c in enumerate(comments) if MIRROR_HEADER_MARK in c]
    if mine:
        comments[mine[0]] = new_header
        for i in reversed(mine[1:]):
            del comments[i]
    else:
        comments.append(new_header)
    bom = '\ufeff' if CSV.read_bytes().startswith(b'\xef\xbb\xbf') else ''
    CSV.write_text(bom + ''.join(comments + [header] + rows + added), encoding='utf-8')

    print('mirrors found            : %d' % len(mirrors))
    print('binding rows added       : %d' % len(added))
    print('mirrors newly bound      : %d' % len({a.split(',')[0] for a in added}))
    if unbound_source:
        # Not an error: a source param bound to no category has nothing for its
        # mirror to copy. Reported so the count is never silently zero.
        print('mirrors whose SOURCE is unbound (skipped): %d' % len(unbound_source))
        for m, s in unbound_source[:5]:
            print('    %-42s <- %s' % (m, s))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
