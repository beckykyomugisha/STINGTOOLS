#!/usr/bin/env python3
"""Build StingTools/Docs/_template_sources/healthcare_rds.docx (Healthcare Pack H-8).

A .docx is a ZIP of OOXML parts. This script emits a MiniWord-consumable
Room Data Sheet template whose {{tokens}} are driven *entirely* by
``StingTools/Data/HEALTHCARE_RDS_FIELDMAP.json`` — so the template can be
rebuilt whenever the field map changes instead of being hand-edited as an
opaque binary.

Token contract (matches how RdsContextBuilder + TokenContext.AsDictionary
flatten the context that MiniWordAdapter feeds to MiniWord):

  * ``room.<x>``  field-map tokens  ->  ``{{doc.<x>}}``      (Doc bucket)
  * ``prj.<x>``   field-map tokens  ->  ``{{project.<x>}}``  (Project bucket)
  * loop tables use MiniWord ``{{foreach <name>}} … {{endforeach}}`` with the
    item fields referenced by *bare* name (e.g. ``{{type}}``), exactly like the
    existing ``transmittal.docx`` / ``deliverable_standard.docx`` templates.

The script self-verifies at the end: it re-parses the emitted .docx and fails
loudly (non-zero exit) if the flat-token set does not exactly match the routed
field-map token set, or if any loop's field tokens are missing.

Re-run:  python tools/build_healthcare_rds_docx.py
"""
import json
import os
import re
import sys
import zipfile
from datetime import datetime, timezone
from xml.sax.saxutils import escape

# ── Paths (portable — resolved relative to this script) ─────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
FIELDMAP = os.path.join(REPO_ROOT, "StingTools", "Data", "HEALTHCARE_RDS_FIELDMAP.json")
OUT = os.path.join(REPO_ROOT, "StingTools", "Docs", "_template_sources", "healthcare_rds.docx")

BRAND = "582C83"  # STING purple (matches build_lps_docx.py)

NS_W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS_R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"

# ── Field-map load + token routing ──────────────────────────────────────────


def route(token):
    """Map a field-map token to its flattened MiniWord key."""
    if token.startswith("room."):
        return "doc." + token[len("room."):]
    if token.startswith("prj."):
        return "project." + token[len("prj."):]
    return token


def load_fieldmap():
    with open(FIELDMAP, encoding="utf-8") as fh:
        fm = json.load(fh)
    tokens = {route(k): k for k in fm["tokens"].keys()}
    loops = {}
    for name, spec in fm["loops"].items():
        loops[name] = list(spec["fields"]) if isinstance(spec, dict) else []
    return tokens, loops


# Builder-added convenience tokens (see RdsContextBuilder.Build).
EXTRA_DOC_TOKENS = ["doc.generated_at", "doc.generator"]

# ── Presentation: ordered sections + friendly labels ────────────────────────
# Explicit labels for known tokens; anything in the field map that is *not*
# covered by a section is swept into an "Other parameters" table so coverage is
# guaranteed even if the field map grows without this file being updated.
LABELS = {
    "doc.number": "Room number", "doc.name": "Room name",
    "doc.function": "Function / use", "doc.area": "Area (m²)",
    "doc.volume": "Volume (m³)", "doc.height": "Height (mm)",
    "doc.occupancy": "Design occupancy", "doc.escape": "Fire escape capacity",
    "doc.health_class": "Healthcare room class", "doc.hbn_ref": "HBN reference",
    "doc.fgi_ref": "FGI reference", "doc.htm_ref": "HTM reference",
    "doc.adb_code": "Activity DataBase code", "doc.infect.class": "Infection-control class",
    "doc.press.regime": "Pressure regime", "doc.press.delta_pa": "Design ΔP (Pa)",
    "doc.ach.req": "Air changes / hr (total)", "doc.ach.outside": "Air changes / hr (outside air)",
    "doc.hepa.required": "HEPA required", "doc.hepa.grade": "HEPA grade",
    "doc.temp.design_c": "Design temperature (°C)", "doc.rh.design_pct": "Design RH (%)",
    "doc.noise.db": "Background noise (dB)", "doc.noise.nr": "Noise rating (NR)",
    "doc.lighting.lux": "Design illuminance (lux)", "doc.lighting.cct": "Colour temperature (K)",
    "doc.fire.compartment": "Fire compartment", "doc.fire.refuge": "Refuge area",
    "doc.lig.risk": "Ligature risk level", "doc.bari.swl_kg": "Bariatric SWL (kg)",
    "doc.hoist": "Ceiling hoist track", "doc.nursecall": "Nurse-call type",
    "doc.privacy": "Privacy level", "doc.mri_zone": "MRI safety zone",
    "doc.rad.controlled": "Radiation controlled area",
    "doc.finish.floor": "Floor finish", "doc.finish.ceiling": "Ceiling finish",
    "doc.finish.wall": "Wall finish", "doc.finish.base": "Skirting / base",
    "project.code": "Project code", "project.name": "Project name",
    "project.client": "Client", "project.facility_type": "Facility type",
    "project.code_base": "Code base", "project.htm_region": "HTM region",
    "project.ae_vent": "AE — Ventilation", "project.ae_mgas": "AE — Medical gases",
    "project.ae_water": "AE — Water", "project.ae_elec": "AE — Electrical",
}

