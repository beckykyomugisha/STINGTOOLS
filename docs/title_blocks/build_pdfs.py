"""Generate STING title block visual artifacts (multi-page PDF).

Pages:
  1. Locator map — A1 landscape with every field numbered + legend.
  2. A1 landscape working sheet — enlarged, every cell labelled.
  3. A4 portrait working sheet — enlarged.
  4. A1 cover page.
  5. Slot positions diagram (normalised 0..1 layouts).
  6. Match-line details.

Output:  docs/title_blocks/STING_TITLE_BLOCK_DESIGN.pdf
"""

from reportlab.lib.pagesizes import A3, landscape
from reportlab.lib.colors import HexColor, black, white, grey
from reportlab.pdfgen import canvas
from reportlab.lib.units import mm

OUT = "docs/title_blocks/STING_TITLE_BLOCK_DESIGN.pdf"
PAGE = landscape(A3)            # 420 × 297 mm working canvas
PW, PH = PAGE                    # points (1 mm = 2.83465 pt)

# Brand palette
INK         = HexColor("#000000")
INK_SOFT    = HexColor("#404040")
INK_LIGHT   = HexColor("#808080")
PAPER       = HexColor("#FFFFFF")
ACCENT      = HexColor("#1976D2")
WARN        = HexColor("#E6A800")
ERROR       = HexColor("#E60000")
OK          = HexColor("#2E7D32")
CHIP_BG     = HexColor("#FFE0B2")
TBLOCK_FILL = HexColor("#F4F4F4")
LEGEND_BG   = HexColor("#FAFAFA")
GRID_LINE   = HexColor("#D0D0D0")
SLOT_FILL   = HexColor("#E3F2FD")


# ── helpers ──────────────────────────────────────────────────────────

def hline(c, x1, y, x2, w=0.4, col=INK):
    c.setStrokeColor(col); c.setLineWidth(w); c.line(x1, y, x2, y)

def vline(c, x, y1, y2, w=0.4, col=INK):
    c.setStrokeColor(col); c.setLineWidth(w); c.line(x, y1, x, y2)

def rect(c, x, y, w, h, stroke=INK, fill=None, lw=0.4):
    c.setStrokeColor(stroke); c.setLineWidth(lw)
    if fill is not None:
        c.setFillColor(fill); c.rect(x, y, w, h, stroke=1, fill=1)
    else:
        c.rect(x, y, w, h, stroke=1, fill=0)

def text(c, x, y, s, size=8, col=INK, font="Helvetica", anchor="left"):
    c.setFillColor(col); c.setFont(font, size)
    if anchor == "center":
        c.drawCentredString(x, y, s)
    elif anchor == "right":
        c.drawRightString(x, y, s)
    else:
        c.drawString(x, y, s)

def cell(c, x, y, w, h, label="", value="", lcol=INK_LIGHT, vcol=INK,
         lsize=5.5, vsize=8.5, fill=None):
    rect(c, x, y, w, h, stroke=INK_SOFT, fill=fill, lw=0.3)
    if label:
        text(c, x + 1.2*mm, y + h - 2.0*mm, label, size=lsize, col=lcol)
    if value:
        text(c, x + 1.2*mm, y + 1.2*mm, value, size=vsize, col=vcol,
             font="Helvetica-Bold")

def chip(c, x, y, w, h, label, value, fill=CHIP_BG):
    rect(c, x, y, w, h, stroke=INK_SOFT, fill=fill, lw=0.3)
    text(c, x + w/2, y + h - 2.5*mm, label, size=5, col=INK_SOFT,
         anchor="center")
    text(c, x + w/2, y + 1.5*mm, value, size=8, col=INK,
         font="Helvetica-Bold", anchor="center")

def page_frame(c, title, subtitle=""):
    """Draw page-edge frame + corner header."""
    rect(c, 6*mm, 6*mm, PW - 12*mm, PH - 12*mm, stroke=INK_LIGHT, lw=0.3)
    rect(c, 6*mm, PH - 18*mm, PW - 12*mm, 12*mm, fill=TBLOCK_FILL,
         stroke=INK_LIGHT, lw=0.3)
    text(c, 10*mm, PH - 12*mm, title, size=14, col=INK,
         font="Helvetica-Bold")
    if subtitle:
        text(c, 10*mm, PH - 16*mm, subtitle, size=8, col=INK_SOFT)
    text(c, PW - 10*mm, PH - 12*mm, "STING — Title Block Family Library",
         size=9, col=INK_SOFT, anchor="right",
         font="Helvetica-Bold")
    text(c, PW - 10*mm, PH - 16*mm, "Phase 167 / 168 — visual reference",
         size=7, col=INK_LIGHT, anchor="right")

def callout(c, x, y, n, col=ACCENT, r=2.7*mm):
    """Numbered callout circle."""
    c.setFillColor(col); c.setStrokeColor(white); c.setLineWidth(0.4)
    c.circle(x, y, r, stroke=1, fill=1)
    c.setFillColor(white); c.setFont("Helvetica-Bold", 7)
    c.drawCentredString(x, y - 2.2, str(n))


# ── Page 1: Locator map ──────────────────────────────────────────────

