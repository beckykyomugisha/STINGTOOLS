#!/usr/bin/env python3
"""
cover_v8_spec.py — STING A1-landscape project COVER sheet, spec preview.

Renders the "cover v8" information-dense front sheet the design team is
iterating on and, in --params mode, a manual label-authoring GUIDE that
prints the real STING shared-parameter each cell should bind to.

Blocks: header identity band · PROJECT · 16:9 IMAGE RENDER · DOCUMENT
CONTROL · BIM & COORDINATION · REVISION HISTORY (native revision
schedule) · WHAT CHANGED · STANDARDS & REFERENCES · DRAWING REGISTER ·
PROJECT DIRECTORY · DELIVERABLE / ISSUE RIBBON (bottom).

Parameter names are harvested from Data/STING_TITLE_BLOCKS.json (the
label bindings the TitleBlockFactory actually mints) so the guide is
authoritative, not guessed. Fields with no bound parameter are flagged
"(… — manual)". Discipline swatches show the HEX code (from the
definitive StingColorRegistry.Disciplines) rather than only the name.

This is a *design study* for review — distinct from the committed Revit
family STING_TB_COVER_A1_v1.0 in STING_TITLE_BLOCKS.json.

Modes:
  python3 cover_v8_spec.py                 → annotated preview (2 edits highlighted)
  python3 cover_v8_spec.py --no-annotate   → clean preview
  python3 cover_v8_spec.py --params        → label-authoring guide (real ${PARAM}s)
"""
import os
import sys

# ── Sheet ────────────────────────────────────────────────────────────
W, H = 841.0, 594.0            # A1 landscape, mm (1 svg unit = 1 mm)

# ── Palette ──────────────────────────────────────────────────────────
INK, SUB, BAR, CARD = "#1A1A1A", "#5A5F66", "#C4C7CC", "#DADCDF"
LINE, HAIR = "#3A3E44", "#9AA0A6"
NEW_BG, NEW_EDGE = "#FFF3C4", "#E0A800"
PARAM_BLUE, NOTE_AMBER = "#1565C0", "#9A6A00"

# ── Discipline colours — DEFINITIVE reference ────────────────────────
# StingColorRegistry.Disciplines (LegendBuilderCommands.cs). COMBINED /
# federated is NOT in the registry — proposed neutral graphite.
DISCIPLINES = [
    ("ARCH",     "A",  "#A0A0A0"),
    ("STRUCT",   "S",  "#C80000"),
    ("MECH",     "M",  "#0080FF"),
    ("ELEC",     "E",  "#FFC800"),
    ("PLUMB",    "P",  "#00B400"),
    ("FIRE",     "FP", "#FF6400"),
    ("LV/DATA",  "LV", "#A000C8"),
    ("COMBINED", "—",  "#3C3C3C"),
]

PARAM = "--params" in sys.argv
ANNOTATE = ("--no-annotate" not in sys.argv) and not PARAM
_svg = []


