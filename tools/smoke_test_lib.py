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

import json
import re
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
