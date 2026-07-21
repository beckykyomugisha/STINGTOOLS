#!/usr/bin/env python3
"""
cover_v8_spec.py — STING A1-landscape project COVER sheet, spec preview.

Renders the "cover v8" information-dense front sheet the design team is
iterating on (DOCUMENT No / DOCUMENT CONTROL / BIM & COORDINATION /
LATEST ISSUES / WHAT CHANGED / STANDARDS & REFERENCES / DRAWING REGISTER
/ PROJECT DIRECTORY + a 16:9 IMAGE RENDER slot).

Outputs a crisp hand-authored SVG (repo convention, see
docs/title_blocks/previews/*.svg) plus a rasterised PNG preview.

This preview differs from the committed Revit family
STING_TB_COVER_A1_v1.0 (a simpler banner cover in STING_TITLE_BLOCKS.json);
it is a *design study* for review, not a family generator.

All original blocks from the reference sheet are retained. Edits +
enrichment applied (the two grid edits are optionally highlighted):
  1. BIM & COORDINATION — 8th field added so the key/value grid is a
     clean 4x2 (REGION now pairs with VOLUME); LV/DATA swatch added.
  2. DOCUMENT CONTROL  — a field added to the right column (REVIEWED) so
     the left/right columns have equal rows.
  3. REVISION HISTORY  — the former "LATEST ISSUES" panel is now a full
     issue register (15 rows + SUIT column), since the block has room
     for the whole history rather than only the last few.
  4. PROJECT block, STANDARDS, and PROJECT DIRECTORY enriched with the
     metadata / rows / roles the extra space allows.

Discipline swatch colours are the *definitive* STING reference from
StingColorRegistry.Disciplines (StingTools/Tags/LegendBuilderCommands.cs)
— proving the swatches can be driven straight from the discipline code.

Usage:  python3 docs/title_blocks/cover_v8_spec.py [--no-annotate]
"""
import os
import sys

# ── Sheet ────────────────────────────────────────────────────────────
W, H = 841.0, 594.0            # A1 landscape, mm (1 svg unit = 1 mm)

# ── Palette ──────────────────────────────────────────────────────────
INK      = "#1A1A1A"
SUB      = "#5A5F66"           # secondary label grey
BAR      = "#C4C7CC"           # panel title-bar / header fill
CARD     = "#DADCDF"           # directory card fill
LINE     = "#3A3E44"           # frame stroke
HAIR     = "#9AA0A6"           # hairline
NEW_BG   = "#FFF3C4"           # highlight for the two added rows
NEW_EDGE = "#E0A800"

# ── Discipline colours — DEFINITIVE reference ────────────────────────
# StingColorRegistry.Disciplines (LegendBuilderCommands.cs):
#   M(0,128,255) E(255,200,0) P(0,180,0) A(160,160,160)
#   S(200,0,0) FP(255,100,0) LV(160,0,200) G(128,80,0)
# COMBINED/federated is NOT in the registry — proposed neutral graphite.
DISCIPLINES = [
    ("ARCH",     "#A0A0A0"),   # A  — Architectural
    ("STRUCT",   "#C80000"),   # S  — Structural
    ("MECH",     "#0080FF"),   # M  — Mechanical
    ("ELEC",     "#FFC800"),   # E  — Electrical
    ("PLUMB",    "#00B400"),   # P  — Plumbing
    ("FIRE",     "#FF6400"),   # FP — Fire protection
    ("LV/DATA",  "#A000C8"),   # LV — Low voltage / data
    ("COMBINED", "#3C3C3C"),   # federated (proposed — add to registry)
]

ANNOTATE = "--no-annotate" not in sys.argv
_svg = []