def page_locator(c):
    page_frame(c, "1.  LOCATOR MAP",
               "STING_TB_A1_v2.0 — A1 landscape working sheet")

    # Drawing area (A1 sheet representation, scaled to fit the page).
    # Use the full vertical space below the page-frame header.
    ox, oy = 10*mm, 14*mm
    sh = PH - 22*mm - 18*mm
    sw = sh * 841 / 594            # ratio-correct A1 (1:3.0)
    rect(c, ox, oy, sw, sh, stroke=INK, lw=0.7)

    # Top status band (full width, 6 mm)
    bw, bh = sw, 6*mm
    rect(c, ox, oy + sh - bh, bw, bh, fill=HexColor("#FFF8E1"),
         stroke=INK_SOFT, lw=0.3)
    text(c, ox + 2*mm, oy + sh - 4.5*mm,
         "STATUS  ▕  SUITABILITY  ▕  ISSUE PURPOSE  ▕  FEDERATION  ▕  LOIN/LOD",
         size=6, col=INK_SOFT, font="Helvetica-Bold")

    # Right-hand title strip (200 mm of 841 mm = 23.8% of width)
    strip_w = sw * 0.30
    strip_x = ox + sw - strip_w
    rect(c, strip_x, oy, strip_w, sh - bh, fill=TBLOCK_FILL,
         stroke=INK_SOFT, lw=0.3)

    # Drawable zone label
    drawable_w = sw - strip_w
    text(c, ox + drawable_w / 2, oy + (sh - bh) / 2 + 4,
         "DRAWABLE ZONE",
         size=11, col=INK_LIGHT, anchor="center",
         font="Helvetica-Bold")
    text(c, ox + drawable_w / 2, oy + (sh - bh) / 2 - 6,
         "(viewport area — scope-box / view crop / element-bbox)",
         size=7, col=INK_LIGHT, anchor="center")

    # Title strip rows (top → bottom): logo | originator | client+app |
    # project | coord+north | notes | drawing title | seg breakdown |
    # full ref | sheet-of | authoring | tr/cde/midp | qr+scale-bar
    strip_top = oy + sh - bh
    rows = [
        ("LOGO BAND",              14*mm),
        ("ORIGINATOR PRACTICE",    10*mm),
        ("CLIENT / APPOINTING",     8*mm),
        ("PROJECT",                12*mm),
        ("COORD / NORTH / KEY",    16*mm),
        ("NOTES + STATUTORY",      14*mm),
        ("DRAWING TITLE",           8*mm),
        ("ID — 7 SEGMENTS",         6*mm),
        ("FULL SHEET REF",          7*mm),
        ("SHEET-OF / SCALE / DATE", 6*mm),
        ("AUTHORING (DR/CK/AP/AU)", 8*mm),
        ("TR / CDE / MIDP",         8*mm),
        ("QR + DISCLAIMER",         9*mm),
    ]
    cur = strip_top
    for label, h in rows:
        cur -= h
        rect(c, strip_x, cur, strip_w, h, stroke=INK_SOFT, lw=0.3)
        text(c, strip_x + 1.5*mm, cur + h - 2.8*mm, label,
             size=5.5, col=INK_LIGHT, font="Helvetica-Bold")

    # Bottom edge revision history (full width, 14 mm)
    rev_h = 14*mm
    rect(c, ox, oy, sw, rev_h, stroke=INK_SOFT, lw=0.3,
         fill=HexColor("#FAFAFA"))
    text(c, ox + 2*mm, oy + rev_h - 3*mm,
         "REVISION HISTORY  —  Rev | Date | Description | Drwn | Chkd | Appr | Suit. | Stat.",
         size=6.5, col=INK_SOFT, font="Helvetica-Bold")

    # ── Numbered callouts ──
    # Coordinates picked at the centre of each labelled region.
    callouts = [
        ( 1, ox + sw*0.55, strip_top + bh + 4*mm,    "Top status band (5 chips)"),
        ( 2, strip_x + strip_w*0.30, strip_top - 7*mm,  "Originator + client logos"),
        ( 3, strip_x + strip_w*0.50, strip_top - 19*mm, "Originator practice block"),
        ( 4, strip_x + strip_w*0.50, strip_top - 28*mm, "Client + appointing party"),
        ( 5, strip_x + strip_w*0.50, strip_top - 38*mm, "Project name + address + RIBA"),
        ( 6, strip_x + strip_w*0.50, strip_top - 52*mm, "Coordinate sys + project north + key plan"),
        ( 7, strip_x + strip_w*0.50, strip_top - 67*mm, "Notes + CDM + copyright"),
        ( 8, strip_x + strip_w*0.50, strip_top - 81*mm, "Drawing title (sheet name)"),
        ( 9, strip_x + strip_w*0.50, strip_top - 88*mm, "ID seven-segment breakdown"),
        (10, strip_x + strip_w*0.50, strip_top - 95*mm, "Full concatenated sheet ref"),
        (11, strip_x + strip_w*0.50, strip_top -101*mm, "Sheet-of-total + scale + date"),
        (12, strip_x + strip_w*0.50, strip_top -108*mm, "Authoring 4-eyes (DR/CK/AP/AU)"),
        (13, strip_x + strip_w*0.50, strip_top -116*mm, "TR-ref + CDE path + MIDP id"),
        (14, strip_x + strip_w*0.50, strip_top -125*mm, "QR code + disclaimer + print bar"),
        (15, ox + sw*0.50, oy + rev_h/2,                  "Revision history (8 cols)"),
        (16, ox + 12*mm,  oy + rev_h/2,                   "Print-scale check bar"),
    ]
    for n, x, y, _ in callouts:
        callout(c, x, y, n)

    # Legend box (right of the page, outside the sheet)
    lx, ly, lw, lh = ox + sw + 4*mm, oy, PW - (ox + sw + 4*mm) - 8*mm, sh
    rect(c, lx, ly, lw, lh, fill=LEGEND_BG, stroke=INK_LIGHT, lw=0.3)
    text(c, lx + 2*mm, ly + lh - 5*mm, "LEGEND — field groups",
         size=9, col=INK, font="Helvetica-Bold")
    cy = ly + lh - 9*mm
    for n, _, _, descr in callouts:
        callout(c, lx + 4*mm, cy, n, r=2.3*mm)
        text(c, lx + 9*mm, cy - 2, descr, size=6.5, col=INK)
        cy -= 5.5*mm

    text(c, ox + sw/2, oy - 4*mm,
         "Sheet drawn at 1:3.36 of A1 (841 × 594 mm).  Right strip 30 % "
         "of width = 252 mm at full size (specced 200 mm — diagram "
         "exaggerated for legibility).",
         size=7, col=INK_LIGHT, anchor="center")

    c.showPage()


# ── Page 2: A1 landscape working sheet (enlarged) ────────────────────