# Sections: (heading, [flat-token, ...]). Tokens listed here must exist in the
# field map; missing ones are skipped with a warning so a renamed token never
# hard-fails the build here (the self-test at the end is the real gate).
SECTIONS = [
    ("1. Room Identity",
     ["doc.number", "doc.name", "doc.function", "doc.area", "doc.volume",
      "doc.height", "doc.occupancy", "doc.escape"]),
    ("2. Classification",
     ["doc.health_class", "doc.hbn_ref", "doc.fgi_ref", "doc.htm_ref",
      "doc.adb_code", "doc.infect.class"]),
    ("3. Ventilation & Pressure Regime",
     ["doc.press.regime", "doc.press.delta_pa", "doc.ach.req", "doc.ach.outside",
      "doc.hepa.required", "doc.hepa.grade", "doc.temp.design_c", "doc.rh.design_pct"]),
    ("4. Acoustics & Lighting",
     ["doc.noise.db", "doc.noise.nr", "doc.lighting.lux", "doc.lighting.cct"]),
    ("5. Safety, Security & Special Requirements",
     ["doc.fire.compartment", "doc.fire.refuge", "doc.lig.risk", "doc.bari.swl_kg",
      "doc.hoist", "doc.nursecall", "doc.privacy", "doc.mri_zone", "doc.rad.controlled"]),
    ("6. Room Finishes",
     ["doc.finish.floor", "doc.finish.ceiling", "doc.finish.wall", "doc.finish.base"]),
]

PROJECT_SECTION = ("9. Project & Authorising Engineers",
                   ["project.code", "project.name", "project.client", "project.facility_type",
                    "project.code_base", "project.htm_region", "project.ae_vent",
                    "project.ae_mgas", "project.ae_water", "project.ae_elec"])

# Loop tables, in render order, with heading text.
LOOP_HEADINGS = {
    "finishes": "6a. Finish Schedule",
    "services": "7. Engineering Services",
    "equipment": "8. Equipment / FF&E Schedule",
    "signatures": "10. Sign-off",
}


def pretty_field(name):
    return name.replace("_", " ").replace("prod code", "Product code").capitalize()


# ── document.xml builders ───────────────────────────────────────────────────


def para(text, *, style=None, bold=False, size=None, color=None, align=None):
    pPr = []
    if style:
        pPr.append(f'<w:pStyle w:val="{style}"/>')
    if align:
        pPr.append(f'<w:jc w:val="{align}"/>')
    pPrXml = f'<w:pPr>{"".join(pPr)}</w:pPr>' if pPr else ""
    rPr = []
    if bold:
        rPr.append("<w:b/>")
    if size:
        rPr.append(f'<w:sz w:val="{size * 2}"/><w:szCs w:val="{size * 2}"/>')
    if color:
        rPr.append(f'<w:color w:val="{color}"/>')
    rPrXml = f'<w:rPr>{"".join(rPr)}</w:rPr>' if rPr else ""
    return (f'<w:p>{pPrXml}<w:r>{rPrXml}'
            f'<w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>')


def heading(text):
    return para(text, style="Heading1", bold=True, size=13, color=BRAND)


def kv_row(label, token_key):
    """Two-column row: bold label | {{token_key}} value."""
    return f"""<w:tr>
        <w:tc><w:tcPr><w:tcW w:w="3400" w:type="dxa"/>
        <w:shd w:val="clear" w:color="auto" w:fill="F2EEF6"/></w:tcPr>
        <w:p><w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">{escape(label)}</w:t></w:r></w:p></w:tc>
        <w:tc><w:tcPr><w:tcW w:w="6200" w:type="dxa"/></w:tcPr>
        <w:p><w:r><w:t xml:space="preserve">{{{{{token_key}}}}}</w:t></w:r></w:p></w:tc>
    </w:tr>"""