# ── SVG helpers ──────────────────────────────────────────────────────
def esc(s):
    return (str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def rect(x, y, w, h, fill="none", stroke=LINE, sw=0.4, rx=0, extra=""):
    r = f' rx="{rx}"' if rx else ""
    _svg.append(f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" '
                f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{r} {extra}/>')


def line(x1, y1, x2, y2, stroke=HAIR, sw=0.3, dash=""):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    _svg.append(f'<line x1="{x1:.2f}" y1="{y1:.2f}" x2="{x2:.2f}" y2="{y2:.2f}" '
                f'stroke="{stroke}" stroke-width="{sw}"{d}/>')


def text(x, y, s, size=2.6, fill=INK, weight="normal", anchor="start",
         spacing=None, family="Helvetica, Arial, sans-serif"):
    ls = f' letter-spacing="{spacing}"' if spacing else ""
    _svg.append(f'<text x="{x:.2f}" y="{y:.2f}" font-family="{family}" '
                f'font-size="{size}" fill="{fill}" font-weight="{weight}" '
                f'text-anchor="{anchor}"{ls}>{esc(s)}</text>')


def panel(x, y, w, h, title, right_tag=None, bar=7.5):
    """White panel with a grey title bar."""
    rect(x, y, w, h, fill="#FFFFFF", stroke=LINE, sw=0.5)
    rect(x, y, w, bar, fill=BAR, stroke=LINE, sw=0.5)
    text(x + 2.5, y + bar - 2.2, title, size=3.5, weight="bold", spacing="0.3")
    if right_tag:
        text(x + w - 2.5, y + bar - 2.2, right_tag, size=2.6, weight="bold",
             fill=SUB, anchor="end")


def kv(x, y, label):
    """Key label with a blank value rule beneath it."""
    text(x, y, label, size=2.5, fill=SUB, spacing="0.2")


def highlight(x, y, w, h):
    if ANNOTATE:
        rect(x, y, w, h, fill=NEW_BG, stroke=NEW_EDGE, sw=0.4, rx=0.8)


def new_tag(x, y):
    if ANNOTATE:
        rect(x, y - 3.0, 8.5, 3.6, fill=NEW_EDGE, stroke="none", rx=0.6)
        text(x + 4.25, y - 0.5, "NEW", size=2.3, fill="#FFFFFF",
             weight="bold", anchor="middle")


# ── Build ────────────────────────────────────────────────────────────
_svg.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}mm" '
            f'height="{H}mm" viewBox="0 0 {W} {H}">')
_svg.append(f'<rect x="0" y="0" width="{W}" height="{H}" fill="#FFFFFF"/>')

# Outer frame
rect(6, 6, W - 12, H - 12, fill="none", stroke=LINE, sw=0.8)
rect(8, 8, W - 16, H - 16, fill="none", stroke=LINE, sw=0.3)

M = 12                         # working margin

# ── Header band ──────────────────────────────────────────────────────
hb_y, hb_h = 10, 42
rect(M, hb_y, W - 2 * M, hb_h, fill=BAR, stroke=LINE, sw=0.5)
seg = [M, 250, 470, 560, 678, W - M]
seg_labels = ["CONSULTING ENGINEERS  ·  LEAD APPOINTED PARTY",
              "DOCUMENT No.", "ISSUE DATE", "SUIT · REV", "DISCIPLINE"]
for i, lab in enumerate(seg_labels):
    x0 = seg[i]
    if i:
        line(x0, hb_y, x0, hb_y + hb_h, stroke=LINE, sw=0.5)
    text(x0 + 3, hb_y + 6, lab, size=2.4, weight="bold", spacing="0.2")

# ── PROJECT block (top-left) — enriched with project metadata ────────
pj_y, pj_h = 58, 74
text(M + 1, pj_y + 5, "PROJECT", size=3.0, weight="bold", fill=SUB, spacing="0.5")
# large project-name placeholder rule
line(M + 1, pj_y + 30, 440, pj_y + 30, stroke=HAIR, sw=0.5)
text(M + 1, pj_y + 27, "PROJECT NAME", size=6.5, weight="bold", fill="#C6CACF")
# metadata strip — 4 cells across the project block
pj_meta = [("PROJECT No.", M + 1), ("CLIENT", M + 112),
           ("WORK STAGE", M + 224), ("SITE / ADDRESS", M + 300)]
for lab, mx in pj_meta:
    kv(mx, pj_y + 50, lab)
    line(mx, pj_y + 52.6, mx + 96, pj_y + 52.6, stroke=HAIR, sw=0.3)
line(M, pj_y + pj_h, 450, pj_y + pj_h, stroke=HAIR, sw=0.4)