def page_landscape(c):
    page_frame(c, "2.  A1 LANDSCAPE WORKING SHEET",
               "STING_TB_A1_v2.0 — every field labelled with bound parameter")

    ox, oy = 8*mm, 14*mm
    sw = PW - 16*mm
    sh = PH - 22*mm - 14*mm        # leave room for page-frame header
    rect(c, ox, oy, sw, sh, stroke=INK, lw=0.7)

    # Top status band (full width)
    bh = 8*mm
    by = oy + sh - bh
    rect(c, ox, by, sw, bh, fill=HexColor("#FFF8E1"),
         stroke=INK_SOFT, lw=0.3)
    cw = sw / 5
    chips = [
        ("STATUS",        "${STATUS}"),
        ("SUITABILITY",   "${SUITABILITY}"),
        ("ISSUE PURPOSE", "${ISSUE_PURPOSE}"),
        ("FEDERATION",    "${FEDERATION_STATUS}"),
        ("LOIN / LOD",    "${LOIN_LOD}"),
    ]
    for i, (lbl, val) in enumerate(chips):
        chip(c, ox + i*cw + 0.5*mm, by + 0.5*mm, cw - 1*mm, bh - 1*mm,
             lbl, val, fill=CHIP_BG)

    # Right title strip (30% of width)
    strip_w = sw * 0.30
    strip_x = ox + sw - strip_w
    strip_h = sh - bh
    rect(c, strip_x, oy, strip_w, strip_h, fill=TBLOCK_FILL,
         stroke=INK_SOFT, lw=0.3)

    # Drawable zone label
    drw_w = sw - strip_w
    text(c, ox + drw_w/2, oy + strip_h/2 + 8,
         "DRAWABLE ZONE — viewport area",
         size=14, col=INK_LIGHT, anchor="center",
         font="Helvetica-Bold")
    text(c, ox + drw_w/2, oy + strip_h/2 - 4,
         "DrawingType.Slots[] resolved by SheetTemplateEngine",
         size=8, col=INK_LIGHT, anchor="center")
    # Slot rectangles preview
    sx, sy = ox + 8*mm, oy + 8*mm
    drwh = strip_h - 16*mm
    drww = drw_w - 16*mm
    rect(c, sx, sy, drww*0.66, drwh, stroke=GRID_LINE, lw=0.3,
         fill=SLOT_FILL)
    text(c, sx + drww*0.33, sy + drwh/2,
         "MAIN_LEFT (0.00, 0.00, 0.66, 1.00)",
         size=7, col=INK_LIGHT, anchor="center")
    rect(c, sx + drww*0.66, sy, drww*0.34, drwh*0.5,
         stroke=GRID_LINE, lw=0.3, fill=SLOT_FILL)
    text(c, sx + drww*0.83, sy + drwh*0.75,
         "MAIN_RIGHT_TOP", size=6, col=INK_LIGHT, anchor="center")
    rect(c, sx + drww*0.66, sy + drwh*0.5, drww*0.34, drwh*0.5,
         stroke=GRID_LINE, lw=0.3, fill=SLOT_FILL)
    text(c, sx + drww*0.83, sy + drwh*0.25,
         "MAIN_RIGHT_BOT", size=6, col=INK_LIGHT, anchor="center")

    # Title strip rows from top down
    cur = by
    rows = [
        ("LOGO BAND",                        18*mm,
            [("ORIGINATOR LOGO  ${ORG_LOGO}", "left", 0.55),
             ("CLIENT LOGO  ${CL_LOGO}",      "right", 0.45)]),
        ("ORIGINATOR PRACTICE",              13*mm,
            [("${ORG_NAME}", None, 1.0),
             ("${ORG_ADDRESS}", None, 1.0)]),
        ("CLIENT  /  APPOINTING PARTY",      11*mm,
            [("${CLIENT_NAME}", "left",  0.5),
             ("${APPOINTING_PARTY}", "right", 0.5)]),
        ("PROJECT",                          18*mm,
            [("${PROJECT_NAME}", None, 1.0),
             ("${PROJECT_ADDRESS}", None, 1.0),
             ("Project no. ${PROJECT_NUMBER}    RIBA Stage ${RIBA_STAGE}", None, 1.0)]),
        ("COORDINATE  /  PROJECT NORTH  /  KEY PLAN", 22*mm,
            [("${COORD_SYS}    Datum ${GROUND_LVL}", None, 1.0),
             ("Project north ${PRJ_NORTH}    Key plan ↗", None, 1.0)]),
        ("NOTES",                            18*mm,
            [("1. Do not scale from this drawing.", None, 1.0),
             ("2. Read with arch / struct / MEP set.", None, 1.0),
             ("3. CDM HAZARD: ${CDM_HAZARD}", None, 1.0),
             ("4. ${COPYRIGHT}", None, 1.0)]),
        ("DRAWING TITLE",                    11*mm,
            [("${SHEET_NAME}", None, 1.0)]),
        ("ID — 7 SEGMENTS",                  9*mm,
            [("PRJ ${PROJ}  ORIG ${ORIG}  VOL ${VOL}  LVL ${LVL}  TYPE ${TYPE}  ROL ${ROL}  SEQ ${SEQ}",
              None, 1.0)]),
        ("FULL SHEET REF",                   10*mm,
            [("${SHEET_FULL_REF}", None, 1.0)]),
        ("SHEET-OF / SCALE / DATE",          9*mm,
            [("Sheet ${OF_TOTAL}    Scale ${SCALE} @ A1    Date ${REV_DATE}",
              None, 1.0)]),
        ("AUTHORING — 4-EYES",               12*mm,
            [("DR ${DR}  CK ${CK}  AP ${AP}  AU ${AU}    Rev ${REV}",
              None, 1.0)]),
        ("TR / CDE / MIDP",                  12*mm,
            [("TR-REF ${TRANSMITTAL_REF}", None, 1.0),
             ("CDE    ${CDE_PATH}", None, 1.0),
             ("MIDP   ${DELIVERABLE_ID}", None, 1.0)]),
        ("QR + DISCLAIMER",                  16*mm, None),
    ]
    for label, h, content in rows:
        cur -= h
        rect(c, strip_x, cur, strip_w, h, stroke=INK_SOFT, lw=0.3)
        text(c, strip_x + 1.5*mm, cur + h - 2.5*mm, label,
             size=5.5, col=INK_LIGHT, font="Helvetica-Bold")
        if content is None:
            # QR placeholder
            qrs = 12*mm
            rect(c, strip_x + 1.5*mm, cur + 1.5*mm, qrs, qrs,
                 fill=INK, stroke=INK)
            for i in range(7):
                for j in range(7):
                    if (i + j) % 3 == 0:
                        rect(c, strip_x + 1.5*mm + i*qrs/7,
                             cur + 1.5*mm + j*qrs/7, qrs/7, qrs/7,
                             fill=PAPER, stroke=PAPER)
            text(c, strip_x + qrs + 4*mm, cur + h - 6*mm,
                 "QR → planscape://", size=6, col=INK_SOFT)
            text(c, strip_x + qrs + 4*mm, cur + h - 9*mm,
                 "  project/{id}/sheet/{guid}", size=6, col=INK_SOFT)
            text(c, strip_x + qrs + 4*mm, cur + 2*mm,
                 "${COPYRIGHT} • Original A1", size=5, col=INK_LIGHT)
        else:
            ty = cur + h - 6*mm
            line_h = (h - 4*mm) / max(1, len(content))
            for line, anchor, w_frac in content:
                if anchor == "right":
                    text(c, strip_x + strip_w - 1.5*mm, ty, line,
                         size=7, col=INK, anchor="right",
                         font="Helvetica-Bold")
                else:
                    text(c, strip_x + 1.5*mm, ty, line, size=7, col=INK,
                         font="Helvetica-Bold")
                ty -= line_h

    # Bottom revision history (8 columns)
    rev_h = 18*mm
    cur -= rev_h
    rect(c, ox, oy + 0, sw, rev_h, stroke=INK_SOFT, lw=0.3,
         fill=HexColor("#FAFAFA"))
    text(c, ox + 2*mm, oy + rev_h - 3.5*mm,
         "REVISION HISTORY",
         size=7, col=INK_LIGHT, font="Helvetica-Bold")
    cols = ["REV", "DATE", "DESCRIPTION", "DRWN", "CHKD",
            "APPR", "SUIT.", "STAT."]
    cwidths = [12, 22, 80, 12, 12, 12, 12, 18]   # mm relative
    total = sum(cwidths)
    cx = ox + 2*mm
    cyh = oy + rev_h - 8*mm
    avail = sw - 4*mm
    for i, lbl in enumerate(cols):
        w = avail * cwidths[i] / total
        rect(c, cx, oy + 2*mm, w, rev_h - 8*mm, stroke=GRID_LINE,
             lw=0.2)
        text(c, cx + w/2, cyh, lbl, size=6.5, col=INK,
             anchor="center", font="Helvetica-Bold")
        cx += w
    # Print-scale check bar (left of revision)
    pbx, pby = ox + 4*mm, oy - 2*mm
    text(c, pbx, pby + 1*mm, "0", size=5, col=INK_LIGHT)
    for i in range(6):
        seg_w = 4*mm
        rect(c, pbx + 3*mm + i*seg_w, pby - 1*mm, seg_w, 1.5*mm,
             stroke=INK,
             fill=INK if i % 2 == 0 else PAPER)
        text(c, pbx + 3*mm + (i+1)*seg_w, pby + 1*mm,
             str((i+1)*5), size=5, col=INK_LIGHT, anchor="center")
    text(c, pbx + 3*mm + 7*seg_w, pby + 1*mm, "mm @ 1:1",
         size=5, col=INK_LIGHT)

    c.showPage()