def kv_table(rows):
    return f"""<w:tbl>
        <w:tblPr><w:tblW w:w="9600" w:type="dxa"/>
            <w:tblBorders>
                <w:top w:val="single" w:sz="4" w:color="888888"/>
                <w:left w:val="single" w:sz="4" w:color="888888"/>
                <w:bottom w:val="single" w:sz="4" w:color="888888"/>
                <w:right w:val="single" w:sz="4" w:color="888888"/>
                <w:insideH w:val="single" w:sz="4" w:color="DDDDDD"/>
                <w:insideV w:val="single" w:sz="4" w:color="DDDDDD"/>
            </w:tblBorders></w:tblPr>
        <w:tblGrid><w:gridCol w:w="3400"/><w:gridCol w:w="6200"/></w:tblGrid>
        {"".join(rows)}
    </w:tbl>"""


def loop_table(loop_name, headers, fields, widths):
    """Header row + one data row bracketed by {{foreach}}/{{endforeach}}.

    Mirrors the working structure in transmittal.docx: the {{foreach name}}
    marker sits as its own paragraph in the FIRST data cell (above the first
    field token) and {{endforeach}} as its own paragraph in the LAST data cell.
    """
    grid = "".join(f'<w:gridCol w:w="{w}"/>' for w in widths)
    head_cells = "".join(
        f'''<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/>
            <w:shd w:val="clear" w:color="auto" w:fill="{BRAND}"/></w:tcPr>
            <w:p><w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/></w:rPr>
            <w:t xml:space="preserve">{escape(h)}</w:t></w:r></w:p></w:tc>'''
        for h, w in zip(headers, widths))
    body_cells = []
    n = len(fields)
    for idx, (f, w) in enumerate(zip(fields, widths)):
        paras = []
        if idx == 0:
            paras.append(f'<w:p><w:r><w:t xml:space="preserve">{{{{foreach {loop_name}}}}}</w:t></w:r></w:p>')
        paras.append(f'<w:p><w:r><w:t xml:space="preserve">{{{{{f}}}}}</w:t></w:r></w:p>')
        if idx == n - 1:
            paras.append('<w:p><w:r><w:t xml:space="preserve">{{endforeach}}</w:t></w:r></w:p>')
        body_cells.append(
            f'<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/></w:tcPr>{"".join(paras)}</w:tc>')
    total = sum(widths)
    return f"""<w:tbl>
        <w:tblPr><w:tblW w:w="{total}" w:type="dxa"/>
            <w:tblBorders>
                <w:top w:val="single" w:sz="4" w:color="888888"/>
                <w:left w:val="single" w:sz="4" w:color="888888"/>
                <w:bottom w:val="single" w:sz="4" w:color="888888"/>
                <w:right w:val="single" w:sz="4" w:color="888888"/>
                <w:insideH w:val="single" w:sz="4" w:color="DDDDDD"/>
                <w:insideV w:val="single" w:sz="4" w:color="DDDDDD"/>
            </w:tblBorders></w:tblPr>
        <w:tblGrid>{grid}</w:tblGrid>
        <w:tr><w:trPr><w:tblHeader/></w:trPr>{head_cells}</w:tr>
        <w:tr>{"".join(body_cells)}</w:tr>
    </w:tbl>"""


def spacer():
    return '<w:p><w:r><w:t xml:space="preserve"> </w:t></w:r></w:p>'


def widths_for(fields, total=9600):
    n = len(fields)
    base = total // n
    ws = [base] * n
    ws[-1] += total - base * n
    return ws


# ── Assembly ────────────────────────────────────────────────────────────────