# ── IMAGE RENDER slot (top-right) — snapped to 16:9 ──────────────────
rn_x, rn_y = 456, pj_y
rn_w = (W - M) - rn_x                       # 373 mm
rn_h = rn_w * 9.0 / 16.0                     # 209.8 mm  → exact 16:9
rect(rn_x, rn_y, rn_w, rn_h, fill="#F4F5F6", stroke=LINE, sw=0.5)
# corner ticks + centre cross to read as a render placeholder
for (cx, cy) in [(rn_x, rn_y), (rn_x + rn_w, rn_y),
                 (rn_x, rn_y + rn_h), (rn_x + rn_w, rn_y + rn_h)]:
    sx = 6 if cx == rn_x else -6
    sy = 6 if cy == rn_y else -6
    line(cx, cy, cx + sx, cy, stroke=HAIR, sw=0.5)
    line(cx, cy, cx, cy + sy, stroke=HAIR, sw=0.5)
line(rn_x + rn_w / 2 - 4, rn_y + rn_h / 2, rn_x + rn_w / 2 + 4,
     rn_y + rn_h / 2, stroke=HAIR, sw=0.4)
line(rn_x + rn_w / 2, rn_y + rn_h / 2 - 4, rn_x + rn_w / 2,
     rn_y + rn_h / 2 + 4, stroke=HAIR, sw=0.4)
text(rn_x + 3, rn_y + 6, "IMAGE RENDER", size=3.2, weight="bold", spacing="0.3")
text(rn_x + rn_w - 3, rn_y + 6, "16 : 9", size=3.0, weight="bold",
     fill=NEW_EDGE, anchor="end")
text(rn_x + rn_w / 2, rn_y + rn_h - 4,
     f"{rn_w:.0f} × {rn_h:.0f} mm   ·   1920 × 1080 px @ min 150 dpi",
     size=2.6, fill=SUB, anchor="middle")

# ── DOCUMENT CONTROL (edit #2: balance L/R columns) ──────────────────
dc_x, dc_y, dc_w, dc_h = M, 138, 285, 118
panel(dc_x, dc_y, dc_w, dc_h, "DOCUMENT CONTROL")
# 3 big pick boxes
box_y, box_h = dc_y + 12, 20
bw = (dc_w - 8) / 3 - 2
for i, lab in enumerate(["SUITABILITY", "STATUS", "REVISION"]):
    bx = dc_x + 3 + i * (bw + 3)
    rect(bx, box_y, bw, box_h, fill="#EFEFEF", stroke=HAIR, sw=0.4)
    text(bx + 2, box_y + 5, lab, size=2.6, weight="bold", spacing="0.2")
# 2-column key/value grid — NOW 4 rows each side
gx_l, gx_r = dc_x + 4, dc_x + dc_w * 0.55
gy = box_y + box_h + 9
row_dy = 15
left_col  = ["PURPOSE", "SECURITY", "DRAWN", "DATE"]
right_col = ["LOD", "REVIEWED", "CHECKED", "APPROVED"]   # REVIEWED = added
for i, lab in enumerate(left_col):
    yy = gy + i * row_dy
    kv(gx_l, yy, lab)
    line(gx_l, yy + 2.6, gx_l + dc_w * 0.40, yy + 2.6, stroke=HAIR, sw=0.3)
for i, lab in enumerate(right_col):
    yy = gy + i * row_dy
    if lab == "REVIEWED":
        highlight(gx_r - 1.5, yy - 4.5, dc_w * 0.34, 6.5)
        new_tag(gx_r + dc_w * 0.30, yy)
    kv(gx_r, yy, lab)
    line(gx_r, yy + 2.6, gx_r + dc_w * 0.30, yy + 2.6, stroke=HAIR, sw=0.3)
# QR box
qr = 20
qx, qy = dc_x + dc_w - qr - 4, dc_y + dc_h - qr - 5
text(qx + qr / 2, qy - 2, "SCAN — VERIFY ISSUE", size=2.1, fill=SUB, anchor="middle")
rect(qx, qy, qr, qr, fill="#FFFFFF", stroke=LINE, sw=0.4)

