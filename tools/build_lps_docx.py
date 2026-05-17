#!/usr/bin/env python3
"""Build StingTools/Docs/_template_sources/lps_compliance_report.docx.

A .docx is a ZIP of OOXML parts. This script writes the minimum set of
parts MiniWord needs to render: document.xml, styles.xml, content types,
relationships, and core/app properties. Tokens are MiniWord {{name}} form
inside table rows for auto-row-duplication; flat tokens elsewhere.
"""
import os
import zipfile
from datetime import datetime
from xml.sax.saxutils import escape

OUT = "/home/user/STINGTOOLS/StingTools/Docs/_template_sources/lps_compliance_report.docx"

# ── XML namespace shorthand (used in many parts) ────────────────────────────
NS_W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS_R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"

# ── document.xml builder helpers ────────────────────────────────────────────


def para(text, *, style=None, bold=False, size=None, color=None, align=None):
    """Single-paragraph WordprocessingML run."""
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
        # OOXML half-points: size 22 = 11pt
        rPr.append(f'<w:sz w:val="{size * 2}"/>')
        rPr.append(f'<w:szCs w:val="{size * 2}"/>')
    if color:
        rPr.append(f'<w:color w:val="{color}"/>')
    rPrXml = f'<w:rPr>{"".join(rPr)}</w:rPr>' if rPr else ""

    return (
        f'<w:p>{pPrXml}<w:r>{rPrXml}'
        f'<w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>'
    )


def heading(text, level=1):
    return para(text, style=f"Heading{level}", bold=True, size=14 if level == 1 else 12)


def kv_row(label, value):
    """Two-column table row: label | value."""
    return f"""<w:tr>
        <w:tc><w:tcPr><w:tcW w:w="3200" w:type="dxa"/></w:tcPr>
        <w:p><w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">{escape(label)}</w:t></w:r></w:p></w:tc>
        <w:tc><w:tcPr><w:tcW w:w="6400" w:type="dxa"/></w:tcPr>
        <w:p><w:r><w:t xml:space="preserve">{escape(value)}</w:t></w:r></w:p></w:tc>
    </w:tr>"""


def kv_table(rows):
    return f"""<w:tbl>
        <w:tblPr>
            <w:tblW w:w="9600" w:type="dxa"/>
            <w:tblBorders>
                <w:top w:val="single" w:sz="4" w:color="888888"/>
                <w:left w:val="single" w:sz="4" w:color="888888"/>
                <w:bottom w:val="single" w:sz="4" w:color="888888"/>
                <w:right w:val="single" w:sz="4" w:color="888888"/>
                <w:insideH w:val="single" w:sz="4" w:color="DDDDDD"/>
                <w:insideV w:val="single" w:sz="4" w:color="DDDDDD"/>
            </w:tblBorders>
        </w:tblPr>
        <w:tblGrid><w:gridCol w:w="3200"/><w:gridCol w:w="6400"/></w:tblGrid>
        {"".join(rows)}
    </w:tbl>"""


def loop_table(headers, fields, widths, *, header_bg="582C83"):
    """A table whose data row contains {{field}} tokens. MiniWord duplicates
    the row per item in the bound list. The list dict key is the same as the
    {{field}} names — MiniWord matches by header context."""
    grid = "".join(f'<w:gridCol w:w="{w}"/>' for w in widths)
    head_cells = "".join(
        f'''<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/>
            <w:shd w:val="clear" w:color="auto" w:fill="{header_bg}"/></w:tcPr>
            <w:p><w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/></w:rPr>
            <w:t xml:space="preserve">{escape(h)}</w:t></w:r></w:p></w:tc>'''
        for h, w in zip(headers, widths)
    )
    body_cells = "".join(
        f'''<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/></w:tcPr>
            <w:p><w:r><w:t xml:space="preserve">{{{{{f}}}}}</w:t></w:r></w:p></w:tc>'''
        for f, w in zip(fields, widths)
    )
    total_w = sum(widths)
    return f"""<w:tbl>
        <w:tblPr>
            <w:tblW w:w="{total_w}" w:type="dxa"/>
            <w:tblBorders>
                <w:top w:val="single" w:sz="4" w:color="888888"/>
                <w:left w:val="single" w:sz="4" w:color="888888"/>
                <w:bottom w:val="single" w:sz="4" w:color="888888"/>
                <w:right w:val="single" w:sz="4" w:color="888888"/>
                <w:insideH w:val="single" w:sz="4" w:color="DDDDDD"/>
                <w:insideV w:val="single" w:sz="4" w:color="DDDDDD"/>
            </w:tblBorders>
        </w:tblPr>
        <w:tblGrid>{grid}</w:tblGrid>
        <w:tr><w:trPr><w:tblHeader/></w:trPr>{head_cells}</w:tr>
        <w:tr>{body_cells}</w:tr>
    </w:tbl>"""


