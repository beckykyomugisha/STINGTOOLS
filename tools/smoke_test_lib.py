"""Shared parsing for the smoke-test source pipeline.

`tools/build_smoke_test.py` renders `smoke_test.json` into a checklist;
`tools/check_smoke_test.py` validates the same source against the codebase.
Both need the same view of the repo, and a second copy of that parsing is
exactly how the three hand-maintained checklists drifted apart in the first
place — so it lives here once.

Nothing in this module is owner-specific. It is driven entirely by
`docs/examples/<OWNER_CODE>/smoke_test.json`.

COMMAND REACHABILITY IS FOUR-LAYERED. A check that consults one layer
over-reports catastrophically — the repo's "141 silent buttons" figure was
~96% false-positive for exactly that reason (see SILENT_BUTTONS_TODO.md and
the Tier 4 doc comment in tools/check_workflow_wiring.ps1). The four layers:

    L1  CommandRegistry   StingTools/UI/Modules/*CommandModule.cs
                          registry.Register("X", ...)
    L2  Cmd_Click runners  each panel's .xaml.cs Cmd_Click body
                          cmdTag == "X"
    L3  handler cases     the six *CommandHandler.cs files
                          case "X":
    L4  workflow-only     StingTools/Core/WorkflowEngine.cs
                          case "X": return new ...

A step reachable only via L4 is legal, but it must declare `reach: "workflow"`
and name the preset — a checklist that tells a tester to click a button that
does not exist wastes a Revit session.
"""

from __future__ import annotations

import hashlib
import json
import re
import zipfile
from pathlib import Path

# The six dockable panels. Keep in step with the $panels list in
# tools/check_workflow_wiring.ps1 — same set, same reason.
PANELS = {
    "STING":          ("StingTools/UI/StingDockPanel.xaml",                          "StingTools/UI/StingDockPanel.xaml.cs"),
    "ELECTRICAL":     ("StingTools/UI/StingElectricalPanel.xaml",                    "StingTools/UI/StingElectricalPanel.xaml.cs"),
    "HVAC":           ("StingTools/UI/StingHvacPanel.xaml",                          "StingTools/UI/StingHvacPanel.xaml.cs"),
    "PLUMBING":       ("StingTools/UI/Plumbing/StingPlumbingPanel.xaml",             "StingTools/UI/Plumbing/StingPlumbingPanel.xaml.cs"),
    "LPS":            ("StingTools/UI/StingLpsPanel.xaml",                           "StingTools/UI/StingLpsPanel.xaml.cs"),
    "SUSTAINABILITY": ("StingTools/UI/Sustainability/StingSustainabilityPanel.xaml", "StingTools/UI/Sustainability/StingSustainabilityPanel.xaml.cs"),
}

HANDLERS = [
    "StingTools/UI/StingCommandHandler.cs",
    "StingTools/UI/StingElectricalCommandHandler.cs",
    "StingTools/UI/StingHvacCommandHandler.cs",
    "StingTools/UI/Plumbing/StingPlumbingCommandHandler.cs",
    "StingTools/UI/StingLpsCommandHandler.cs",
    "StingTools/UI/Sustainability/StingSustainabilityCommandHandler.cs",
]

WORKFLOW_ENGINE = "StingTools/Core/WorkflowEngine.cs"
MODULES_DIR = "StingTools/UI/Modules"
DATA_DIR = "StingTools/Data"
REGISTRY_JSON = "StingTools/Data/PARAMETER_REGISTRY.json"
RESOLVED_BINDINGS = "StingTools/Data/RESOLVED_BINDINGS.csv"

_TAG_RX = re.compile(r'Tag="([^"]+)"')
_CONTENT_RX = re.compile(r'Content="([^"]*)"')
_STYLE_RX = re.compile(r'Style="\{StaticResource ([^}"]+)\}"')
_TEXT_RX = re.compile(r'Text="([^"]*)"')


def read(root: Path, rel: str) -> str:
    return (root / rel).read_text(encoding="utf-8-sig", errors="replace")


def _decode_xml_entities(s: str) -> str:
    return (s.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">")
             .replace("&quot;", '"').replace("&apos;", "'").replace("&#xA;", " "))


class PanelButton:
    __slots__ = ("panel", "tab", "section", "label", "tag", "line")

    def __init__(self, panel, tab, section, label, tag, line):
        self.panel, self.tab, self.section = panel, tab, section
        self.label, self.tag, self.line = label, tag, line

    def as_dict(self):
        return {"panel": self.panel, "tab": self.tab, "panelSection": self.section,
                "button": self.label, "commandTag": self.tag, "line": self.line}


