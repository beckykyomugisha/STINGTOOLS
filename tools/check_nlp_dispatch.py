#!/usr/bin/env python3
"""
Every command tag the natural-language processor can hand to the dispatcher
must resolve to something. A tag that does not ends in the "STING - Unknown
Command" dialog: the user typed a sensible request, NLP matched it with high
confidence, and the plugin answered "could not be matched to a handler".

WHY A STATIC SCAN
  NlpDispatcher.Run tries WorkflowEngine.ResolveCommandPublic, then
  StingDockPanel.DispatchCommand -> StingCommandHandler.Execute, which tries
  CommandRegistry.TryHandle, its own switch, a set of StartsWith prefix
  routes, and finally WorkflowEngine.GetCommandInstance again. Only the first
  layer can be asked "do you know this tag?" at runtime without executing the
  command, so the old startup check (NLPEngine.ValidateIntentPatterns) asked
  that one layer and vouched for the rest with a hand-kept allowlist. Two of
  the tags on that allowlist had no handler at all - the check certified them.
  This scan reads all four layers from source instead.

WHAT COUNTS AS RESOLVABLE (and what deliberately does not)
  L1  `case "X":` inside WorkflowEngine.ResolveCommand only - not every switch
      in that 2,000-line file.
  L2  `case "X":` and `StartsWith("X")` prefix routes inside
      StingCommandHandler.Execute only - the file has dozens of unrelated
      switches whose labels ("Horizontal", "Left", ...) must not vouch for a tag.
  L3  `registry.Register("X", ...)` in UI/Modules/*CommandModule.cs.
  NOT the satellite panel handlers (Electrical / HVAC / Plumbing / LPS /
  Sustainability): NLP dispatches through the MAIN handler, and satellites
  fall through to it - never the other way round. A tag only a satellite
  knows is unknown to NLP.
  Matching is ordinal and case-sensitive, like the C# switches: "CobieExport"
  does not reach `case "COBieExport":`.

WHAT IS EMITTED
  IntentPatterns tuples (the Browse list and every typed query), the Quick
  Commands list, and the Suggestions tuples.

Usage:
  python tools/check_nlp_dispatch.py          # report
  python tools/check_nlp_dispatch.py --check  # CI gate: exit 1 on any dead tag
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NLP = 'StingTools/Tags/NLPCommandProcessor.cs'
ENGINE = 'StingTools/Core/WorkflowEngine.cs'
HANDLER = 'StingTools/UI/StingCommandHandler.cs'
MODULES = 'StingTools/UI/Modules'


def read(rel):
    with io.open(os.path.join(ROOT, rel), encoding='utf-8-sig') as fh:
        return fh.read()


def method_body(text, signature_regex, what):
    """Return the brace-balanced body of the first method whose signature matches."""
    m = re.search(signature_regex, text)
    if not m:
        sys.exit('check_nlp_dispatch: could not find %s - has it been renamed?' % what)
    i = text.index('{', m.end())
    depth, j = 0, i
    in_str = in_chr = in_line = in_block = verbatim = False
    while j < len(text):
        c = text[j]
        nxt = text[j + 1] if j + 1 < len(text) else ''
        if in_line:
            if c == '\n':
                in_line = False
        elif in_block:
            if c == '*' and nxt == '/':
                in_block = False
                j += 1
        elif in_str:
            if verbatim:
                if c == '"' and nxt == '"':
                    j += 1
                elif c == '"':
                    in_str = False
            elif c == '\\':
                j += 1
            elif c == '"':
                in_str = False
        elif in_chr:
            if c == '\\':
                j += 1
            elif c == "'":
                in_chr = False
        else:
            if c == '/' and nxt == '/':
                in_line = True
            elif c == '/' and nxt == '*':
                in_block = True
            elif c == '"':
                # @"..." and $@"..." / @$"..." are verbatim: "" escapes a quote,
                # a backslash does not.
                prefix = text[max(0, j - 2):j]
                in_str, verbatim = True, '@' in prefix and prefix.strip('$@') == ''
                verbatim = verbatim or text[j - 1] == '@'
            elif c == "'":
                in_chr = True
            elif c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return text[i:j + 1]
        j += 1
    sys.exit('check_nlp_dispatch: unbalanced braces in %s' % what)


def emitted_tags(nlp):
    """tag -> list of 'source:line' where NLP can hand it to the dispatcher."""
    out = {}

    def add(tag, src, pos):
        line = nlp.count('\n', 0, pos) + 1
        out.setdefault(tag, []).append('%s:%d' % (src, line))

    # IntentPatterns: (@"regex", "Tag", "Intent", "Description")
    for m in re.finditer(r'\(\s*@"(?:[^"]|"")*"\s*,\s*"([^"]+)"\s*,\s*"[^"]*"\s*,', nlp):
        add(m.group(1), 'intent', m.start())
    # Quick Commands: new StingListPicker.ListItem { Label = "Tag", ... } inside quickItems
    q = re.search(r'var\s+quickItems\s*=\s*new\s+List<[^>]+>\s*\{(.*?)\};', nlp, re.S)
    if q:
        for m in re.finditer(r'Label\s*=\s*"([^"]+)"', q.group(1)):
            add(m.group(1), 'quick', q.start(1) + m.start())
    # Suggestions: suggestions.Add(("PRIORITY", "Label", "Tag", ...))
    for m in re.finditer(r'suggestions\.Add\(\(\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([^"]+)"', nlp):
        add(m.group(1), 'suggest', m.start())
    return out


def resolvable():
    engine = method_body(read(ENGINE), r'static\s+IExternalCommand\s+ResolveCommand\s*\(\s*string\s+\w+\s*\)',
                         'WorkflowEngine.ResolveCommand')
    handler = method_body(read(HANDLER), r'public\s+void\s+Execute\s*\(\s*UIApplication\s+\w+\s*\)',
                          'StingCommandHandler.Execute')
    # A ResolveCommand case whose body only throws ("...is an inline dispatch
    # handler...") resolves nothing by itself: NlpDispatcher catches the throw
    # and falls through to the handler, so such a tag is live only if the
    # handler (or a registry module) also knows it.
    throwing = set(re.findall(r'case\s+"([^"]+)"\s*:\s*throw\b', engine))
    exact = set(re.findall(r'case\s+"([^"]+)"\s*:', engine)) - throwing
    exact |= set(re.findall(r'case\s+"([^"]+)"\s*:', handler))
    for name in sorted(os.listdir(os.path.join(ROOT, MODULES))):
        if name.endswith('CommandModule.cs'):
            exact |= set(re.findall(r'registry\.Register\(\s*"([^"]+)"', read(MODULES + '/' + name)))
    prefixes = set(re.findall(r'\.StartsWith\(\s*"([^"]+)"\s*\)', handler))
    return exact, prefixes


def main():
    check = '--check' in sys.argv
    exact, prefixes = resolvable()

    # Instrument self-test: a probe that sees nothing reports "all clear".
    for ok in ('AutoTag', 'ValidateTags'):
        if ok not in exact:
            sys.exit('check_nlp_dispatch: control %r not found in the resolvable set - the scan is broken' % ok)
    if 'ZZ_NotACommand' in exact:
        sys.exit('check_nlp_dispatch: negative control resolved - the scan is broken')

    tags = emitted_tags(read(NLP))
    if len(tags) < 100:
        sys.exit('check_nlp_dispatch: only %d emitted tags parsed - the NLP file format changed' % len(tags))

    def resolves(t):
        return t in exact or any(t.startswith(p) for p in prefixes)

    dead = sorted(t for t in tags if not resolves(t))
    print('NLP-emitted tags          : %d' % len(tags))
    print('resolvable names          : %d exact + %d prefix routes' % (len(exact), len(prefixes)))
    print('dead (Unknown Command)    : %d' % len(dead))
    for t in dead:
        print('  %-40s %s' % (t, ', '.join(tags[t][:3])))
    if dead and check:
        print()
        print('Every tag above would show "STING - Unknown Command" when NLP picks it.')
        print('Point the intent at the real tag (a case in WorkflowEngine.ResolveCommand or')
        print('StingCommandHandler.Execute, or a CommandRegistry module entry), or delete it.')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