def section_break():
    return '<w:p><w:r><w:t xml:space="preserve"> </w:t></w:r></w:p>'


# ── Body assembly ──────────────────────────────────────────────────────────

body_parts = []

# Cover
body_parts.append(
    para(
        "STING — Lightning Protection Compliance Report",
        bold=True, size=20, color="582C83", align="center",
    )
)
body_parts.append(
    para(
        "BS EN 62305 — Protection against lightning",
        size=12, color="666666", align="center",
    )
)
body_parts.append(section_break())

# 1. Project information
body_parts.append(heading("1. Project Information"))
body_parts.append(
    kv_table(
        [
            kv_row("Project name", "{{project_name}}"),
            kv_row("Project code", "{{project_code}}"),
            kv_row("Building name", "{{building_name}}"),
            kv_row("Client", "{{client_name}}"),
            kv_row("Report date", "{{report_date}}"),
            kv_row("Standard", "BS EN 62305"),
        ]
    )
)
body_parts.append(section_break())

# 2. LPS class & design parameters
body_parts.append(heading("2. LPS Class & Design Parameters"))
body_parts.append(
    kv_table(
        [
            kv_row("LPS class", "{{lps_class}}"),
            kv_row("Rolling sphere radius (m)", "{{rolling_sphere_m}}"),
            kv_row("Mesh size (m)", "{{mesh_size_m}}"),
            kv_row("Protection angle (deg)", "{{protection_angle_deg}}"),
            kv_row("Down conductor spacing (m)", "{{down_conductor_spacing_m}}"),
            kv_row("Earth resistance target (Ω)", "{{earth_resistance_target_ohm}}"),
            kv_row("Conductor cross-section (mm²)", "{{conductor_cross_sect_mm2}}"),
            kv_row("Conductor material", "{{conductor_material}}"),
            kv_row("Surge protection level", "{{surge_protection_lvl}}"),
            kv_row("Separation distance (max, mm)", "{{separation_distance_mm}}"),
            kv_row("Inspection interval (months)", "{{inspection_interval}}"),
            kv_row("Risk assessment reference", "{{risk_assessment_ref}}"),
            kv_row("Ground flash density Ng (flashes/km²/yr)", "{{ng_value}}"),
            kv_row("Annual strikes Nd", "{{annual_strikes}}"),
            kv_row("Collection area (m²)", "{{collection_area_m2}}"),
        ]
    )
)
body_parts.append(section_break())

# 3. Component counts
body_parts.append(heading("3. Component Counts"))
body_parts.append(
    kv_table(
        [
            kv_row("Air terminals", "{{air_terminal_count}}"),
            kv_row("Down conductors", "{{down_conductor_count}}"),
            kv_row("Earth electrodes", "{{earth_electrode_count}}"),
            kv_row("Average earth resistance (Ω)", "{{earth_resistance_avg_ohm}}"),
        ]
    )
)
body_parts.append(section_break())

# 4. Compliance verdict
body_parts.append(heading("4. Compliance Verdict"))
body_parts.append(
    para("Verdict: {{compliance_status}}", bold=True, size=14, color="582C83")
)
body_parts.append(
    kv_table(
        [
            kv_row("Pass", "{{compliance_pass}}"),
            kv_row("Warn", "{{compliance_warn}}"),
            kv_row("Fail", "{{compliance_fail}}"),
        ]
    )
)
body_parts.append(section_break())

# 5. Compliance checks (loop table)
body_parts.append(heading("5. Compliance Checks"))
body_parts.append(
    loop_table(
        headers=["Check", "Severity", "Message"],
        fields=["CheckName", "Severity", "Message"],
        widths=[2400, 1600, 5600],
    )
)
body_parts.append(section_break())