_COMMENT_RX = re.compile(r"<!--.*?-->", re.S)


def _blank_comments(xaml: str) -> str:
    """Blank out XML comments, preserving newlines so line numbers stay true.

    Not cosmetic: StingDockPanel.xaml carries a comment reading
    `Do NOT add a top-level <TabItem Header="HVAC"> here`, and a scan that reads
    it pins every button in the main panel to a tab that does not exist.
    """
    return _COMMENT_RX.sub(lambda m: "\n" * m.group(0).count("\n"), xaml)


def parse_panel_buttons(root: Path) -> dict[str, list[PanelButton]]:
    """Every `<Button Tag="X" Click="Cmd_Click">` in every panel, with the
    top-level tab and the section heading it sits under.

    `section` is the nearest preceding heading, which is either a TextBlock
    carrying that panel's heading style (`SectionLabel` / `SubLabel` in the main
    panel, `ElecLabel` / `HvacLabel` / `PlumbLabel` / `LpsLabel` / `SusLabel` in
    the satellites — all end in "Label") or an `<Expander Header="...">`, since
    the panels group overflow buttons inside expanders. Prose TextBlocks are
    deliberately NOT treated as headings: they describe a group, they do not
    name it, and a checklist that told a tester to look under "Element maturity
    vs the milestone LOD matrix (param/naming/geometry proxy)" would be useless.
    """
    out: dict[str, list[PanelButton]] = {}
    for panel, (xaml_rel, _cb_rel) in PANELS.items():
        xaml = _blank_comments(read(root, xaml_rel))
        rows: list[PanelButton] = []
        tab_stack: list[str] = []
        section = ""
        for m in re.finditer(r"</?\s*(TabItem|Button|TextBlock|Expander)\b[^>]*?>", xaml, re.S):
            el, name = m.group(0), m.group(1)
            line = xaml.count("\n", 0, m.start()) + 1
            closing = el.startswith("</")
            self_closing = el.rstrip().endswith("/>")

            if name == "TabItem":
                if closing:
                    if tab_stack:
                        tab_stack.pop()
                    continue
                header = re.search(r'Header="([^"]*)"', el)
                if not self_closing:
                    tab_stack.append(_decode_xml_entities(header.group(1)) if header else "")
                    section = ""
                continue

            if name == "Expander":
                if closing:
                    continue
                header = re.search(r'Header="([^"]*)"', el)
                if header:
                    section = _decode_xml_entities(header.group(1)).strip()
                continue

            if name == "TextBlock":
                if closing:
                    continue
                style = _STYLE_RX.search(el)
                text = _TEXT_RX.search(el)
                if not text or not style or not style.group(1).endswith("Label"):
                    continue
                section = _decode_xml_entities(text.group(1)).strip()
                continue

            # Button
            if closing or "Cmd_Click" not in el:
                continue
            tag = _TAG_RX.search(el)
            if not tag:
                continue
            content = _CONTENT_RX.search(el)
            rows.append(PanelButton(
                panel=panel,
                tab=tab_stack[0] if tab_stack else "",
                section=section,
                label=_decode_xml_entities(content.group(1)).strip() if content else "",
                tag=tag.group(1),
                line=line,
            ))
        out[panel] = rows
    return out


def dispatch_layers(root: Path) -> dict[str, set[str]]:
    """The four reachability layers, each as a set of command names."""
    l1: set[str] = set()
    mod_dir = root / MODULES_DIR
    if mod_dir.is_dir():
        for f in sorted(mod_dir.glob("*CommandModule.cs")):
            l1 |= set(re.findall(r'registry\.Register\(\s*"([^"]+)"', f.read_text(encoding="utf-8-sig", errors="replace")))

    l2: set[str] = set()
    for _panel, (_xaml_rel, cb_rel) in PANELS.items():
        p = root / cb_rel
        if not p.is_file():
            continue
        cb = p.read_text(encoding="utf-8-sig", errors="replace")
        i = cb.find("private void Cmd_Click")
        if i < 0:
            continue
        j = cb.find("\n        private ", i + 10)
        body = cb[i: j if j > 0 else len(cb)]
        l2 |= set(re.findall(r'cmdTag\s*==\s*"([^"]+)"', body))

    l3: set[str] = set()
    for rel in HANDLERS:
        p = root / rel
        if p.is_file():
            l3 |= set(re.findall(r'case\s+"([^"]+)"\s*:', p.read_text(encoding="utf-8-sig", errors="replace")))

    l4 = set(re.findall(r'case\s+"([^"]+)"\s*:', read(root, WORKFLOW_ENGINE)))
    return {"L1": l1, "L2": l2, "L3": l3, "L4": l4}