# ── SVG helpers ──────────────────────────────────────────────────────
def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def rect(x, y, w, h, fill="none", stroke=LINE, sw=0.4, rx=0, extra=""):
    r = f' rx="{rx}"' if rx else ""
    _svg.append(f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" '
                f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{r} {extra}/>')


def line(x1, y1, x2, y2, stroke=HAIR, sw=0.3):
    _svg.append(f'<line x1="{x1:.2f}" y1="{y1:.2f}" x2="{x2:.2f}" y2="{y2:.2f}" '
                f'stroke="{stroke}" stroke-width="{sw}"/>')


def text(x, y, s, size=2.6, fill=INK, weight="normal", anchor="start",
         spacing=None, family="Helvetica, Arial, sans-serif"):
    ls = f' letter-spacing="{spacing}"' if spacing else ""
    _svg.append(f'<text x="{x:.2f}" y="{y:.2f}" font-family="{family}" '
                f'font-size="{size}" fill="{fill}" font-weight="{weight}" '
                f'text-anchor="{anchor}"{ls}>{esc(s)}</text>')


def ptok(x, y, param, size=1.75):
    """Render the bound parameter (or a manual-entry note) — guide mode only."""
    if not (PARAM and param):
        return
    if param.startswith("("):
        text(x, y, param, size=size, fill=NOTE_AMBER)
    else:
        text(x, y, "${" + param + "}", size=size, fill=PARAM_BLUE,
             family="Consolas, 'Courier New', monospace")


def panel(x, y, w, h, title, right_tag=None, bar=7.5):
    rect(x, y, w, h, fill="#FFFFFF", stroke=LINE, sw=0.5)
    rect(x, y, w, bar, fill=BAR, stroke=LINE, sw=0.5)
    text(x + 2.5, y + bar - 2.2, title, size=3.5, weight="bold", spacing="0.3")
    if right_tag:
        text(x + w - 2.5, y + bar - 2.2, right_tag, size=2.6, weight="bold",
             fill=SUB, anchor="end")


def kv(x, y, label, param=None, rule_w=0):
    text(x, y, label, size=2.5, fill=SUB, spacing="0.2")
    if rule_w:
        line(x, y + 2.6, x + rule_w, y + 2.6, HAIR, 0.3)
    ptok(x, y + 4.6, param)


def highlight(x, y, w, h):
    if ANNOTATE:
        rect(x, y, w, h, fill=NEW_BG, stroke=NEW_EDGE, sw=0.4, rx=0.8)


def new_tag(x, y):
    if ANNOTATE:
        rect(x, y - 3.0, 8.5, 3.6, fill=NEW_EDGE, stroke="none", rx=0.6)
        text(x + 4.25, y - 0.5, "NEW", size=2.3, fill="#FFFFFF", weight="bold", anchor="middle")


# ── Build ────────────────────────────────────────────────────────────
_svg.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}mm" '
            f'height="{H}mm" viewBox="0 0 {W} {H}">')
_svg.append(f'<rect x="0" y="0" width="{W}" height="{H}" fill="#FFFFFF"/>')
rect(6, 6, W - 12, H - 12, fill="none", stroke=LINE, sw=0.8)
rect(8, 8, W - 16, H - 16, fill="none", stroke=LINE, sw=0.3)

M = 12
if PARAM:
    text(W / 2, 5, "LABEL-AUTHORING GUIDE  ·  ${PARAM} = shared parameter to bind  ·  (… manual) = free text",
         size=2.6, fill=PARAM_BLUE, weight="bold", anchor="middle")

# ── Header identity band ─────────────────────────────────────────────
hb_y, hb_h = 10, 42
rect(M, hb_y, W - 2 * M, hb_h, fill=BAR, stroke=LINE, sw=0.5)
seg = [M, 250, 470, 560, 678, W - M]
seg_defs = [
    ("CONSULTING ENGINEERS  ·  LEAD APPOINTED PARTY", "PRJ_TB_COMPANY_NAME_TXT"),
    ("DOCUMENT No.", "STING_SHEET_FULL_REF_TXT"),
    ("ISSUE DATE", "PRJ_TB_REVISION_DATE_TXT"),
    ("SUIT · REV", "PRJ_DWG_SUITABILITY_COD_TXT"),
    ("DISCIPLINE", "PRJ_TB_DISCIPLINE_TXT"),
]
for i, (lab, param) in enumerate(seg_defs):
    x0 = seg[i]
    if i:
        line(x0, hb_y, x0, hb_y + hb_h, LINE, 0.5)
    text(x0 + 3, hb_y + 6, lab, size=2.4, weight="bold", spacing="0.2")
    ptok(x0 + 3, hb_y + 11, param)
    if i == 3:   # SUIT · REV carries two params
        ptok(x0 + 3, hb_y + 15, "PRJ_TB_REVISION_NR_TXT")