def build_document_xml(tokens, loops):
    covered = set()
    body = []

    # Title band (also repeated as page header via header1.xml).
    body.append(para("STING — Healthcare Room Data Sheet", bold=True, size=20,
                     color=BRAND, align="center"))
    body.append(para("HBN / HTM Room Data Sheet — {{doc.number}}  {{doc.name}}",
                     size=12, color="666666", align="center"))
    body.append(spacer())

    for title, keys in SECTIONS:
        present = [k for k in keys if k in tokens]
        for k in keys:
            if k not in tokens:
                print(f"  warn: section '{title}' references '{k}' not in field map", file=sys.stderr)
        if not present:
            continue
        body.append(heading(title))
        body.append(kv_table([kv_row(LABELS.get(k, pretty_field(k.split('.', 1)[-1])), k)
                              for k in present]))
        body.append(spacer())
        covered.update(present)

    # Finish schedule loop (6a) sits with the finishes section.
    if "finishes" in loops:
        body.append(heading(LOOP_HEADINGS["finishes"]))
        fields = loops["finishes"]
        body.append(loop_table("finishes", [pretty_field(f) for f in fields], fields,
                               widths_for(fields)))
        body.append(spacer())

    for lname in ("services", "equipment"):
        if lname in loops:
            body.append(heading(LOOP_HEADINGS[lname]))
            fields = loops[lname]
            body.append(loop_table(lname, [pretty_field(f) for f in fields], fields,
                                   widths_for(fields)))
            body.append(spacer())

    # Project & AE section.
    title, keys = PROJECT_SECTION
    present = [k for k in keys if k in tokens]
    if present:
        body.append(heading(title))
        body.append(kv_table([kv_row(LABELS.get(k, pretty_field(k.split('.', 1)[-1])), k)
                              for k in present]))
        body.append(spacer())
        covered.update(present)

    # Sign-off loop (10).
    if "signatures" in loops:
        body.append(heading(LOOP_HEADINGS["signatures"]))
        fields = loops["signatures"]
        body.append(loop_table("signatures", [pretty_field(f) for f in fields], fields,
                               widths_for(fields)))
        body.append(spacer())

    # Any field-map token not placed in a section -> Other parameters (coverage
    # guarantee so the self-test never fails just because the field map grew).
    leftover = [k for k in tokens if k not in covered]
    if leftover:
        body.append(heading("11. Other Parameters"))
        body.append(kv_table([kv_row(LABELS.get(k, pretty_field(k.split('.', 1)[-1])), k)
                              for k in sorted(leftover)]))
        body.append(spacer())

    # Footer note uses the builder's convenience tokens so they are exercised.
    body.append(para(
        "Generated by {{doc.generator}} on {{doc.generated_at}}. "
        "This Room Data Sheet is model-derived; clinical, infection-control and "
        "authorising-engineer sign-off (§10) are required before it is issued "
        "for construction under the applicable HBN/HTM guidance.",
        size=9, color="666666"))

    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="{NS_W}" xmlns:r="{NS_R}">
<w:body>
{"".join(body)}
<w:sectPr>
    <w:headerReference w:type="default" r:id="rIdHdr"/>
    <w:footerReference w:type="default" r:id="rIdFtr"/>
    <w:pgSz w:w="11906" w:h="16838"/>
    <w:pgMar w:top="1440" w:right="1134" w:bottom="1134" w:left="1134"
             w:header="708" w:footer="708" w:gutter="0"/>
</w:sectPr>
</w:body>
</w:document>"""


HEADER_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:hdr xmlns:w="{NS_W}" xmlns:r="{NS_R}">
  <w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="12" w:space="1" w:color="{BRAND}"/></w:pBdr></w:pPr>
    <w:r><w:rPr><w:b/><w:color w:val="{BRAND}"/><w:sz w:val="18"/></w:rPr>
    <w:t xml:space="preserve">STING Healthcare — Room Data Sheet</w:t></w:r>
    <w:r><w:rPr><w:sz w:val="16"/><w:color w:val="888888"/></w:rPr>
    <w:t xml:space="preserve">    {{{{project.name}}}} ({{{{project.code}}}})</w:t></w:r></w:p>
</w:hdr>"""

FOOTER_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:ftr xmlns:w="{NS_W}" xmlns:r="{NS_R}">
  <w:tbl><w:tblPr><w:tblW w:w="9638" w:type="dxa"/>
    <w:tblLook w:firstColumn="1" w:firstRow="0" w:lastColumn="0" w:lastRow="0" w:noHBand="0" w:noVBand="1" w:val="04A0"/></w:tblPr>
    <w:tblGrid><w:gridCol w:w="3213"/><w:gridCol w:w="3212"/><w:gridCol w:w="3213"/></w:tblGrid>
    <w:tr>
      <w:tc><w:tcPr><w:tcW w:w="3213" w:type="dxa"/></w:tcPr>
        <w:p><w:r><w:rPr><w:sz w:val="16"/></w:rPr><w:t xml:space="preserve">{{{{project.client}}}}</w:t></w:r></w:p></w:tc>
      <w:tc><w:tcPr><w:tcW w:w="3212" w:type="dxa"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="center"/></w:pPr>
          <w:r><w:rPr><w:sz w:val="16"/></w:rPr><w:t xml:space="preserve">Page </w:t></w:r>
          <w:r><w:fldChar w:fldCharType="begin"/><w:instrText xml:space="preserve">PAGE</w:instrText><w:fldChar w:fldCharType="end"/></w:r>
          <w:r><w:rPr><w:sz w:val="16"/></w:rPr><w:t xml:space="preserve"> of </w:t></w:r>
          <w:r><w:fldChar w:fldCharType="begin"/><w:instrText xml:space="preserve">NUMPAGES</w:instrText><w:fldChar w:fldCharType="end"/></w:r></w:p></w:tc>
      <w:tc><w:tcPr><w:tcW w:w="3213" w:type="dxa"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="right"/></w:pPr>
          <w:r><w:rPr><w:sz w:val="16"/></w:rPr><w:t xml:space="preserve">RDS {{{{doc.number}}}}</w:t></w:r></w:p></w:tc>
    </w:tr></w:tbl>