def resolve_tag(layers: dict[str, set[str]], tag: str) -> list[str]:
    """Which of L1-L4 can reach `tag`. Empty means unreachable."""
    return [k for k in ("L1", "L2", "L3", "L4") if tag in layers[k]]


def workflow_presets(root: Path) -> dict[str, dict]:
    """Every WORKFLOW_*.json in StingTools/Data, by file name."""
    out = {}
    for p in sorted((root / DATA_DIR).glob("WORKFLOW_*.json")):
        try:
            out[p.name] = json.loads(p.read_text(encoding="utf-8-sig"))
        except Exception as ex:               # a broken preset is a finding, not a crash
            out[p.name] = {"__parse_error__": str(ex)}
    return out


def registry_params(root: Path) -> dict[str, dict]:
    """Every parameter in PARAMETER_REGISTRY.json, by name.

    The registry nests parameters under several keys (`source_tokens`,
    `support_params`, `extended_params`, ...), so this walks the whole document
    for objects carrying a `param_name`, rather than hard-coding the sections.
    """
    doc = json.loads((root / REGISTRY_JSON).read_text(encoding="utf-8-sig"))
    out: dict[str, dict] = {}

    def walk(node):
        if isinstance(node, dict):
            name = node.get("param_name") or node.get("name")
            if isinstance(name, str) and re.fullmatch(r"[A-Z][A-Z0-9_]{2,}", name):
                out.setdefault(name, node)
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    walk(doc)
    return out


def bound_params(root: Path) -> dict[str, list[str]]:
    """RESOLVED_BINDINGS.csv — param -> categories ("<ALL>" for universal).

    This is the file SharedParamGuids treats as the domain-derived single source
    of truth for what a parameter binds to; a parameter absent from it is
    intentionally UNBOUND, so naming one in an `expected` string is a claim the
    checklist cannot make good on.
    """
    out: dict[str, list[str]] = {}
    p = root / RESOLVED_BINDINGS
    if not p.is_file():
        return out
    for raw in p.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        raw = raw.strip()
        if not raw or raw.startswith("#"):
            continue
        cols = raw.split(",", 1)
        if len(cols) < 2:
            continue
        name, cats = cols[0].strip(), cols[1].strip().strip('"')
        if not name or name.lower() == "parameter_name":
            continue
        out[name] = ["<ALL>"] if cats == "<ALL>" else [c.strip() for c in cats.split("|") if c.strip()]
    return out


# A parameter-looking token inside an `expected` string. Deliberately narrow:
# SCREAMING_SNAKE with at least one underscore, so ordinary prose and acronyms
# ("CSV", "RAG", "LOD") are not mistaken for parameter names.
PARAM_TOKEN_RX = re.compile(r"\b([A-Z][A-Z0-9]*(?:_[A-Z0-9]+){2,})\b")


def load_sources(root: Path) -> list[tuple[str, Path, dict]]:
    """Every owner's smoke-test source: (OWNER_CODE, path, parsed json)."""
    out = []
    for p in sorted((root / "docs" / "examples").glob("*/smoke_test.json")):
        out.append((p.parent.name, p, json.loads(p.read_text(encoding="utf-8-sig"))))
    return out


def docx_path_for(source_path: Path, owner: str) -> Path:
    """The generated checklist that sits beside an owner's source."""
    return source_path.parent / f"{owner}_Revit_Smoke_Test_Checklist.docx"