# ── Page 3: A4 portrait working sheet ────────────────────────────────

def page_portrait(c):
    page_frame(c, "3.  A4 PORTRAIT WORKING SHEET",
               "STING_TB_A4_PORT_v1.0 — bottom strip layout for RFI / memos / single-page schedules")

    # Show the A4 portrait sheet at scale on the A3 landscape page.
    # A4 ratio 210:297 = 1:1.414. We want it to sit clearly on the page.
    # Keep it 200 mm tall, ~141 mm wide, centred.
    sh = 220*mm
    sw = sh * 210 / 297       # ≈ 156 mm
    ox = (PW - sw) / 2 - 60*mm
    oy = (PH - sh) / 2 - 6*mm
    rect(c, ox, oy, sw, sh, stroke=INK, lw=0.7)

    # Top status band (full width)
    bh = 7*mm
    by = oy + sh - bh
    rect(c, ox, by, sw, bh, fill=HexColor("#FFF8E1"),
         stroke=INK_SOFT, lw=0.3)
    cw = sw / 4
    chips = [
        ("STATUS",        "${STATUS}"),
        ("SUITABILITY",   "${SUITABILITY}"),
        ("REV",           "${REV}"),
        ("DATE",          "${REV_DATE}"),
    ]
    for i, (lbl, val) in enumerate(chips):
        chip(c, ox + i*cw + 0.4*mm, by + 0.4*mm, cw - 0.8*mm,
             bh - 0.8*mm, lbl, val, fill=CHIP_BG)

    # Drawable zone (top region)
    drwh = sh - bh - 110*mm
    rect(c, ox + 4*mm, oy + 110*mm, sw - 8*mm, drwh,
         stroke=GRID_LINE, lw=0.3, fill=SLOT_FILL)
    text(c, ox + sw/2, oy + 110*mm + drwh/2 + 4,
         "DRAWABLE ZONE",
         size=11, col=INK_LIGHT, anchor="center",
         font="Helvetica-Bold")
    text(c, ox + sw/2, oy + 110*mm + drwh/2 - 6,
         "Single MAIN slot   (0.0, 0.0, 1.0, 1.0)",
         size=7, col=INK_LIGHT, anchor="center")

    # Bottom title strip (110 mm tall)
    strip_y = oy
    strip_h = 110*mm
    rect(c, ox, strip_y, sw, strip_h, fill=TBLOCK_FILL,
         stroke=INK_SOFT, lw=0.3)

    # Logo band
    rect(c, ox + 2*mm, strip_y + strip_h - 14*mm, 30*mm, 12*mm,
         stroke=GRID_LINE, lw=0.3)
    text(c, ox + 17*mm, strip_y + strip_h - 8*mm, "ORG LOGO",
         size=6, col=INK_LIGHT, anchor="center")
    rect(c, ox + sw - 32*mm, strip_y + strip_h - 14*mm, 30*mm, 12*mm,
         stroke=GRID_LINE, lw=0.3)
    text(c, ox + sw - 17*mm, strip_y + strip_h - 8*mm, "CLIENT LOGO",
         size=6, col=INK_LIGHT, anchor="center")

    # Originator + client lines
    cy = strip_y + strip_h - 18*mm
    text(c, ox + 2*mm, cy, "${ORG_NAME}", size=7, col=INK,
         font="Helvetica-Bold"); cy -= 3*mm
    text(c, ox + 2*mm, cy, "${ORG_ADDRESS}", size=6, col=INK_SOFT)

    # Project block
    cy = strip_y + strip_h - 28*mm
    rect(c, ox + 2*mm, cy - 14*mm, sw - 4*mm, 14*mm,
         stroke=INK_SOFT, lw=0.3)
    text(c, ox + 4*mm, cy - 3*mm,
         "PROJECT  ${PROJECT_NAME}",
         size=8, col=INK, font="Helvetica-Bold")
    text(c, ox + 4*mm, cy - 6*mm,
         "${PROJECT_ADDRESS}", size=6.5, col=INK_SOFT)
    text(c, ox + 4*mm, cy - 9*mm,
         "Project no. ${PROJECT_NUMBER}    Client ${CLIENT_NAME}",
         size=6.5, col=INK_SOFT)
    text(c, ox + 4*mm, cy - 12*mm,
         "RIBA ${RIBA_STAGE}    Coord ${COORD_SYS}",
         size=6.5, col=INK_SOFT)

    # Drawing title + ID block
    cy -= 18*mm
    rect(c, ox + 2*mm, cy - 18*mm, sw - 4*mm, 18*mm,
         stroke=INK_SOFT, lw=0.3)
    text(c, ox + 4*mm, cy - 3*mm, "DRAWING TITLE",
         size=5.5, col=INK_LIGHT, font="Helvetica-Bold")
    text(c, ox + 4*mm, cy - 7*mm, "${SHEET_NAME}",
         size=9, col=INK, font="Helvetica-Bold")
    text(c, ox + 4*mm, cy - 11*mm,
         "PRJ ${PROJ}  ORIG ${ORIG}  VOL ${VOL}  LVL ${LVL}  "
         "TYPE ${TYPE}  ROL ${ROL}  SEQ ${SEQ}",
         size=5.5, col=INK_SOFT)
    text(c, ox + 4*mm, cy - 16*mm, "${SHEET_FULL_REF}",
         size=11, col=INK, font="Helvetica-Bold")

    # Authoring strip
    cy -= 22*mm
    cols = [("DRWN", "${DR}"), ("CHKD", "${CK}"),
            ("APPR", "${AP}"), ("AUTH", "${AU}"),
            ("REV",  "${REV}"), ("OF",   "${OF_TOTAL}")]
    cw2 = (sw - 4*mm) / 6
    for i, (lbl, val) in enumerate(cols):
        cell(c, ox + 2*mm + i*cw2, cy - 8*mm, cw2, 8*mm,
             label=lbl, value=val, lsize=5, vsize=7)

    # Footer line
    cy -= 12*mm
    text(c, ox + 2*mm, cy,
         "TR ${TRANSMITTAL_REF}  •  CDE ${CDE_PATH}  •  MIDP ${DELIVERABLE_ID}",
         size=5.5, col=INK_SOFT)
    text(c, ox + sw - 2*mm, cy,
         "Original A4 portrait", size=5, col=INK_LIGHT, anchor="right")

    # Side annotation panel — explains why bottom-strip layout
    ax = ox + sw + 12*mm
    ay = oy
    aw = PW - ax - 8*mm
    ah = sh
    rect(c, ax, ay, aw, ah, fill=LEGEND_BG, stroke=INK_LIGHT, lw=0.3)
    text(c, ax + 3*mm, ay + ah - 6*mm, "Why bottom strip on portrait?",
         size=10, col=INK, font="Helvetica-Bold")
    notes = [
        "• A4 portrait is reserved for single-page deliverables — RFIs,",
        "  technical queries, single-page schedules, transmittal memos.",
        "• Right strip would consume too much of the 210 mm width — only",
        "  ~140 mm of drawable zone remains. Bottom strip preserves the",
        "  full 195 mm × 175 mm drawable area above.",
        "• Status chips collapse to 4 (STATUS / SUITABILITY / REV / DATE)",
        "  — full 5-chip band of A1 doesn't fit at 210 mm width.",
        "• QR + revision-history strip moved to a continuation page when",
        "  needed (SheetTemplateEngine handles overflow automatically).",
        "",
        "Cell field count:",
        "• 24 bound labels (vs 35 on A1 landscape)",
        "• Same shared-parameter universe — TitleBlockParamApplier",
        "  resolves identically.",
        "",
        "Used by:",
        "• Tags/RichTagDisplayCommands (RFI / clarification sketches)",
        "• Planscape.Docs.Templates (transmittal cover letter,",
        "  technical query, RFI, technical response, signature pages)",
        "• Schedule sheets (door schedule, window schedule, finishes",
        "  matrix at A4 portrait when small project)",
        "",
        "Bound parameters specific to this family:",
        "• STING_RFI_NR_TXT (when used as RFI)",
        "• STING_RFI_SUBJECT_TXT",
        "• STING_QUESTION_TEXT",
        "• STING_RESPONSE_TEXT",
        "• STING_RECIPIENT_NAME_TXT (transmittal use)",
        "• STING_COST_IMPACT_TXT",
        "• STING_TIME_IMPACT_TXT",
    ]
    nx, ny = ax + 3*mm, ay + ah - 12*mm
    for line in notes:
        text(c, nx, ny, line, size=7, col=INK)
        ny -= 4*mm

    c.showPage()