# 6. Down conductors (loop table)
body_parts.append(heading("6. Down Conductors"))
body_parts.append(
    loop_table(
        headers=[
            "Id",
            "Family",
            "Level",
            "Length (m)",
            "Material",
            "Cross-sec (mm²)",
            "Sep dist (mm)",
            "Status",
        ],
        fields=[
            "Id",
            "Family",
            "Level",
            "LengthM",
            "Material",
            "CrossSectMm2",
            "SepDistMm",
            "Status",
        ],
        widths=[800, 1400, 1200, 1000, 1000, 1200, 1200, 1400],
    )
)
body_parts.append(section_break())

# 7. Earth electrodes (loop table)
body_parts.append(heading("7. Earth Electrodes"))
body_parts.append(
    loop_table(
        headers=[
            "Id",
            "Family",
            "Level",
            "Earth type",
            "R (Ω)",
            "Last test",
            "Cert ref",
            "Status",
        ],
        fields=[
            "Id",
            "Family",
            "Level",
            "EarthType",
            "ResistanceOhm",
            "TestDate",
            "CertRef",
            "Status",
        ],
        widths=[800, 1400, 1200, 1300, 800, 1300, 1500, 1300],
    )
)
body_parts.append(section_break())

# 8. Inspection
body_parts.append(heading("8. Inspection"))
body_parts.append(
    kv_table(
        [
            kv_row("Last test date", "{{test_date}}"),
            kv_row("Certificate reference", "{{cert_ref}}"),
            kv_row("Next interval (months)", "{{inspection_interval}}"),
        ]
    )
)
body_parts.append(section_break())

# Footer
body_parts.append(
    para(
        "Generated by StingTools — Lightning Protection module. "
        "This report is a model-derived audit of the LPS configuration; "
        "physical commissioning, soil resistivity tests and visual inspection "
        "by a qualified electrician are required for handover under "
        "BS EN 62305-3 §E.7.",
        size=9,
        color="666666",
    )
)

# ── Wrap in document.xml ────────────────────────────────────────────────────
DOCUMENT_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="{NS_W}" xmlns:r="{NS_R}">
<w:body>
{"".join(body_parts)}
<w:sectPr>
    <w:pgSz w:w="11906" w:h="16838"/>
    <w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134"
             w:header="708" w:footer="708" w:gutter="0"/>
</w:sectPr>
</w:body>
</w:document>"""

# ── Other parts ────────────────────────────────────────────────────────────

CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml"
            ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml"
            ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/docProps/core.xml"
            ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml"
            ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
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
</Relationships>"""

STYLES_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="{NS_W}">
  <w:docDefaults>
    <w:rPrDefault><w:rPr>
      <w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/>
      <w:sz w:val="22"/><w:szCs w:val="22"/>
    </w:rPr></w:rPrDefault>
    <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/><w:qFormat/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr>
    <w:rPr><w:b/><w:color w:val="582C83"/><w:sz w:val="32"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr><w:spacing w:before="200" w:after="100"/><w:outlineLvl w:val="1"/></w:pPr>
    <w:rPr><w:b/><w:color w:val="333333"/><w:sz w:val="26"/></w:rPr>
  </w:style>
</w:styles>"""

now_iso = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
CORE_XML = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                   xmlns:dcterms="http://purl.org/dc/terms/"
                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>STING — Lightning Protection Compliance Report</dc:title>
  <dc:creator>StingTools</dc:creator>
  <cp:lastModifiedBy>StingTools</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">{now_iso}</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">{now_iso}</dcterms:modified>
</cp:coreProperties>"""

APP_XML = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
            xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>StingTools — Lightning Protection</Application>
  <DocSecurity>0</DocSecurity>
  <Template>lps_compliance_report.docx</Template>
</Properties>"""

# ── Pack zip ────────────────────────────────────────────────────────────────
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("[Content_Types].xml", CONTENT_TYPES)
    z.writestr("_rels/.rels", ROOT_RELS)
    z.writestr("word/_rels/document.xml.rels", DOCUMENT_RELS)
    z.writestr("word/document.xml", DOCUMENT_XML)
    z.writestr("word/styles.xml", STYLES_XML)
    z.writestr("docProps/core.xml", CORE_XML)
    z.writestr("docProps/app.xml", APP_XML)

print(f"Wrote {OUT}")
print(f"Size: {os.path.getsize(OUT)} bytes")