# ── the .docx staleness stamp ─────────────────────────────────────────────────
#
# The markdown is gated by regeneration: CI re-renders it and byte-compares. The
# .docx cannot be gated that way, because rendering it needs python-docx and the
# checker is deliberately stdlib-only so it runs on a bare runner.
#
# Without a gate the .docx is the one hand-carried copy left, and it is the copy
# the tester physically holds in the Revit session — so an edit to smoke_test.json
# that regenerated only the markdown would put a stale checklist in their hands.
# That is the exact drift this whole pipeline exists to stop, one level down.
#
# So the generator stamps a digest of its inputs into the .docx core properties,
# and the checker reads it back out of the zip with `zipfile` — stdlib, no
# python-docx, no new CI dependency. Mismatch means "regenerate", which is one
# command on any machine that has python-docx.
#
# The digest covers the SOURCE **and THE GENERATOR**, not the source alone. A
# change to render_docx() alters the document without touching the JSON, and the
# markdown byte-diff would not notice — source-only hashing would leave that hole
# open. Hashing the generator closes it at the cost of one regeneration whenever
# the generator changes, which is correct: the generator determines the output.
#
# Bytes are LF-normalised before hashing so a Windows checkout with
# core.autocrlf=true produces the same digest as a Linux CI runner. .gitattributes
# pins the source to LF for the same reason; this is defence in depth, and it also
# covers tools/*.py, which is not pinned.

# TWO STAMPS, because one digest cannot answer both questions.
#
#   inputs-sha256  what the document was BUILT FROM. Catches the real failure
#                  mode: smoke_test.json changed, only the markdown was
#                  regenerated, and the .docx quietly describes the old pack.
#
#   parts-sha256   what the document now CONTAINS — every OPC part except
#                  docProps/core.xml, which is excluded because it carries this
#                  stamp and cannot hash itself. Catches a hand-edit in Word,
#                  which the inputs digest sails straight past: the source and
#                  generator are unchanged, so the provenance stamp still
#                  matches while the body says something else.
#
# Neither is a tamper-proof seal — anyone determined can regenerate both. That
# is not the threat. The threat is someone opening the checklist in Word the
# night before the session, fixing a typo, and shipping a document that no
# longer round-trips to the source. Accidents are what a gate is for.

DOCX_STAMP_PREFIX = "inputs-sha256:"
DOCX_PARTS_PREFIX = "parts-sha256:"

# The part that carries the stamps, and therefore cannot be inside parts-sha256.
DOCX_STAMP_PART = "docProps/core.xml"

_DOCX_STAMP_RX = re.compile(re.escape(DOCX_STAMP_PREFIX) + r"([0-9a-f]{64})")
_DOCX_PARTS_RX = re.compile(re.escape(DOCX_PARTS_PREFIX) + r"([0-9a-f]{64})")


def docx_inputs_digest(root: Path, source_path: Path) -> str:
    """SHA-256 over one owner's source plus the generator that renders it."""
    h = hashlib.sha256()
    for p in (source_path,
              root / "tools" / "build_smoke_test.py",
              root / "tools" / "smoke_test_lib.py"):
        h.update(p.name.encode("utf-8") + b"\0")
        h.update(p.read_bytes().replace(b"\r\n", b"\n") + b"\0")
    return h.hexdigest()


def read_docx_stamp(docx_path: Path) -> str | None:
    """The digest stamped into a generated .docx, or None if absent/unreadable.

    Reads `docProps/core.xml` straight out of the OPC zip. None means "no usable
    stamp" — a missing file, a corrupt zip, or a document that predates stamping
    — and every one of those cases is a regenerate, so the caller does not need
    to tell them apart.
    """
    return _stamp(docx_path, _DOCX_STAMP_RX)


def read_docx_parts_stamp(docx_path: Path) -> str | None:
    """The content digest stamped into a generated .docx, or None if absent."""
    return _stamp(docx_path, _DOCX_PARTS_RX)


def _stamp(docx_path: Path, rx: re.Pattern) -> str | None:
    try:
        with zipfile.ZipFile(docx_path) as z:
            xml = z.read(DOCX_STAMP_PART).decode("utf-8", "replace")
    except (OSError, KeyError, zipfile.BadZipFile):
        return None
    m = rx.search(xml)
    return m.group(1) if m else None


def docx_parts_digest(docx_path: Path) -> str | None:
    """SHA-256 over every OPC part of a .docx except the one holding the stamps.

    Order-independent (parts are sorted by name) so a zip rewritten in a
    different entry order still hashes the same — it is the content that matters,
    not the packaging. Returns None if the file cannot be read as a zip, which
    the caller treats the same as a missing stamp: regenerate.
    """
    try:
        with zipfile.ZipFile(docx_path) as z:
            names = sorted(n for n in z.namelist() if n != DOCX_STAMP_PART)
            h = hashlib.sha256()
            for n in names:
                h.update(n.encode("utf-8") + b"\0")
                h.update(z.read(n) + b"\0")
    except (OSError, zipfile.BadZipFile):
        return None
    return h.hexdigest()