# ── Page 4: A1 cover page ────────────────────────────────────────────

def page_cover(c):
    page_frame(c, "4.  A1 COVER PAGE",
               "STING_TB_COVER_A1_v1.0 — issue bundle front sheet, no drawable viewport")

    # A1 landscape cover, scaled to fit
    sw = 280*mm
    sh = sw * 594 / 841    # ≈ 198 mm
    ox = (PW - sw) / 2
    oy = (PH - sh) / 2 - 4*mm
    rect(c, ox, oy, sw, sh, stroke=INK, lw=0.7)

    # Top blank band
    band_h = 30*mm
    by = oy + sh - band_h
    rect(c, ox, by, sw, band_h, fill=PAPER, stroke=INK_SOFT, lw=0.3)

    # Logo placeholders
    logo_w = 50*mm
    logo_h = 22*mm
    rect(c, ox + sw*0.30 - logo_w/2, by + (band_h - logo_h)/2,
         logo_w, logo_h, stroke=GRID_LINE, lw=0.4)
    text(c, ox + sw*0.30, by + band_h/2,
         "ORIGINATOR LOGO", size=8, col=INK_LIGHT,
         anchor="center", font="Helvetica-Bold")
    text(c, ox + sw*0.30, by + band_h/2 - 5,
         "${ORG_LOGO}", size=6, col=INK_LIGHT, anchor="center")
    rect(c, ox + sw*0.70 - logo_w/2, by + (band_h - logo_h)/2,
         logo_w, logo_h, stroke=GRID_LINE, lw=0.4)
    text(c, ox + sw*0.70, by + band_h/2,
         "CLIENT LOGO", size=8, col=INK_LIGHT,
         anchor="center", font="Helvetica-Bold")
    text(c, ox + sw*0.70, by + band_h/2 - 5,
         "${CL_LOGO}", size=6, col=INK_LIGHT, anchor="center")

    # Main title block (centred, double-rule border)
    th = 50*mm
    ty = oy + sh - band_h - 16*mm - th
    tx = ox + 30*mm
    tw = sw - 60*mm
    rect(c, tx, ty, tw, th, stroke=INK, lw=1.0)
    rect(c, tx + 1.5*mm, ty + 1.5*mm, tw - 3*mm, th - 3*mm,
         stroke=INK_SOFT, lw=0.4)
    text(c, ox + sw/2, ty + th*0.65,
         "${PROJECT_NAME}",
         size=22, col=INK, font="Helvetica-Bold", anchor="center")
    text(c, ox + sw/2, ty + th*0.35,
         "${PROJECT_ADDRESS}",
         size=11, col=INK_SOFT, anchor="center")
    text(c, ox + sw/2, ty + th*0.18,
         "Project no. ${PROJECT_NUMBER}    RIBA Stage ${RIBA_STAGE}",
         size=9, col=INK_LIGHT, anchor="center")

    # Container issue panel
    px = ox + sw*0.32
    pw = sw*0.36
    ph = 50*mm
    py = ty - 12*mm - ph
    rect(c, px, py, pw, ph, stroke=INK_SOFT, lw=0.4,
         fill=HexColor("#FAFAFA"))
    text(c, px + 2*mm, py + ph - 4*mm,
         "INFORMATION CONTAINER ISSUE",
         size=8, col=INK, font="Helvetica-Bold")
    rows = [
        ("Suitability",   "${SUITABILITY}  —  ${SUIT_DESC}"),
        ("Issue purpose", "${ISSUE_PURPOSE}"),
        ("Revision",      "${REV}"),
        ("Issue date",    "${REV_DATE}"),
        ("Transmittal",   "${TRANSMITTAL_REF}"),
        ("Deliverable",   "${DELIVERABLE_ID}"),
        ("CDE container", "${CDE_PATH}"),
        ("LOIN / LOD",    "${LOIN_LOD}"),
    ]
    rh = (ph - 7*mm) / len(rows)
    for i, (lbl, val) in enumerate(rows):
        ry = py + ph - 7*mm - (i+1)*rh
        text(c, px + 2*mm, ry + rh*0.35, lbl,
             size=7, col=INK_SOFT)
        text(c, px + pw*0.45, ry + rh*0.35, val,
             size=7.5, col=INK, font="Helvetica-Bold")

    # Parties block
    pyy = py - 8*mm
    text(c, ox + sw/2, pyy,
         "Originator  ${ORG_NAME}  (${ORG_CODE})", size=8,
         col=INK, anchor="center", font="Helvetica-Bold")
    text(c, ox + sw/2, pyy - 4*mm,
         "Appointing party  ${APPOINTING_PARTY}",
         size=7.5, col=INK_SOFT, anchor="center")
    text(c, ox + sw/2, pyy - 7*mm,
         "Lead appointed party  ${LEAD_APPOINTED_PARTY}",
         size=7.5, col=INK_SOFT, anchor="center")

    # Footer
    fh = 14*mm
    rect(c, ox, oy, sw, fh, fill=TBLOCK_FILL, stroke=INK_SOFT, lw=0.3)
    text(c, ox + 2*mm, oy + fh - 3*mm,
         "Statutory  CDM / fire / asbestos as per individual sheets.    "
         "${COPYRIGHT}    Original A1.",
         size=6.5, col=INK_SOFT)
    text(c, ox + 2*mm, oy + 5*mm,
         "${SECURITY_CLASS} — restricted distribution per CDE rules.",
         size=6.5, col=INK_SOFT)
    text(c, ox + 2*mm, oy + 1.5*mm,
         "Sheet ${SHEET_FULL_REF} — Cover",
         size=7, col=INK, font="Helvetica-Bold")
    text(c, ox + sw - 2*mm, oy + 1.5*mm,
         "${OF_TOTAL}", size=8, col=INK,
         anchor="right", font="Helvetica-Bold")

    # Side annotation
    ax = ox - 4*mm
    text(c, ax, oy + sh - 5*mm,
         "22 bound cells.  No viewport.",
         size=7, col=INK_LIGHT, anchor="right")
    text(c, ax, oy + sh - 8.5*mm,
         "Generated by BIMManagerEngine.GenerateProjectCover.",
         size=7, col=INK_LIGHT, anchor="right")

    c.showPage()


