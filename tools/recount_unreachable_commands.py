#!/usr/bin/env python3
"""Re-derive how many IExternalCommand classes no dispatch layer can reach. WF-7.

WHY THIS EXISTS
---------------
docs/UNREACHABLE_COMMANDS_TRIAGE.md carried "126 not in dock-panel dispatcher",
measured against the StingCommandHandler switch ALONE. Dispatch is not one layer:

    1. handler switches          StingTools/UI/*CommandHandler.cs
    2. CommandRegistry modules   StingTools/UI/Modules/*CommandModule.cs, consulted
                                 by the handler BEFORE its own switch
    3. panel code-behind runners StingTools/UI/**/*.xaml.cs Cmd_Click suites
    4. WorkflowEngine.ResolveCommand   reachable from a preset, with no button
    5. the legacy ribbon         typeof(X).FullName passed to PushButtonData

A count against layer 1 only over-reports, and every over-report in this file is
an invitation to delete live code. The button-side audit (SILENT_BUTTONS_TODO.md)
was corrected the same way and fell to zero.

WHAT "REACHABLE" MEANS HERE, EXACTLY
------------------------------------
This is a NAME-REFERENCE analysis, not a call graph. Three buckets, and the
distinction between the second and third is the whole point:

    DISPATCHED   the class name appears in a dispatch-layer file (1-5 above), or
                 in a .addin / .xaml / shipped data file.
    REFERENCED   the name appears in some other .cs file - another command chains
                 to it, a wizard constructs it, a test names it. Reachable ONLY IF
                 that referrer is itself reachable, which this script does not
                 prove. Needs a human read; it is NOT evidence of death.
    ORPHANED     nothing anywhere outside its own declaration file names it. This
                 is the only bucket that can be called unreachable from static
                 evidence alone.

Deliberately NOT claimed: that an ORPHANED command is dead. A command can still
be constructed reflectively. What the bucket does say is that no dispatcher, no
markup and no data file names it - which is the question the triage doc asks.

Usage:  python tools/recount_unreachable_commands.py [--check]
        --check exits 1 if the doc's headline numbers disagree with the code.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'StingTools')
DOC = os.path.join(ROOT, 'docs', 'UNREACHABLE_COMMANDS_TRIAGE.md')


def is_dispatch(rel):
    """Which dispatch layer this file is, or None.

    Matched on FILENAME SUFFIX, never on directory. An earlier version of this
    keyed handlers on '/UI/<name>CommandHandler.cs' and so missed
    UI/Plumbing/StingPlumbingCommandHandler.cs and
    UI/Sustainability/StingSustainabilityCommandHandler.cs - 2 of the 6 - which
    reported 33 plumbing commands as unreachable. That is precisely the mistake
    this script exists to correct, so the rule is now one a new subdirectory
    cannot break, and audit_layers() below re-checks it against the tree.
    """
    r = rel.replace('\\', '/')
    if r.endswith('CommandHandler.cs'):
        return 'handler'
    if r.endswith('CommandModule.cs') or r.endswith('CommandRegistry.cs'):
        return 'registry-module'
    if r.endswith('.xaml.cs'):
        return 'code-behind'
    if r.endswith('/Core/WorkflowEngine.cs'):
        return 'workflow'
    if r.endswith('/Core/StingToolsApp.cs'):
        return 'ribbon'
    return None


def usage_re(name):
    """Does this file USE the class, as opposed to merely declaring it?

    Needed because a dispatcher can declare the command it dispatches:
    StingToolsApp.cs holds 13 Hub*Command classes AND the ribbon call
    typeof(HubAutoTagCommand).FullName that reaches them. Skipping the
    declaring file wholesale reported all 13 as orphaned.
    """
    n = re.escape(name)
    return re.compile(
        r'typeof\s*\(\s*(?:[\w.]+\.)?' + n + r'\s*\)'
        r'|new\s+(?:[\w.]+\.)?' + n + r'\s*\('
        r'|<\s*(?:[\w.]+\.)?' + n + r'\s*>'
        r'|"[\w.]*\b' + n + r'"')


NON_CS_ROOTS = ('.addin', '.xaml', '.json', '.csv', '.txt')

CLASS_RE = re.compile(r'\bclass\s+([A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*:\s*([^{]*)')
IDENT_RE = re.compile(r'\b([A-Za-z_]\w*)\b')
COMMENT_RE = re.compile(r'//[^\n]*|/\*.*?\*/', re.S)


def strip_comments(t):
    """Blank out C# comments so a mention in prose is not read as a call.

    This matters more than it sounds. Before it, the only two commands that
    looked "referenced but not dispatched" were both named in COMMENTS - one in
    a note about a validation hook, one in a numbered list of features - and a
    reader would have taken them for live call sites.

    Crude on purpose: a "//" inside a string literal (a URL) is cut too. That
    can only remove text, never invent a class name, and no command name lives
    inside a URL. String literals are otherwise kept, because the ribbon names
    commands as strings.
    """
    return COMMENT_RE.sub(' ', t)


def cs_files():
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in filenames:
            if fn.endswith('.cs') and not fn.endswith('.g.cs'):
                yield os.path.join(dirpath, fn)


def other_files():
    skip = ('obj', 'bin', '.git', 'node_modules', 'CompiledPlugin')
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in skip]
        for fn in filenames:
            if fn.endswith(NON_CS_ROOTS):
                yield os.path.join(dirpath, fn)


def read(path):
    with open(path, encoding='utf-8', errors='replace') as fh:
        return fh.read()


def main():
    check = '--check' in sys.argv

    # 1. every IExternalCommand class, and where it is declared
    sites = {}          # name -> [declaring file, ...]
    texts = {}
    for path in cs_files():
        rel = os.path.relpath(path, ROOT).replace('\\', '/')
        t = read(path)
        texts[rel] = t
        for m in CLASS_RE.finditer(t):
            if re.search(r'\bIExternalCommand\b', m.group(2)):
                sites.setdefault(m.group(1), []).append(rel)

    # A name declared twice cannot be resolved by a name-reference scan: which
    # class a `RunCommand<ClashDetectionCommand>` binds depends on the file's
    # `using` directives, not on anything visible here. Those are reported as
    # AMBIGUOUS rather than silently folded into one - folding them is how a
    # dead twin hides behind a live one.
    ambiguous = {n: f for n, f in sites.items() if len(f) > 1}
    declared = {n: f[0] for n, f in sites.items() if len(f) == 1}
    n_classes = sum(len(f) for f in sites.values())

    # INSTRUMENT CHECK. A regex that stops matching reports every command as
    # orphaned, which is a broken script, not a finding.
    if len(declared) < 800:
        sys.stderr.write(
            'FAILED: only %d IExternalCommand classes parsed; the reader is wrong,\n'
            'not the codebase. Expected well over 1,000.\n' % len(declared))
        return 2

    names = set(declared)

    # INSTRUMENT CHECK on the layer rule itself. Every file the tree calls a
    # handler, module or registry must be classified as one. This is the guard
    # that the directory-keyed version lacked, and it fails loudly rather than
    # quietly shrinking the dispatch set and inflating the finding.
    layers_found = {}
    for rel in texts:
        l = is_dispatch(rel)
        if l:
            layers_found.setdefault(l, []).append(rel)
    missed = [rel for rel in texts
              if re.search(r'(CommandHandler|CommandModule|CommandRegistry)\.cs$', rel)
              and not is_dispatch(rel)]
    if missed:
        sys.stderr.write('FAILED: dispatch files not classified as a layer:\n')
        for rel in missed:
            sys.stderr.write('  ' + rel + '\n')
        return 2
    if len(layers_found.get('handler', [])) < 5:
        sys.stderr.write(
            'FAILED: only %d command handlers found; the layer rule is wrong.\n'
            % len(layers_found.get('handler', [])))
        return 2

    # 2. where each name is referenced. One pass per file over its identifiers,
    #    not one regex per class over every file.
    dispatched = {}
    referenced = {}
    comment_only = {}
    for rel, t in texts.items():
        layer = is_dispatch(rel)
        code = strip_comments(t)
        code_names = set(IDENT_RE.findall(code)) & names
        for name in set(IDENT_RE.findall(t)) & names:
            in_code = name in code_names
            if rel == declared[name]:
                # The declaring file counts only if it USES the class as well.
                if layer and in_code and usage_re(name).search(code):
                    dispatched.setdefault(name, set()).add(layer)
                continue
            if not in_code:
                comment_only.setdefault(name, set()).add(rel)
            elif layer:
                dispatched.setdefault(name, set()).add(layer)
            else:
                referenced.setdefault(name, set()).add(rel)

    for path in other_files():
        try:
            t = read(path)
        except OSError:
            continue
        for name in set(IDENT_RE.findall(t)) & names:
            dispatched.setdefault(name, set()).add('data/xaml/addin')

    total = n_classes
    n_dispatched = len(dispatched)
    only_ref = sorted(n for n in referenced if n not in dispatched)
    orphaned = sorted(n for n in declared
                      if n not in dispatched and n not in referenced)

    # The four buckets partition every declared class. If they stop adding up,
    # something is being counted twice or dropped, and every number above is
    # suspect - so say so instead of printing a plausible-looking total.
    partition = (n_dispatched + len(only_ref) + len(orphaned)
                 + sum(len(f) for f in ambiguous.values()))
    if partition != n_classes:
        sys.stderr.write(
            'FAILED: buckets sum to %d but %d classes are declared. The\n'
            'classification is losing or double-counting commands.\n'
            % (partition, n_classes))
        return 2

    by_layer = {}
    for layers in dispatched.values():
        for l in layers:
            by_layer[l] = by_layer.get(l, 0) + 1

    print('IExternalCommand classes declared      : %d' % total)
    print('  ... under %d distinct names, %d of which are declared twice'
          % (len(sites), len(ambiguous)))
    print('Reached by a dispatch layer            : %d' % n_dispatched)
    for l in sorted(by_layer):
        print('    via %-22s : %d' % (l, by_layer[l]))
    print('    (layers overlap; a command reached by two counts once above)')
    print('Referenced only from non-dispatch code : %d' % len(only_ref))
    print('Named nowhere outside their own file   : %d' % len(orphaned))
    print('Ambiguous - name declared twice        : %d classes under %d names'
          % (sum(len(f) for f in ambiguous.values()), len(ambiguous)))
    print('')

    if ambiguous:
        print('AMBIGUOUS (a bare-name reference cannot say which one it binds):')
        for n in sorted(ambiguous):
            print('  %s' % n)
            for f in ambiguous[n]:
                print('      %s' % f)
        print('')

    if only_ref:
        print('REFERENCED-ONLY (reachable only if the referrer is; needs a read):')
        for n in only_ref:
            src = sorted(referenced[n])
            more = '' if len(src) <= 2 else ' (+%d more)' % (len(src) - 2)
            print('  %-50s <- %s%s' % (n, ', '.join(src[:2]), more))
        print('')
    if orphaned:
        print('ORPHANED (no reference anywhere, including XAML/JSON/.addin):')
        for n in orphaned:
            note = ''
            if n in comment_only:
                c = sorted(comment_only[n])
                note = '   [named only in a COMMENT in %s]' % ', '.join(c[:2])
            print('  %-50s in %s%s' % (n, declared[n], note))
        print('')

    if check:
        doc = read(DOC)
        want = [
            ('**Total IExternalCommand classes**', total),
            ('**Reached by a dispatch layer**', n_dispatched),
            ('**Referenced only from non-dispatch code**', len(only_ref)),
            ('**Named nowhere outside their own file**', len(orphaned)),
            ('**Ambiguous — name declared twice**',
             sum(len(f) for f in ambiguous.values())),
        ]
        bad = []
        for label, val in want:
            m = re.search(re.escape(label) + r'[^\n]*?\*\*([\d,]+)\*\*', doc)
            if not m:
                bad.append('%s: not found in the doc' % label)
            elif int(m.group(1).replace(',', '')) != val:
                bad.append('%s: doc says %s, code says %d'
                           % (label, m.group(1), val))
        if bad:
            sys.stderr.write('UNREACHABLE-COMMANDS DOC IS STALE:\n')
            for b in bad:
                sys.stderr.write('  ' + b + '\n')
            sys.stderr.write(
                '\nRe-run tools/recount_unreachable_commands.py and update\n'
                'docs/UNREACHABLE_COMMANDS_TRIAGE.md. A count in that file drives\n'
                'deletions, so a stale one is an invitation to delete live code.\n')
            return 1
        print('Doc headline numbers agree with the code.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