# ── PROJECT block ────────────────────────────────────────────────────
pj_y, pj_h = 58, 74
text(M + 1, pj_y + 5, "PROJECT", size=3.0, weight="bold", fill=SUB, spacing="0.5")
line(M + 1, pj_y + 30, 440, pj_y + 30, HAIR, 0.5)
text(M + 1, pj_y + 27, "PROJECT NAME", size=6.5, weight="bold", fill="#C6CACF")
ptok(M + 1, pj_y + 34, "PRJ_ORG_PROJECT_NAME_TXT", size=2.1)
pj_meta = [("PROJECT No.", M + 1, "PRJ_ORG_PROJECT_NUMBER_TXT"),
           ("CLIENT", M + 112, "PRJ_TB_CLIENT_NAME_TXT"),
           ("WORK STAGE", M + 224, "PRJ_ORG_RIBA_STAGE_TXT"),
           ("SITE / ADDRESS", M + 300, "PRJ_ORG_PROJECT_ADDRESS_TXT")]
for lab, mx, param in pj_meta:
    kv(mx, pj_y + 50, lab, param, rule_w=96)
line(M, pj_y + pj_h, 450, pj_y + pj_h, HAIR, 0.4)

# ── IMAGE RENDER slot — exact 16:9 ───────────────────────────────────
rn_x, rn_y = 456, pj_y
rn_w = (W - M) - rn_x
rn_h = rn_w * 9.0 / 16.0
rect(rn_x, rn_y, rn_w, rn_h, fill="#F4F5F6", stroke=LINE, sw=0.5)
line(rn_x + rn_w / 2 - 4, rn_y + rn_h / 2, rn_x + rn_w / 2 + 4, rn_y + rn_h / 2, HAIR, 0.4)
line(rn_x + rn_w / 2, rn_y + rn_h / 2 - 4, rn_x + rn_w / 2, rn_y + rn_h / 2 + 4, HAIR, 0.4)
text(rn_x + 3, rn_y + 6, "IMAGE RENDER", size=3.2, weight="bold", spacing="0.3")
text(rn_x + rn_w - 3, rn_y + 6, "16 : 9", size=3.0, weight="bold", fill=NEW_EDGE, anchor="end")
text(rn_x + rn_w / 2, rn_y + rn_h - 4,
     f"{rn_w:.0f} × {rn_h:.0f} mm   ·   1920 × 1080 px @ min 150 dpi",
     size=2.6, fill=SUB, anchor="middle")

# ── DOCUMENT CONTROL ─────────────────────────────────────────────────
dc_x, dc_y, dc_w, dc_h = M, 138, 285, 118
panel(dc_x, dc_y, dc_w, dc_h, "DOCUMENT CONTROL")
box_y, box_h = dc_y + 12, 20
bw = (dc_w - 8) / 3 - 2
box_defs = [("SUITABILITY", "PRJ_DWG_SUITABILITY_COD_TXT"),
            ("STATUS", "PRJ_TB_DELIVERABLE_STATUS_TXT"),
            ("REVISION", "PRJ_TB_REVISION_NR_TXT")]
for i, (lab, param) in enumerate(box_defs):
    bx = dc_x + 3 + i * (bw + 3)
    rect(bx, box_y, bw, box_h, fill="#EFEFEF", stroke=HAIR, sw=0.4)
    text(bx + 2, box_y + 5, lab, size=2.6, weight="bold", spacing="0.2")
    ptok(bx + 2, box_y + 10, param)
gx_l, gx_r = dc_x + 4, dc_x + dc_w * 0.55
gy, row_dy = box_y + box_h + 9, 15
left_col = [("PURPOSE", "PRJ_DWG_ISSUE_PURPOSE_TXT"), ("SECURITY", "PRJ_ORG_SECURITY_CLASS_TXT"),
            ("DRAWN", "PRJ_TB_DRAWN_BY_TXT"), ("DATE", "PRJ_TB_DATE_DRAWN_TXT")]
right_col = [("LOD", "STING_LOIN_LOD_TXT"), ("AUTHORISED", "STING_AUTHORISED_BY_TXT"),
             ("CHECKED", "PRJ_TB_CHECKED_BY_TXT"), ("APPROVED", "PRJ_TB_APVD_BY_TXT")]
for i, (lab, param) in enumerate(left_col):
    kv(gx_l, gy + i * row_dy, lab, param, rule_w=dc_w * 0.40)
for i, (lab, param) in enumerate(right_col):
    yy = gy + i * row_dy
    if lab == "AUTHORISED":
        highlight(gx_r - 1.5, yy - 4.5, dc_w * 0.34, 6.5)
        new_tag(gx_r + dc_w * 0.30, yy)
    kv(gx_r, yy, lab, param, rule_w=dc_w * 0.30)