# ── Page 5: Slot positions diagram ───────────────────────────────────

def _slot_panel(c, ox, oy, w, h, title, slots, caption=""):
    """Draw a single slot-layout panel.
    slots: list of (label, nx, ny, nw, nh, fill_col)"""
    rect(c, ox, oy, w, h, stroke=INK, lw=0.5)
    # Title bar
    rect(c, ox, oy + h - 7*mm, w, 7*mm, fill=TBLOCK_FILL,
         stroke=INK_SOFT, lw=0.3)
    text(c, ox + w/2, oy + h - 4.5*mm, title,
         size=8, col=INK, anchor="center", font="Helvetica-Bold")
    # Drawable zone (the panel below the title bar)
    dx, dy = ox, oy
    dw, dh = w, h - 7*mm
    # Grid (10 × 10 normalised)
    c.setStrokeColor(GRID_LINE); c.setLineWidth(0.15)
    for i in range(1, 10):
        c.line(dx + dw*i/10, dy, dx + dw*i/10, dy + dh)
        c.line(dx, dy + dh*i/10, dx + dw, dy + dh*i/10)
    # Slots
    for lbl, nx, ny, nw, nh, fill in slots:
        sx = dx + dw*nx
        # invert Y so origin is top-left in the user model
        sy = dy + dh*(1 - ny - nh)
        sw_ = dw*nw
        sh_ = dh*nh
        rect(c, sx + 0.5, sy + 0.5, sw_ - 1, sh_ - 1,
             stroke=INK, fill=fill, lw=0.5)
        text(c, sx + sw_/2, sy + sh_/2 + 1.5, lbl,
             size=6.5, col=INK, anchor="center",
             font="Helvetica-Bold")
        text(c, sx + sw_/2, sy + sh_/2 - 4,
             f"({nx:.2f}, {ny:.2f}, {nw:.2f}, {nh:.2f})",
             size=5, col=INK_SOFT, anchor="center")
    # Caption
    if caption:
        text(c, dx + dw/2, oy - 3*mm, caption,
             size=6.5, col=INK_LIGHT, anchor="center")