# ── BIM & COORDINATION (edit #1: complete the 4x2 grid) ──────────────
bm_x, bm_y, bm_w, bm_h = dc_x + dc_w + 6, 138, 448 - (dc_x + dc_w + 6), 118
panel(bm_x, bm_y, bm_w, bm_h, "BIM & COORDINATION")
grid = [("CDE STATE", "FEDERATION"),
        ("COORD SYS", "GROUND LVL"),
        ("PROJECT N", "CLASS"),
        ("REGION",    "VOLUME")]        # VOLUME = added → clean 4x2
gxl, gxr = bm_x + 4, bm_x + bm_w * 0.52
gyy = bm_y + 12
dy = 9.5
for i, (l, r) in enumerate(grid):
    yy = gyy + i * dy
    kv(gxl, yy, l)
    if r == "VOLUME":
        highlight(gxr - 1.5, yy - 4.2, bm_w * 0.44, 6.2)
        new_tag(gxr + bm_w * 0.30, yy)
    kv(gxr, yy, r)
yy = gyy + 4 * dy
line(bm_x + 3, yy - 2, bm_x + bm_w - 3, yy - 2, stroke=HAIR, sw=0.3)
text(bm_x + 4, yy + 3, "Naming standard:  BS EN ISO 19650-2 : 2018", size=2.4, fill=SUB)
text(bm_x + 4, yy + 8, "Units mm  ·  Revit 2025  ·  IFC 4", size=2.4, fill=SUB)
line(bm_x + 3, yy + 11, bm_x + bm_w - 3, yy + 11, stroke=HAIR, sw=0.3)
# Discipline colour swatches — driven from the definitive registry
text(bm_x + 4, yy + 16, "DISCIPLINE COLOUR", size=2.4, weight="bold", spacing="0.2")
sw_n = len(DISCIPLINES)
sw_gap = (bm_w - 8) / sw_n
sw_s = min(8.0, sw_gap - 2)
for i, (name, col) in enumerate(DISCIPLINES):
    cx = bm_x + 4 + i * sw_gap
    cy = yy + 19
    rect(cx, cy, sw_s, sw_s, fill=col, stroke=LINE, sw=0.3)
    text(cx + sw_s / 2, cy + sw_s + 3, name, size=1.9, anchor="middle")

# ── REVISION HISTORY (full register — the block has room for the ─────
#    whole issue history, not just the last 4) ─────────────────────────
li_x, li_y, li_w, li_h = M, 264, 246, 206
panel(li_x, li_y, li_w, li_h, "REVISION HISTORY", right_tag="FULL ISSUE LOG")
# column x-anchors
c_rev  = li_x + 3
c_desc = li_x + 17
c_suit = li_x + li_w - 66
c_date = li_x + li_w - 44
c_by   = li_x + li_w - 15
rh_cols = [("REV", c_rev), ("DESCRIPTION", c_desc),
           ("SUIT", c_suit), ("DATE", c_date), ("BY", c_by)]
for c, cx in rh_cols:
    text(cx, li_y + 13, c, size=2.3, weight="bold", fill=SUB)
line(li_x + 2, li_y + 15, li_x + li_w - 2, li_y + 15, stroke=LINE, sw=0.4)
# column separators
for cx in (c_desc - 2, c_suit - 2, c_date - 2, c_by - 2):
    line(cx, li_y + 8, cx, li_y + li_h - 3, stroke="#E4E6E9", sw=0.25)
# 15 empty history rows (was 4-line "latest issues")
rh_rows = 15
rh_step = (li_h - 20) / rh_rows
for i in range(rh_rows + 1):
    line(li_x + 2, li_y + 17 + i * rh_step, li_x + li_w - 2,
         li_y + 17 + i * rh_step, stroke="#E4E6E9", sw=0.25)

# ── WHAT CHANGED IN THIS ISSUE ───────────────────────────────────────
wc_x, wc_y, wc_w, wc_h = li_x + li_w + 6, 264, 186, 84
panel(wc_x, wc_y, wc_w, wc_h, "WHAT CHANGED IN THIS ISSUE")

# ── STANDARDS & REFERENCES ───────────────────────────────────────────
st_x, st_y, st_w, st_h = wc_x, wc_y + wc_h + 6, 186, 116
panel(st_x, st_y, st_w, st_h, "STANDARDS & REFERENCES")
sc = [("REF / STANDARD", st_x + 3), ("SCOPE", st_x + 95), ("USED IN", st_x + st_w - 30)]
for c, cx in sc:
    text(cx, st_y + 13, c, size=2.3, weight="bold", fill=SUB)