qr = 20
qx, qy = dc_x + dc_w - qr - 4, dc_y + dc_h - qr - 5
text(qx + qr / 2, qy - 2, "SCAN — VERIFY ISSUE", size=2.1, fill=SUB, anchor="middle")
rect(qx, qy, qr, qr, fill="#FFFFFF", stroke=LINE, sw=0.4)

# ── BIM & COORDINATION ───────────────────────────────────────────────
bm_x, bm_y = dc_x + dc_w + 6, 138
bm_w, bm_h = 448 - bm_x, 118
panel(bm_x, bm_y, bm_w, bm_h, "BIM & COORDINATION")
grid = [("CDE STATE", "PRJ_TB_DELIVERABLE_CDE_TXT", "FEDERATION", "STING_FEDERATION_STATUS_TXT"),
        ("COORD SYS", "(coord system — manual)", "GROUND LVL", "STING_SHEET_LEVEL_TXT"),
        ("PROJECT N", "(project north — manual)", "CLASS", "(Uniclass — manual)"),
        ("REGION", "(region — manual)", "VOLUME", "STING_SHEET_VOLUME_TXT")]
gxl, gxr = bm_x + 4, bm_x + bm_w * 0.52
gyy, dy = bm_y + 12, 9.5
for i, (l, lp, r, rp) in enumerate(grid):
    yy = gyy + i * dy
    kv(gxl, yy, l, lp)
    if r == "VOLUME":
        highlight(gxr - 1.5, yy - 4.2, bm_w * 0.44, 6.2)
        new_tag(gxr + bm_w * 0.30, yy)
    kv(gxr, yy, r, rp)
yy = gyy + 4 * dy
line(bm_x + 3, yy - 2, bm_x + bm_w - 3, yy - 2, HAIR, 0.3)
text(bm_x + 4, yy + 3, "Naming standard:  BS EN ISO 19650-2 : 2018", size=2.4, fill=SUB)
text(bm_x + 4, yy + 8, "Units mm  ·  Revit 2025  ·  IFC 4", size=2.4, fill=SUB)
line(bm_x + 3, yy + 11, bm_x + bm_w - 3, yy + 11, HAIR, 0.3)
text(bm_x + 4, yy + 16, "DISCIPLINE COLOUR (hex)", size=2.4, weight="bold", spacing="0.2")
sw_gap = (bm_w - 8) / len(DISCIPLINES)
sw_s = min(7.0, sw_gap - 2)
for i, (name, code, col) in enumerate(DISCIPLINES):
    cx = bm_x + 4 + i * sw_gap
    cy = yy + 20
    text(cx + sw_s / 2, cy - 0.8, code, size=1.8, anchor="middle", fill=SUB)
    rect(cx, cy, sw_s, sw_s, fill=col, stroke=LINE, sw=0.3)
    text(cx + sw_s / 2, cy + sw_s + 3, col, size=1.9, anchor="middle", weight="bold")

# ── REVISION HISTORY (native revision schedule) ──────────────────────
li_x, li_y, li_w, li_h = M, 264, 246, 200
panel(li_x, li_y, li_w, li_h, "REVISION HISTORY", right_tag="NATIVE REVISION SCHEDULE")
c_rev, c_desc = li_x + 3, li_x + 17
c_suit, c_date, c_by = li_x + li_w - 66, li_x + li_w - 44, li_x + li_w - 15
rh = [("REV", c_rev, "Revision Number"), ("DESCRIPTION", c_desc, "Revision Description"),
      ("SUIT", c_suit, "Issued to ⇒ rename"), ("DATE", c_date, "Revision Date"),
      ("BY", c_by, "Issued by")]
for c, cx, fld in rh:
    text(cx, li_y + 13, c, size=2.3, weight="bold", fill=SUB)
    if PARAM:
        text(cx, li_y + 16.5, fld, size=1.55, fill=PARAM_BLUE)