def page_slots(c):
    page_frame(c, "5.  SLOT POSITIONS",
               "DrawingType.Slots[] normalised over the drawable zone "
               "(top-left = 0,0 ; bottom-right = 1,1)")

    cols = 4
    rows = 2
    margin = 12*mm
    gap = 6*mm
    pw = (PW - 2*margin - (cols-1)*gap) / cols
    ph = (PH - 50*mm - (rows-1)*gap) / rows

    panels = [
        ("MAIN  (single full slot)",
            [("MAIN", 0.00, 0.00, 1.00, 1.00, SLOT_FILL)],
            "Default — used by presentation, cover, full-bleed sheets."),
        ("MAIN_LEFT + RIGHT_TOP / RIGHT_BOT",
            [("MAIN_LEFT",      0.00, 0.00, 0.66, 1.00, SLOT_FILL),
             ("MAIN_RIGHT_TOP", 0.66, 0.00, 0.34, 0.50, HexColor("#FFE0B2")),
             ("MAIN_RIGHT_BOT", 0.66, 0.50, 0.34, 0.50, HexColor("#C8E6C9"))],
            "Plan + side detail / 3D — A1 working sheet default."),
        ("FOUR_UP grid",
            [("FOUR_UP_TL", 0.00, 0.00, 0.50, 0.50, SLOT_FILL),
             ("FOUR_UP_TR", 0.50, 0.00, 0.50, 0.50, HexColor("#FFE0B2")),
             ("FOUR_UP_BL", 0.00, 0.50, 0.50, 0.50, HexColor("#C8E6C9")),
             ("FOUR_UP_BR", 0.50, 0.50, 0.50, 0.50, HexColor("#F8BBD0"))],
            "Elevations 4-up, render board, presentation context."),
        ("THREE_HSTACK",
            [("TOP",    0.00, 0.00, 1.00, 0.34, SLOT_FILL),
             ("MID",    0.00, 0.34, 1.00, 0.33, HexColor("#FFE0B2")),
             ("BOT",    0.00, 0.67, 1.00, 0.33, HexColor("#C8E6C9"))],
            "Riser diagrams, vertical sections."),
        ("FAB SPOOL — 5-slot",
            [("PLAN",   0.00, 0.00, 0.36, 0.40, SLOT_FILL),
             ("ISO",    0.36, 0.00, 0.36, 0.40, HexColor("#FFE0B2")),
             ("ELEV0",  0.00, 0.40, 0.36, 0.30, HexColor("#C8E6C9")),
             ("ELEV90", 0.36, 0.40, 0.36, 0.30, HexColor("#F8BBD0")),
             ("AXON",   0.00, 0.70, 0.72, 0.30, HexColor("#E1BEE7"))],
            "Pipe / duct / conduit assembly. BOM strip on right (0.72…1.0)."),
        ("PLAN + LEGEND + DETAIL",
            [("PLAN",   0.00, 0.00, 0.66, 1.00, SLOT_FILL),
             ("LEGEND", 0.66, 0.00, 0.34, 0.50, HexColor("#FFE0B2")),
             ("DETAIL", 0.66, 0.50, 0.34, 0.50, HexColor("#C8E6C9"))],
            "Architectural plan with legend + key detail."),
        ("CLARIFICATION (RFI sketch)",
            [("SKETCH", 0.00, 0.00, 0.62, 1.00, SLOT_FILL),
             ("Q&A",    0.62, 0.00, 0.38, 1.00, HexColor("#FFE0B2"))],
            "A3 landscape — left sketch, right RFI Q&A panel."),
        ("PRESENTATION (full bleed)",
            [("RENDER", 0.00, 0.00, 1.00, 0.90, SLOT_FILL),
             ("CAPTION",0.00, 0.90, 1.00, 0.10, HexColor("#FFE0B2"))],
            "60 mm caption bar below render."),
    ]

    for i, (title, slots, caption) in enumerate(panels):
        col = i % cols
        row = i // cols
        x = margin + col*(pw + gap)
        y = PH - 24*mm - (row+1)*ph - row*gap
        _slot_panel(c, x, y, pw, ph, title, slots, caption)

    # Footer note
    text(c, PW/2, 14*mm,
         "Coordinates are normalised to the drawable zone of the parent title block. "
         "SheetTemplateEngine resolves them to absolute mm at sheet creation time. "
         "Origin (0,0) = top-left of drawable zone.",
         size=8, col=INK_SOFT, anchor="center")
    text(c, PW/2, 10*mm,
         "Negative coords or coords > 1.0 spill outside the drawable zone — "
         "PlaceWithOverflowCommand handles overflow to continuation sheets.",
         size=7, col=INK_LIGHT, anchor="center")

    c.showPage()


# ── Page 6: Match-line details ───────────────────────────────────────