</w:ftr>"""

CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>"""

ROOT_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>"""

DOCUMENT_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
  <Relationship Id="rIdHdr" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
  <Relationship Id="rIdFtr" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
</Relationships>"""

STYLES_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="{NS_W}">
  <w:docDefaults><w:rPrDefault><w:rPr>
    <w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/>
    <w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:rPrDefault>
    <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/><w:qFormat/>
    <w:pPr><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr>
    <w:rPr><w:b/><w:color w:val="{BRAND}"/><w:sz w:val="26"/></w:rPr></w:style>
</w:styles>"""

APP_XML = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
  <Application>StingTools — Healthcare Pack (RDS)</Application>
  <DocSecurity>0</DocSecurity>
  <Template>healthcare_rds.docx</Template>
</Properties>"""


def core_xml():
    now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
    xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>STING Healthcare — Room Data Sheet</dc:title>
  <dc:creator>StingTools</dc:creator>
  <cp:lastModifiedBy>StingTools</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified>
</cp:coreProperties>"""


# ── Self-verification ───────────────────────────────────────────────────────


def verify(path, tokens, loops):
    """Re-parse the emitted .docx; fail loudly on any token/loop mismatch."""
    z = zipfile.ZipFile(path)
    xml = "".join(z.read(n).decode("utf-8") for n in z.namelist()
                  if n.endswith(".xml") and ("document" in n or "header" in n or "footer" in n))
    raw = set(re.findall(r"\{\{([^}]+)\}\}", xml))

    control = set()
    flat = set()
    loop_fields = set()
    loop_names = set(loops.keys())
    for t in raw:
        t = t.strip()
        if t.startswith("foreach ") or t == "endforeach":
            control.add(t)
            continue
        if "." in t:
            flat.add(t)
        else:
            loop_fields.add(t)

    expected_flat = set(tokens.keys()) | set(EXTRA_DOC_TOKENS)
    errors = []

    missing = expected_flat - flat
    extra = flat - expected_flat
    if missing:
        errors.append(f"field-map tokens missing from template: {sorted(missing)}")
    if extra:
        errors.append(f"template has tokens not in field map: {sorted(extra)}")

    for name, fields in loops.items():
        if f"foreach {name}" not in control:
            errors.append(f"loop '{name}' has no {{{{foreach {name}}}}} marker")
        for f in fields:
            if f not in loop_fields:
                errors.append(f"loop '{name}' field '{f}' missing from template")

    all_expected_loop_fields = {f for fs in loops.values() for f in fs}
    stray = loop_fields - all_expected_loop_fields
    if stray:
        errors.append(f"template has bare loop tokens not in any loop field set: {sorted(stray)}")

    if errors:
        print("SELF-TEST FAILED:", file=sys.stderr)
        for e in errors:
            print("  -", e, file=sys.stderr)
        sys.exit(1)

    print(f"  self-test OK: {len(expected_flat)} flat tokens, "
          f"{len(loop_names)} loops ({', '.join(sorted(loop_names))})")


# ── Main ────────────────────────────────────────────────────────────────────


def main():
    tokens, loops = load_fieldmap()
    document_xml = build_document_xml(tokens, loops)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", CONTENT_TYPES)
        z.writestr("_rels/.rels", ROOT_RELS)
        z.writestr("word/_rels/document.xml.rels", DOCUMENT_RELS)
        z.writestr("word/document.xml", document_xml)
        z.writestr("word/header1.xml", HEADER_XML)
        z.writestr("word/footer1.xml", FOOTER_XML)
        z.writestr("word/styles.xml", STYLES_XML)
        z.writestr("docProps/core.xml", core_xml())
        z.writestr("docProps/app.xml", APP_XML)

    print(f"Wrote {OUT} ({os.path.getsize(OUT)} bytes)")
    verify(OUT, tokens, loops)


if __name__ == "__main__":
    main()