line(li_x + 2, li_y + (18 if PARAM else 15), li_x + li_w - 2, li_y + (18 if PARAM else 15), LINE, 0.4)
for cx in (c_desc - 2, c_suit - 2, c_date - 2, c_by - 2):
    line(cx, li_y + 8, cx, li_y + li_h - 3, "#E4E6E9", 0.25)
rh_rows = 14
rh_step = (li_h - 24) / rh_rows
for i in range(rh_rows + 1):
    line(li_x + 2, li_y + 21 + i * rh_step, li_x + li_w - 2, li_y + 21 + i * rh_step, "#E4E6E9", 0.25)

# ── WHAT CHANGED IN THIS ISSUE ───────────────────────────────────────
wc_x, wc_y, wc_w, wc_h = li_x + li_w + 6, 264, 186, 80
panel(wc_x, wc_y, wc_w, wc_h, "WHAT CHANGED IN THIS ISSUE")
ptok(wc_x + 3, wc_y + 13, "PRJ_TB_REVISION_DESCRIPTION_TXT")

# ── STANDARDS & REFERENCES ───────────────────────────────────────────
st_x, st_y, st_w, st_h = wc_x, wc_y + wc_h + 6, 186, 114
panel(st_x, st_y, st_w, st_h, "STANDARDS & REFERENCES", right_tag="static legend")
for c, cx in [("REF / STANDARD", st_x + 3), ("SCOPE", st_x + 95), ("USED IN", st_x + st_w - 30)]:
    text(cx, st_y + 13, c, size=2.3, weight="bold", fill=SUB)
line(st_x + 2, st_y + 15, st_x + st_w - 2, st_y + 15, HAIR, 0.3)
std_rows = [("BS EN ISO 19650-2", "Info mgmt", "All"), ("BS EN ISO 19650-5", "Security", "All"),
            ("Uniclass 2015", "Classification", "All"), ("BS 7671:2018+A2", "Wiring regs", "Power"),
            ("BS EN 62305 §4.3", "Lightning", "E-4001"), ("BS 5839-1 §14", "Fire detect", "E-5001"),
            ("BS EN 12464-1", "Lighting", "E-1101"), ("CIBSE Guide K", "Electricity", "E-2001")]
for i, (a, b, c) in enumerate(std_rows):
    yy = st_y + 23 + i * 11.2
    text(st_x + 3, yy, a, size=2.4)
    text(st_x + 95, yy, b, size=2.4, fill=SUB)
    text(st_x + st_w - 30, yy, c, size=2.4, fill=SUB)

# ── DRAWING REGISTER (right) ─────────────────────────────────────────
dr_x, dr_y = 456, 278
dr_w, dr_h = (W - M) - dr_x, 258
panel(dr_x, dr_y, dr_w, dr_h, "DRAWING REGISTER", right_tag="LIVE")
if PARAM:
    text(dr_x + dr_w / 2, dr_y + 6, "Revit schedule placed at print — ExportSheetRegister",
         size=2.0, fill=PARAM_BLUE, anchor="middle")
for c, cx in [("SHEET No.", dr_x + 4), ("TITLE", dr_x + 70), ("REV", dr_x + dr_w - 14)]:
    text(cx, dr_y + 14, c, size=2.6, weight="bold", fill=SUB)
line(dr_x + 3, dr_y + 16, dr_x + dr_w - 3, dr_y + 16, HAIR, 0.3)
for i in range(21):
    line(dr_x + 3, dr_y + 26 + i * 10.6, dr_x + dr_w - 3, dr_y + 26 + i * 10.6, "#E4E6E9", 0.25)

# ── PROJECT DIRECTORY ────────────────────────────────────────────────
pd_x, pd_y, pd_w, pd_h = M, 470, 440, 68
panel(pd_x, pd_y, pd_w, pd_h, "PROJECT DIRECTORY — STAKEHOLDERS & CONSULTANTS")
cards = [("CLIENT", "PRJ_TB_CLIENT_NAME_TXT"), ("ARCHITECT / LEAD", "PRJ_TB_COMPANY_NAME_TXT"),
         ("STRUCTURAL", "PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT"),
         ("MEP ENGINEER", "PRJ_TB_MEP_CONSULTANTS_NAME_TXT"),
         ("CONTRACTOR", "PRJ_TB_CONTRACTOR_NAME_TXT"), ("INFO MANAGER", "(info manager — manual)")]