def page_matchlines(c):
    page_frame(c, "6.  MATCH-LINE DETAILS",
               "Sheet-to-sheet continuation marks per BS 1192 §A.6")

    # Layout: 2 sheets side by side at top showing a match line in
    # action; 4 detail blocks at bottom showing the symbol library.
    margin = 14*mm

    # Top — two sheets sharing a match line
    panel_w = (PW - 2*margin - 8*mm) / 2
    panel_h = 100*mm
    panel_y = PH - 24*mm - panel_h
    # left sheet
    lx = margin
    ly = panel_y
    rect(c, lx, ly, panel_w, panel_h, stroke=INK, lw=0.6)
    text(c, lx + 4*mm, ly + panel_h - 5*mm,
         "SHEET   STG-PLNS-ZZ-01-DR-A-0002",
         size=7, col=INK, font="Helvetica-Bold")
    text(c, lx + 4*mm, ly + panel_h - 8.5*mm,
         "Ground floor — west half",
         size=6.5, col=INK_SOFT)
    # plan content stub
    rect(c, lx + 6*mm, ly + 8*mm, panel_w - 12*mm, panel_h - 18*mm,
         stroke=GRID_LINE, lw=0.3, fill=HexColor("#FAFAFA"))
    # match line on the right edge of left sheet
    mlx = lx + panel_w - 14*mm
    c.setStrokeColor(ERROR); c.setLineWidth(1.2)
    c.line(mlx, ly + 10*mm, mlx, ly + panel_h - 12*mm)
    # break ticks
    for ty in range(0, int(panel_h - 22*mm), 6):
        c.setStrokeColor(ERROR); c.setLineWidth(1.2)
        c.line(mlx - 2*mm, ly + 10*mm + ty, mlx + 2*mm,
               ly + 10*mm + ty + 1.5*mm)
    # match line label
    text(c, mlx, ly + panel_h - 10*mm, "MATCH LINE  M.L.",
         size=6.5, col=ERROR, anchor="center", font="Helvetica-Bold")
    text(c, mlx, ly + panel_h - 13*mm, "see  STG-PLNS-ZZ-01-DR-A-0003",
         size=5.5, col=ERROR, anchor="center")
    # arrow pointing right
    c.setStrokeColor(INK); c.setLineWidth(0.6)
    c.line(mlx + 4*mm, ly + 30*mm, mlx + 14*mm, ly + 30*mm)
    c.line(mlx + 14*mm, ly + 30*mm, mlx + 11*mm, ly + 32*mm)
    c.line(mlx + 14*mm, ly + 30*mm, mlx + 11*mm, ly + 28*mm)
    text(c, mlx + 9*mm, ly + 32.5*mm, "continues right →",
         size=5.5, col=INK)

    # right sheet
    rx = lx + panel_w + 8*mm
    rect(c, rx, ly, panel_w, panel_h, stroke=INK, lw=0.6)
    text(c, rx + 4*mm, ly + panel_h - 5*mm,
         "SHEET   STG-PLNS-ZZ-01-DR-A-0003",
         size=7, col=INK, font="Helvetica-Bold")
    text(c, rx + 4*mm, ly + panel_h - 8.5*mm,
         "Ground floor — east half",
         size=6.5, col=INK_SOFT)
    rect(c, rx + 6*mm, ly + 8*mm, panel_w - 12*mm, panel_h - 18*mm,
         stroke=GRID_LINE, lw=0.3, fill=HexColor("#FAFAFA"))
    # match line on the left edge of right sheet
    mrx = rx + 14*mm
    c.setStrokeColor(ERROR); c.setLineWidth(1.2)
    c.line(mrx, ly + 10*mm, mrx, ly + panel_h - 12*mm)
    for ty in range(0, int(panel_h - 22*mm), 6):
        c.setStrokeColor(ERROR); c.setLineWidth(1.2)
        c.line(mrx - 2*mm, ly + 10*mm + ty, mrx + 2*mm,
               ly + 10*mm + ty + 1.5*mm)
    text(c, mrx, ly + panel_h - 10*mm, "MATCH LINE  M.L.",
         size=6.5, col=ERROR, anchor="center", font="Helvetica-Bold")
    text(c, mrx, ly + panel_h - 13*mm, "see  STG-PLNS-ZZ-01-DR-A-0002",
         size=5.5, col=ERROR, anchor="center")
    c.setStrokeColor(INK); c.setLineWidth(0.6)
    c.line(mrx - 4*mm, ly + 30*mm, mrx - 14*mm, ly + 30*mm)
    c.line(mrx - 14*mm, ly + 30*mm, mrx - 11*mm, ly + 32*mm)
    c.line(mrx - 14*mm, ly + 30*mm, mrx - 11*mm, ly + 28*mm)
    text(c, mrx - 9*mm, ly + 32.5*mm, "← continues from",
         size=5.5, col=INK, anchor="end")

    # Caption
    text(c, PW/2, panel_y - 4*mm,
         "Two adjacent A1 sheets sharing a north–south match line. "
         "Symbol replicates on both sides; cross-reference back-links "
         "are auto-populated from STING_SHEET_FULL_REF on the paired sheet.",
         size=8, col=INK_SOFT, anchor="center")

    # Symbol library — 4 detail blocks
    y2 = panel_y - 18*mm
    block_h = 60*mm
    block_w = (PW - 2*margin - 3*8*mm) / 4

    blocks = [
        ("VERTICAL  M.L.", "vert"),
        ("HORIZONTAL  M.L.", "horiz"),
        ("DOG-LEG  M.L.",  "dogleg"),
        ("BREAK-LINE",     "break"),
    ]

    for i, (title, kind) in enumerate(blocks):
        bx = margin + i*(block_w + 8*mm)
        by = y2 - block_h
        rect(c, bx, by, block_w, block_h, stroke=INK_SOFT, lw=0.4)
        text(c, bx + block_w/2, by + block_h - 5*mm, title,
             size=7.5, col=INK, anchor="center",
             font="Helvetica-Bold")
        # draw the symbol
        cx = bx + block_w/2
        cy = by + block_h*0.50
        c.setStrokeColor(ERROR); c.setLineWidth(1.4)
        if kind == "vert":
            c.line(cx, by + 8*mm, cx, by + block_h - 12*mm)
            for ty in range(0, int(block_h - 20*mm), 4):
                c.line(cx - 2*mm, by + 8*mm + ty,
                       cx + 2*mm, by + 8*mm + ty + 1*mm)
        elif kind == "horiz":
            c.line(bx + 6*mm, cy, bx + block_w - 6*mm, cy)
            for tx in range(0, int(block_w - 12*mm), 4):
                c.line(bx + 6*mm + tx, cy - 2*mm,
                       bx + 6*mm + tx + 1*mm, cy + 2*mm)
        elif kind == "dogleg":
            c.line(bx + 6*mm, by + 12*mm, bx + block_w/2, by + 12*mm)
            c.line(bx + block_w/2, by + 12*mm,
                   bx + block_w/2, by + block_h - 14*mm)
            c.line(bx + block_w/2, by + block_h - 14*mm,
                   bx + block_w - 6*mm, by + block_h - 14*mm)
            for tx in range(0, int(block_w/2 - 8*mm), 4):
                c.line(bx + 6*mm + tx, by + 10*mm,
                       bx + 6*mm + tx + 1*mm, by + 14*mm)
        elif kind == "break":
            c.setStrokeColor(INK); c.setLineWidth(0.6)
            c.line(bx + 6*mm, cy, bx + block_w*0.40, cy)
            # zig-zag
            zx = bx + block_w*0.40
            c.line(zx, cy, zx + 4*mm, cy + 3*mm)
            c.line(zx + 4*mm, cy + 3*mm, zx + 8*mm, cy - 3*mm)
            c.line(zx + 8*mm, cy - 3*mm, zx + 12*mm, cy + 3*mm)
            c.line(zx + 12*mm, cy + 3*mm, zx + 16*mm, cy)
            c.line(zx + 16*mm, cy, bx + block_w - 6*mm, cy)
        # Bound parameter caption
        text(c, bx + block_w/2, by + 5.5*mm,
             "STING_MATCH_REF_TXT",
             size=6, col=INK_SOFT, anchor="center",
             font="Helvetica-Bold")
        text(c, bx + block_w/2, by + 3*mm,
             "→ paired sheet ref",
             size=5.5, col=INK_LIGHT, anchor="center")

    # Bottom notes
    text(c, margin, 24*mm,
         "Match-line conventions:",
         size=8.5, col=INK, font="Helvetica-Bold")
    notes = [
        "• Drawn in the dedicated 'STING - Match Line' line style: red 0.50 mm projection, dashed-with-tick pattern.",
        "• Each match line carries a label tag bound to STING_MATCH_REF_TXT — populated by the paired sheet's full ref.",
        "• Use BS 1192 §A.6 placement: parallel to grid where possible, never through a sectioned element, never closer than 25 mm to drawable-zone edge.",
        "• Pair detection: BatchSectionsCommand / GenerateFromScopeBoxesCommand auto-cross-references when two sheets share a scope box edge.",
        "• Continuation arrow + 'see SHEET-REF' caption are detail-component placeholders; SheetTemplateEngine drops them at the line's tip.",
    ]
    ny = 20*mm
    for line in notes:
        text(c, margin + 2*mm, ny, line, size=7, col=INK)
        ny -= 3.5*mm

    c.showPage()


# ── Driver ───────────────────────────────────────────────────────────

def main():
    c = canvas.Canvas(OUT, pagesize=PAGE)
    c.setTitle("STING Title Block Family Library — visual reference")
    c.setAuthor("STING / Planscape")
    c.setSubject("Phase 167/168 — title block design (locator + sheets + slots + matchlines)")
    page_locator(c)
    page_landscape(c)
    page_portrait(c)
    page_cover(c)
    page_slots(c)
    page_matchlines(c)
    c.save()
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