line(st_x + 2, st_y + 15, st_x + st_w - 2, st_y + 15, stroke=HAIR, sw=0.3)
rows = [("BS EN ISO 19650-2", "Info mgmt", "All"),
        ("BS EN ISO 19650-5", "Security", "All"),
        ("Uniclass 2015", "Classification", "All"),
        ("BS 7671:2018+A2", "Wiring regs", "Power"),
        ("BS EN 62305 §4.3", "Lightning", "E-4001"),
        ("BS 5839-1 §14", "Fire detect", "E-5001"),
        ("BS EN 12464-1", "Lighting", "E-1101"),
        ("CIBSE Guide K", "Electricity", "E-2001")]
for i, (a, b, c) in enumerate(rows):
    yy = st_y + 23 + i * 11.4
    text(st_x + 3, yy, a, size=2.4)
    text(st_x + 95, yy, b, size=2.4, fill=SUB)
    text(st_x + st_w - 30, yy, c, size=2.4, fill=SUB)

# ── DRAWING REGISTER (right) ─────────────────────────────────────────
dr_x, dr_y = 456, 278
dr_w, dr_h = (W - M) - dr_x, 270
panel(dr_x, dr_y, dr_w, dr_h, "DRAWING REGISTER", right_tag="LIVE")
dc_cols = [("SHEET No.", dr_x + 4), ("TITLE", dr_x + 70), ("REV", dr_x + dr_w - 14)]
for c, cx in dc_cols:
    text(cx, dr_y + 14, c, size=2.6, weight="bold", fill=SUB)
line(dr_x + 3, dr_y + 16, dr_x + dr_w - 3, dr_y + 16, stroke=HAIR, sw=0.3)
for i in range(22):
    line(dr_x + 3, dr_y + 26 + i * 10.6, dr_x + dr_w - 3, dr_y + 26 + i * 10.6,
         stroke="#E4E6E9", sw=0.25)

# ── PROJECT DIRECTORY — enriched: 6 ISO 19650 roles + contact lines ──
pd_x, pd_y, pd_w, pd_h = M, 478, 440, 74
panel(pd_x, pd_y, pd_w, pd_h, "PROJECT DIRECTORY — STAKEHOLDERS & CONSULTANTS")
cards = ["CLIENT", "ARCHITECT", "STRUCTURAL", "MEP ENGINEER",
         "INFO MANAGER", "PRINCIPAL DESIGNER"]
n = len(cards)
gap = 2.5
cw = (pd_w - 6 - gap * (n - 1)) / n
for i, c in enumerate(cards):
    cx = pd_x + 3 + i * (cw + gap)
    rect(cx, pd_y + 11, cw, pd_h - 15, fill=CARD, stroke=HAIR, sw=0.4)
    text(cx + 1.6, pd_y + 16, c, size=2.2, weight="bold", spacing="0.1")
    line(cx + 1.6, pd_y + 18, cx + cw - 1.6, pd_y + 18, stroke=HAIR, sw=0.3)
    for j, sub in enumerate(["Company", "Contact", "Tel · Email"]):
        sy = pd_y + 24 + j * 11
        text(cx + 1.6, sy, sub, size=1.9, fill=SUB)
        line(cx + 1.6, sy + 2, cx + cw - 1.6, sy + 2, stroke="#C9CCD0", sw=0.25)

_svg.append("</svg>")

# ── Write ────────────────────────────────────────────────────────────
here = os.path.dirname(os.path.abspath(__file__))
svg_path = os.path.join(here, "previews", "cover_v8_SPEC_preview.svg")
os.makedirs(os.path.dirname(svg_path), exist_ok=True)
svg_doc = "\n".join(_svg)
with open(svg_path, "w") as f:
    f.write(svg_doc)
print("wrote", svg_path, f"({len(svg_doc)} bytes, annotate={ANNOTATE})")

try:
    import cairosvg
    png_path = svg_path.replace(".svg", ".png")
    cairosvg.svg2png(bytestring=svg_doc.encode(), write_to=png_path,
                     output_width=2400)
    print("wrote", png_path)
except Exception as e:                       # pragma: no cover
    print("PNG skipped:", e)