ncard = len(cards)
gap = 2.5
cw = (pd_w - 6 - gap * (ncard - 1)) / ncard
for i, (role, param) in enumerate(cards):
    cx = pd_x + 3 + i * (cw + gap)
    rect(cx, pd_y + 11, cw, pd_h - 15, fill=CARD, stroke=HAIR, sw=0.4)
    text(cx + 1.6, pd_y + 16, role, size=2.1, weight="bold", spacing="0.1")
    line(cx + 1.6, pd_y + 18, cx + cw - 1.6, pd_y + 18, HAIR, 0.3)
    ptok(cx + 1.6, pd_y + 22, param)
    for j, sub in enumerate(["Company", "Contact", "Tel · Email"]):
        sy = pd_y + 27 + j * 8.5
        text(cx + 1.6, sy, sub, size=1.9, fill=SUB)
        line(cx + 1.6, sy + 2, cx + cw - 1.6, sy + 2, "#C9CCD0", 0.25)

# ── DELIVERABLE / ISSUE RIBBON (bottom) ──────────────────────────────
rb_y, rb_h = 546, 28
rect(M, rb_y, W - 2 * M, rb_h, fill="#FFFFFF", stroke=LINE, sw=0.5)
rect(M, rb_y, W - 2 * M, 6.5, fill=BAR, stroke=LINE, sw=0.5)
text(M + 2.5, rb_y + 4.9, "DELIVERABLE / ISSUE RIBBON", size=2.8, weight="bold", spacing="0.3")
ribbon = [("FULL REF", "STING_SHEET_FULL_REF_TXT"), ("DELIVERABLE STATUS", "PRJ_TB_DELIVERABLE_STATUS_TXT"),
          ("CDE STATE", "PRJ_TB_DELIVERABLE_CDE_TXT"), ("DATA DROP", "PRJ_TB_DELIVERABLE_DATADROP_TXT"),
          ("LAST TRANSMITTAL", "PRJ_TB_LAST_TRANSMITTAL_TXT"), ("AUTHORISED BY", "STING_AUTHORISED_BY_TXT"),
          ("AUTH DATE", "STING_AUTHORISED_DATE_TXT"), ("SHEET x OF y", "STING_SHEET_OF_TOTAL_TXT"),
          ("PAPER · SCALE", "PRJ_TB_PAPER_SZ_TXT"), ("NOTES / LEGEND", "PRJ_TB_NOTES_LEGEND_REF_TXT")]
seg_w = (W - 2 * M) / len(ribbon)
for i, (cap, param) in enumerate(ribbon):
    sx = M + i * seg_w
    if i:
        line(sx, rb_y + 7, sx, rb_y + rb_h, LINE, 0.4)
    text(sx + 2, rb_y + 12, cap, size=2.1, weight="bold", fill=SUB)
    ptok(sx + 2, rb_y + 16.5, param)
    line(sx + 2, rb_y + 22, sx + seg_w - 2, rb_y + 22, HAIR, 0.3)

_svg.append("</svg>")

# ── Write ────────────────────────────────────────────────────────────
here = os.path.dirname(os.path.abspath(__file__))
base = "cover_v8_PARAM_GUIDE" if PARAM else "cover_v8_SPEC_preview"
svg_path = os.path.join(here, "previews", base + ".svg")
os.makedirs(os.path.dirname(svg_path), exist_ok=True)
svg_doc = "\n".join(_svg)
with open(svg_path, "w") as f:
    f.write(svg_doc)
print("wrote", svg_path, f"({len(svg_doc)} bytes, params={PARAM}, annotate={ANNOTATE})")

try:
    import cairosvg
    cairosvg.svg2png(bytestring=svg_doc.encode(), write_to=svg_path.replace(".svg", ".png"),
                     output_width=2600)
    print("wrote", svg_path.replace(".svg", ".png"))
except Exception as e:                       # pragma: no cover
    print("PNG skipped:", e)
