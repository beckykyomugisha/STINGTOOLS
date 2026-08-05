#!/usr/bin/env python3
"""
STING Title-Block Catalogue Polisher
=====================================

Replaces the stub layouts shipped by `expand_title_block_catalogue.py`
with polished, per-family layouts:

  - 8 size+orientation working-sheet commons (A0/A2/A3/A4 × LAND/PORT)
    get refined bottom-strip layouts proportional to their paper size.
  - 16 concrete BIM/NONBIM working sheets get proper ISO 19650 identity
    strips (BIM) or minimal sheet-number blocks (NONBIM) sized for
    their paper.
  - 14 specialty families get bespoke layouts per purpose:
      Fabrication × 4   — right BOM strip + bottom fab metadata
      Presentation × 2  — full-bleed + corner watermark
      Cover, Divider, Register — single-purpose layouts
      Transmittal A4    — recipient + drawings + signatures
      Submission × 3    — regulator banner + statutory cells
      Clarification A3  — RFI query + sketch area

The hand-tuned A1 LAND BIM/NONBIM and the master A1_common_v2.0 are
preserved verbatim — those have full design polish already.

Run from repo root after expand_title_block_catalogue.py:
    python3 tools/polish_title_block_catalogue.py
    python3 tools/generate_title_block_previews.py
"""

import json
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SPEC = REPO / "StingTools" / "Data" / "STING_TITLE_BLOCKS.json"

# Sheet sizes (mm, paper W x H, landscape).
SIZES = {
    "A0": (1189, 841),
    "A1": (841,  594),
    "A2": (594,  420),
    "A3": (420,  297),
    "A4": (297,  210),
}

# Strip height per size — bottom-strip height. Scales roughly with paper
# height to preserve the strip-to-drawable-zone ratio.
STRIP_H = {"A0": 130, "A1": 110, "A2": 88, "A3": 70, "A4": 56}

# BIM identity strip height (sits ABOVE the main bottom strip when BIM mode).
BIM_STRIP_H = {"A0": 36, "A1": 30, "A2": 24, "A3": 20, "A4": 16}

# Text size scales — smaller for smaller papers so labels remain readable
# without overflowing cells.
TEXT_SCALE = {"A0": 1.30, "A1": 1.00, "A2": 0.85, "A3": 0.70, "A4": 0.55}


# ─── Helpers ─────────────────────────────────────────────────────────────

def find(lib, fam_id):
    for f in lib["families"]:
        if f["id"] == fam_id:
            return f
    return None


def upsert(lib, fam):
    """Replace the matching family by id, or append."""
    for i, f in enumerate(lib["families"]):
        if f["id"] == fam["id"]:
            lib["families"][i] = fam
            return
    lib["families"].append(fam)


def t(size, base):
    """Scale a text-size value by the size's text scale."""
    return round(base * TEXT_SCALE[size], 2)


# ─── Working-sheet abstract commons ──────────────────────────────────────

def polish_working_common(size, orientation):
    """Return a polished abstract common for a size+orientation."""
    is_port = orientation == "PORT"
    if is_port:
        w, h = SIZES[size][1], SIZES[size][0]
    else:
        w, h = SIZES[size]
    sh = STRIP_H[size]
    fam_id = f"{size}_{'PORT' if is_port else 'LAND'}_common_v2.0"
    desc_o = "portrait" if is_port else "landscape"
    desc = (
        f"{size} {desc_o} working sheet ({w} × {h} mm) — common base shared "
        f"between BIM and NONBIM variants. Inherits the 40-param identity-data "
        f"universe from A1_common_v2.0 and adds geometry sized for this "
        f"paper. Five-column bottom strip (CLIENT/PROJECT/CONSULTANTS — "
        f"NOTES/PROJECT-REF/DISCIPLINE/STAGE — DRAWING TITLE — DATES/AUTHORING — "
        f"SHEET ID), strip height {sh} mm, drawable zone {w-20} × {h-sh-15} mm."
    )

    # 5-column bottom strip — proportional widths
    c1 = round(w * 0.22)   # identity column
    c2 = round(w * 0.42)   # notes / context
    c3 = round(w * 0.66)   # drawing title
    c4 = round(w * 0.86)   # dates / authoring
    # remainder = sheet number column

    # 6 row-dividers in c1 (CLIENT/PROJECT/ARCH/STRUCT/MEP/CONTR)
    rh = sh / 6.0

    lines = [
        # Outer border
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        # Strip top edge
        {"from": [0, sh], "to": [w, sh], "style": "Medium Lines"},
        # Vertical dividers
        {"from": [c1, 0], "to": [c1, sh], "style": "Thin Lines"},
        {"from": [c2, 0], "to": [c2, sh], "style": "Thin Lines"},
        {"from": [c3, 0], "to": [c3, sh], "style": "Thin Lines"},
        {"from": [c4, 0], "to": [c4, sh], "style": "Thin Lines"},
    ]
    # c1 row dividers
    for i in range(1, 6):
        y = round(i * rh, 1)
        lines.append({"from": [0, y], "to": [c1, y], "style": "Thin Lines"})
    # c2 internal dividers — 3 cells (notes / project-ref / discipline / stage)
    for frac in [0.30, 0.55, 0.78]:
        y = round(sh * frac, 1)
        lines.append({"from": [c1, y], "to": [c2, y], "style": "Thin Lines"})
    # c4 internal dividers — date / drawn / checked / approved
    for frac in [0.25, 0.50, 0.75]:
        y = round(sh * frac, 1)
        lines.append({"from": [c3, y], "to": [c4, y], "style": "Thin Lines"})

    # Static-text headers
    pad = max(2, round(rh * 0.12))
    static = [
        {"text": "CLIENT",      "anchor": [pad, sh - pad - 1],          "size": t(size, 1.4)},
        {"text": "PROJECT",     "anchor": [pad, sh - rh*1 - pad - 1],   "size": t(size, 1.2)},
        {"text": "ARCHITECT",   "anchor": [pad, sh - rh*2 - pad - 1],   "size": t(size, 1.0)},
        {"text": "STRUCTURAL",  "anchor": [pad, sh - rh*3 - pad - 1],   "size": t(size, 1.0)},
        {"text": "MEP",         "anchor": [pad, sh - rh*4 - pad - 1],   "size": t(size, 1.0)},
        {"text": "CONTRACTOR",  "anchor": [pad, sh - rh*5 - pad - 1],   "size": t(size, 1.0)},

        {"text": "NOTES",          "anchor": [c1+pad, sh - pad - 1],         "size": t(size, 1.2)},
        {"text": "PROJECT REF",    "anchor": [c1+pad, sh*0.78 - pad - 1],    "size": t(size, 0.9)},
        {"text": "DISCIPLINE",     "anchor": [c1+pad, sh*0.55 - pad - 1],    "size": t(size, 0.9)},
        {"text": "RIBA STAGE",     "anchor": [c1+pad, sh*0.30 - pad - 1],    "size": t(size, 0.9)},

        {"text": "DRAWING TITLE",  "anchor": [c2+pad, sh - pad - 1],         "size": t(size, 1.4)},

        {"text": "DATE",           "anchor": [c3+pad, sh - pad - 1],         "size": t(size, 1.0)},
        {"text": "DRAWN BY",       "anchor": [c3+pad, sh*0.75 - pad - 1],    "size": t(size, 0.9)},
        {"text": "CHECKED BY",     "anchor": [c3+pad, sh*0.50 - pad - 1],    "size": t(size, 0.9)},
        {"text": "APPROVED BY",    "anchor": [c3+pad, sh*0.25 - pad - 1],    "size": t(size, 0.9)},

        {"text": "SHEET",          "anchor": [c4+pad, sh - pad - 1],         "size": t(size, 1.0)},
        {"text": "SCALE",          "anchor": [c4+pad, sh*0.60 - pad - 1],    "size": t(size, 0.9)},
        {"text": "PAPER",          "anchor": [c4+pad, sh*0.30 - pad - 1],    "size": t(size, 0.9)},
    ]

    # Labels — bind to existing PRJ_TB_/PRJ_ORG_ params
    cy = lambda i: sh - rh*i - rh*0.55
    labels = [
        {"param": "PRJ_TB_CLIENT_NAME_TXT",    "anchor": [pad, sh - rh*0.55],    "size": t(size, 1.6)},
        {"param": "PRJ_TB_CLIENT_ADDRESS_TXT", "anchor": [pad, sh - rh*0.85 - 1], "size": t(size, 0.9)},

        {"param": "PRJ_ORG_PROJECT_NAME_TXT",   "anchor": [pad, cy(1) + 1],      "size": t(size, 1.5)},
        {"param": "PRJ_ORG_PROJECT_ADDRESS_TXT","anchor": [pad, cy(1) - rh*0.3], "size": t(size, 0.85)},

        {"param": "PRJ_TB_CONSULTANT_NAME_TXT",          "anchor": [pad, cy(2) + 1],      "size": t(size, 1.1)},
        {"param": "PRJ_TB_CONSULTANT_ADDRESS_TXT",       "anchor": [pad, cy(2) - rh*0.3], "size": t(size, 0.75)},
        {"param": "PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT",    "anchor": [pad, cy(3) + 1],      "size": t(size, 1.1)},
        {"param": "PRJ_TB_STRUCTURAL_CONSULTANTS_ADDRESS_TXT", "anchor": [pad, cy(3) - rh*0.3], "size": t(size, 0.75)},
        {"param": "PRJ_TB_MEP_CONSULTANTS_NAME_TXT",     "anchor": [pad, cy(4) + 1],      "size": t(size, 1.1)},
        {"param": "PRJ_TB_MEP_CONSULTANTS_ADDRESS_TXT",  "anchor": [pad, cy(4) - rh*0.3], "size": t(size, 0.75)},
        {"param": "PRJ_TB_CONTRACTOR_NAME_TXT",          "anchor": [pad, cy(5) + 1],      "size": t(size, 1.1)},
        {"param": "PRJ_TB_CONTRACTOR_ADDRESS_TXT",       "anchor": [pad, cy(5) - rh*0.3], "size": t(size, 0.75)},

        {"param": "PRJ_TB_NOTES_LEGEND_REF_TXT", "anchor": [c1+pad, sh*0.85 - 1], "size": t(size, 1.0)},
        {"param": "PRJ_ORG_PROJECT_CODE_TXT",    "anchor": [c1+pad, sh*0.71 - 1], "size": t(size, 1.0)},
        {"param": "PRJ_TB_DISCIPLINE_TXT",       "anchor": [c1+pad, sh*0.48 - 1], "size": t(size, 1.0)},
        {"param": "PRJ_ORG_RIBA_STAGE_TXT",      "anchor": [c1+pad, sh*0.23 - 1], "size": t(size, 1.0)},

        {"param": "PRJ_ORG_PROJECT_NAME_TXT",    "anchor": [c2+pad, sh - rh*1.2], "size": t(size, 3.0)},

        {"param": "PRJ_TB_DATE_DRAWN_TXT",  "anchor": [c3+pad, sh - rh*0.6],      "size": t(size, 1.0)},
        {"param": "PRJ_TB_DRAWN_BY_TXT",    "anchor": [c3+pad, sh*0.68 - 1],      "size": t(size, 1.0)},
        {"param": "PRJ_TB_CHECKED_BY_TXT",  "anchor": [c3+pad, sh*0.43 - 1],      "size": t(size, 1.0)},
        {"param": "PRJ_TB_APVD_BY_TXT",     "anchor": [c3+pad, sh*0.18 - 1],      "size": t(size, 1.0)},

        {"param": "PRJ_TB_SCALE_OVERRIDE_TXT", "anchor": [c4+pad, sh*0.50 - 1], "size": t(size, 1.0)},
        {"param": "PRJ_TB_PAPER_SZ_TXT",       "anchor": [c4+pad, sh*0.20 - 1], "size": t(size, 1.0)},
    ]

    # Slots — proportional to paper. drawable_y leaves room for BIM strip.
    drawable_y = sh + 5
    drawable_h = h - drawable_y - 5
    half_w = (w - 30) // 2
    quad_w = (w - 30) // 2
    quad_h = (drawable_h - 15) // 2

    slots = [
        {"id": "S01", "anchor": [10, drawable_y], "size": [w - 20, drawable_h],
         "purposeTag": "main-plan", "category": "primary",
         "viewportType": "Title w/ Line", "scaleHint": 100,
         "description": "Main drawing area — full-bleed plan / 3D / section",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "S02", "anchor": [10, drawable_y], "size": [half_w, drawable_h],
         "purposeTag": "main-plan-half-left", "category": "primary",
         "viewportType": "Title w/ Line", "scaleHint": 100,
         "description": "Left half — 50/50 split",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "S03", "anchor": [20+half_w, drawable_y], "size": [half_w, drawable_h],
         "purposeTag": "main-plan-half-right", "category": "primary",
         "viewportType": "Title w/ Line", "scaleHint": 100,
         "description": "Right half — 50/50 split",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "S04", "anchor": [10, drawable_y], "size": [quad_w, quad_h],
         "purposeTag": "quad-bottom-left", "category": "primary",
         "scaleHint": 200,
         "description": "Bottom-left quadrant",
         "createReferencePlanes": False, "showCornerMarker": True},
        {"id": "S05", "anchor": [20+quad_w, drawable_y], "size": [quad_w, quad_h],
         "purposeTag": "quad-bottom-right", "category": "primary",
         "scaleHint": 200,
         "description": "Bottom-right quadrant",
         "createReferencePlanes": False, "showCornerMarker": True},
        {"id": "S06", "anchor": [10, drawable_y+quad_h+5], "size": [quad_w, quad_h],
         "purposeTag": "quad-top-left", "category": "primary",
         "scaleHint": 200,
         "description": "Top-left quadrant",
         "createReferencePlanes": False, "showCornerMarker": True},
        {"id": "S07", "anchor": [20+quad_w, drawable_y+quad_h+5], "size": [quad_w, quad_h],
         "purposeTag": "quad-top-right", "category": "primary",
         "scaleHint": 200,
         "description": "Top-right quadrant",
         "createReferencePlanes": False, "showCornerMarker": True},
    ]

    # ── Auxiliary slots — vertical right-edge column when room allows ──
    # Only A0/A1/A2 have enough drawable width to host a dedicated 150 mm
    # auxiliary column without crowding the main slot. A3/A4 too small —
    # skip auxiliary slots entirely (operator drops legends as nested
    # families directly in the drawable zone).
    aux_eligible = size in ("A0", "A1", "A2")
    if aux_eligible:
        # Auxiliary column 150mm wide, hugged against right edge inside
        # the drawable zone. Stack: KP (top) → NOTES → LEGEND → REV.
        aux_w = round(w * 0.16)
        aux_x = w - aux_w - 10
        # Top: key-plan pocket (square-ish)
        kp_h = round(drawable_h * 0.18)
        slots.append({"id": "KP", "anchor": [aux_x, drawable_y + drawable_h - kp_h - 5],
                      "size": [aux_w, kp_h],
                      "purposeTag": "key-plan", "category": "auxiliary",
                      "scaleHint": 500,
                      "description": "Key-plan pocket — small location overview",
                      "createReferencePlanes": False, "showCornerMarker": True,
                      "respectShowToggle": True})
        # Notes panel — drafting view or text legend
        notes_h = round(drawable_h * 0.28)
        notes_y = drawable_y + drawable_h - kp_h - notes_h - 12
        slots.append({"id": "NOTES", "anchor": [aux_x, notes_y],
                      "size": [aux_w, notes_h],
                      "purposeTag": "notes", "category": "auxiliary",
                      "viewportType": "No Title",
                      "description": "Notes panel — drafting view or legend with general notes",
                      "createReferencePlanes": False, "showCornerMarker": True,
                      "automationHook": "Legend_BuildNotes"})
        # Discipline legend — symbol legend keyed to PRJ_TB_DISCIPLINE_TXT
        leg_h = round(drawable_h * 0.24)
        leg_y = notes_y - leg_h - 5
        slots.append({"id": "LEGEND", "anchor": [aux_x, leg_y],
                      "size": [aux_w, leg_h],
                      "purposeTag": "discipline-legend", "category": "auxiliary",
                      "viewportType": "No Title",
                      "description": "Discipline legend — symbol key matching the active discipline",
                      "createReferencePlanes": False, "showCornerMarker": True,
                      "automationHook": "Legend_DisciplineLegendBind"})
        # Revision-history strip — vertical schedule, rotated 90° in Revit
        rev_h = leg_y - drawable_y - 5
        slots.append({"id": "REV", "anchor": [aux_x, drawable_y],
                      "size": [aux_w, rev_h],
                      "purposeTag": "revision-history", "category": "auxiliary",
                      "viewportType": "No Title",
                      "rotation": 0,
                      "description": "Revision-history schedule — auto-populated from Revit's revision data",
                      "createReferencePlanes": False, "showCornerMarker": True,
                      "respectShowToggle": True,
                      "automationHook": "Revisions_AutoPopulateSchedule"})

    # ── Symbol slots — north arrow + scale bar in the title strip ──
    # These overlay the strip so they're visible regardless of which
    # main-plan slot is in use. Sized small per BS 1192 typical.
    if size in ("A0", "A1", "A2", "A3"):
        # North arrow — bottom-right of the drawable, just above the strip
        na_w, na_h = 18, 18
        slots.append({"id": "NA",
                      "anchor": [w - na_w - 15, sh + 7],
                      "size": [na_w, na_h],
                      "purposeTag": "north-arrow", "category": "symbol",
                      "description": "North-arrow symbol — nested family, hides via TB_SHOW_NORTH_ARROW_BOOL",
                      "createReferencePlanes": False, "showCornerMarker": False,
                      "respectShowToggle": True,
                      "automationHook": "Symbol_PlaceNorthArrow"})
        # Scale bar — bottom-left of the drawable, just above the strip
        sb_w, sb_h = round(w * 0.10), 8
        slots.append({"id": "SB",
                      "anchor": [15, sh + 5],
                      "size": [sb_w, sb_h],
                      "purposeTag": "scale-bar", "category": "symbol",
                      "description": "Scale-bar — nested family, hides via TB_SHOW_SCALEBAR_BOOL",
                      "createReferencePlanes": False, "showCornerMarker": False,
                      "respectShowToggle": True,
                      "automationHook": "Symbol_PlaceScaleBar"})

    return {
        "id": fam_id,
        "abstract": True,
        "extends": "A1_common_v2.0",
        "description": desc,
        "templateRft": f"Annotations/Titleblocks/{size} metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True,
             "group": "IdentityData", "default": f"{size}{'P' if is_port else ''}"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": [],
        "slots": slots,
    }


# ─── Concrete BIM / NONBIM working sheets ────────────────────────────────

def polish_concrete(size, orientation, mode, common_id):
    """Generate the BIM identity strip overlay or NONBIM minimal block."""
    is_port = orientation == "PORT"
    is_bim = mode == "BIM"
    suffix = "_PORT_" if is_port else "_"
    fam_id = f"STING_TB_{size}{suffix}{mode}_v2.0"
    if is_port:
        w = SIZES[size][1]
    else:
        w = SIZES[size][0]
    sh = STRIP_H[size]
    bsh = BIM_STRIP_H[size]
    desc_o = "portrait" if is_port else "landscape"

    if is_bim:
        # BIM identity strip sits ABOVE the main bottom strip — y range
        # [sh, sh + bsh].
        y0 = sh
        y1 = sh + bsh
        # Sub-row split: top half = status/suitability/rev, bottom half = 7-seg ID
        ymid = round(y0 + bsh * 0.55, 1)

        lines = [
            {"from": [0, y1], "to": [w, y1], "style": "Medium Lines"},
            {"from": [0, ymid], "to": [w, ymid], "style": "Thin Lines"},
        ]
        # Top-row dividers (5 cells: status / suitability / rev / rev-date / loin)
        x_top = [w*0.22, w*0.42, w*0.55, w*0.70, w*0.85]
        for x in x_top:
            lines.append({"from": [round(x, 1), ymid], "to": [round(x, 1), y1], "style": "Thin Lines"})
        # Bottom-row dividers (8 cells: 7-seg ID + total)
        seg_w = w / 8.0
        for i in range(1, 8):
            x = round(i*seg_w, 1)
            lines.append({"from": [x, y0], "to": [x, ymid], "style": "Thin Lines"})

        sw_pad = max(1.5, w * 0.003)
        ts = TEXT_SCALE[size]
        static = [
            {"text": "STATUS",      "anchor": [sw_pad, y1 - 2*ts],            "size": t(size, 0.9)},
            {"text": "SUITABILITY", "anchor": [w*0.22 + sw_pad, y1 - 2*ts],   "size": t(size, 0.9)},
            {"text": "REV",         "anchor": [w*0.42 + sw_pad, y1 - 2*ts],   "size": t(size, 0.9)},
            {"text": "REV DATE",    "anchor": [w*0.55 + sw_pad, y1 - 2*ts],   "size": t(size, 0.9)},
            {"text": "LOIN/LOD",    "anchor": [w*0.70 + sw_pad, y1 - 2*ts],   "size": t(size, 0.9)},
            {"text": "FED",         "anchor": [w*0.85 + sw_pad, y1 - 2*ts],   "size": t(size, 0.9)},

            {"text": "ISO 19650 SHEET ID", "anchor": [sw_pad, ymid - 2*ts], "size": t(size, 0.8)},
            {"text": "PRJ",   "anchor": [seg_w*0 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "ORIG",  "anchor": [seg_w*1 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "VOL",   "anchor": [seg_w*2 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "LVL",   "anchor": [seg_w*3 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "TYPE",  "anchor": [seg_w*4 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "ROLE",  "anchor": [seg_w*5 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "SEQ",   "anchor": [seg_w*6 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
            {"text": "OF",    "anchor": [seg_w*7 + sw_pad, ymid - 2*ts - 4], "size": t(size, 0.7)},
        ]

        labels = [
            {"param": "PRJ_TB_DELIVERABLE_STATUS_TXT", "anchor": [sw_pad, y1 - bsh*0.7],         "size": t(size, 1.4)},
            {"param": "PRJ_DWG_SUITABILITY_COD_TXT",   "anchor": [w*0.22 + sw_pad, y1 - bsh*0.7], "size": t(size, 1.4)},
            {"param": "PRJ_TB_REVISION_NR_TXT",        "anchor": [w*0.42 + sw_pad, y1 - bsh*0.7], "size": t(size, 1.4)},
            {"param": "PRJ_TB_REVISION_DATE_TXT",      "anchor": [w*0.55 + sw_pad, y1 - bsh*0.7], "size": t(size, 1.0)},
            {"param": "STING_LOIN_LOD_TXT",            "anchor": [w*0.70 + sw_pad, y1 - bsh*0.7], "size": t(size, 1.0)},
            {"param": "STING_FEDERATION_STATUS_TXT",   "anchor": [w*0.85 + sw_pad, y1 - bsh*0.7], "size": t(size, 1.0)},

            # 7-segment cells
            {"param": "STING_SHEET_PROJECT_TXT", "anchor": [seg_w*0 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_ORIG_TXT",    "anchor": [seg_w*1 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_VOLUME_TXT",  "anchor": [seg_w*2 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_LEVEL_TXT",   "anchor": [seg_w*3 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_TYPE_TXT",    "anchor": [seg_w*4 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_ROLE_TXT",    "anchor": [seg_w*5 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_SEQ_TXT",     "anchor": [seg_w*6 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.2)},
            {"param": "STING_SHEET_OF_TOTAL_TXT","anchor": [seg_w*7 + sw_pad, y0 + bsh*0.18], "size": t(size, 1.0)},
        ]

        filled_regions = [
            # Suitability chip — coloured background under the SUITABILITY cell
            {"topLeft": [w*0.22, y1], "bottomRight": [w*0.42, ymid],
             "fillTypeName": "Solid fill", "color": "#F2A341"},
            # Status band — narrow stripe at the top edge of the BIM strip
            {"topLeft": [0, y1], "bottomRight": [w, y1 - 1.5],
             "fillTypeName": "Solid fill", "color": "#1F4E79"},
        ]

        params = [
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "BIM"},
            {"name": "STING_SHEET_PROJECT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "STG"},
            {"name": "STING_SHEET_ORIG_TXT",     "kind": "shared", "instance": True, "group": "IdentityData", "default": "PLNS"},
            {"name": "STING_SHEET_VOLUME_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "ZZ"},
            {"name": "STING_SHEET_LEVEL_TXT",    "kind": "shared", "instance": True, "group": "IdentityData", "default": "01"},
            {"name": "STING_SHEET_TYPE_TXT",     "kind": "shared", "instance": True, "group": "IdentityData", "default": "DR"},
            {"name": "STING_SHEET_ROLE_TXT",     "kind": "shared", "instance": True, "group": "IdentityData", "default": "A"},
            {"name": "STING_SHEET_SEQ_TXT",      "kind": "shared", "instance": True, "group": "IdentityData", "default": "0001"},
            {"name": "STING_SHEET_FULL_REF_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "STG-PLNS-ZZ-01-DR-A-0001"},
            {"name": "STING_SHEET_OF_TOTAL_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "01 / 01"},
            {"name": "PRJ_DWG_SUITABILITY_COD_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "S2"},
            {"name": "STING_SUITABILITY_DESC_TXT",    "kind": "shared", "instance": True, "group": "IdentityData", "default": "Shared, Non-contractual"},
            {"name": "PRJ_DWG_ISSUE_PURPOSE_TXT",     "kind": "shared", "instance": True, "group": "IdentityData", "default": "FOR INFORMATION"},
            {"name": "PRJ_TB_DELIVERABLE_STATUS_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "Shared"},
            {"name": "PRJ_TB_REVISION_NR_TXT",        "kind": "shared", "instance": True, "group": "IdentityData", "default": "P01"},
            {"name": "PRJ_TB_REVISION_DATE_TXT",      "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_REVISION_DESCRIPTION_TXT","kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_LAST_TRANSMITTAL_TXT",   "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_DELIVERABLE_CDE_TXT",    "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "STING_FEDERATION_STATUS_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "Federated"},
            {"name": "STING_LOIN_LOD_TXT",            "kind": "shared", "instance": True, "group": "IdentityData", "default": "LOD 300"},
            {"name": "STING_AUTHORISED_BY_TXT",       "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "STING_AUTHORISED_DATE_TXT",     "kind": "shared", "instance": True, "group": "IdentityData"},
        ]
    else:
        # NONBIM — minimal sheet-number block in the right column of the
        # parent's strip. Just a couple of lines + sheet number label.
        c4 = round(w * 0.86)
        params = [
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
            {"name": "PRJ_TB_SHEET_NR_TXT",      "kind": "shared", "instance": True, "group": "IdentityData", "default": "A-001"},
            {"name": "PRJ_TB_REVISION_NR_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "0"},
            {"name": "PRJ_TB_REVISION_DATE_TXT", "kind": "shared", "instance": True, "group": "IdentityData"},
        ]
        # NONBIM sheet number rendered LARGE in the rightmost column
        lines = []
        static = [
            {"text": "SHEET NUMBER", "anchor": [c4 + 2, sh*0.85], "size": t(size, 1.2)},
            {"text": "REV",          "anchor": [c4 + 2, sh*0.30], "size": t(size, 1.0)},
        ]
        labels = [
            {"param": "PRJ_TB_SHEET_NR_TXT",      "anchor": [c4 + 2, sh*0.55], "size": t(size, 4.0)},
            {"param": "PRJ_TB_REVISION_NR_TXT",   "anchor": [c4 + 2, sh*0.10], "size": t(size, 1.5)},
        ]
        filled_regions = []

    return {
        "id": fam_id,
        "extends": common_id,
        "mode": mode,
        "description": (
            f"{size} {desc_o} working sheet — "
            f"{'full ISO 19650 BIM identity (top BIM strip with status / suitability chip / revision / LOIN / federation, plus 7-segment sheet ID row)' if is_bim else 'minimal NONBIM identity (large sheet-number block in the right column)'}. "
            f"Extends {common_id} — inherits the 5-column bottom strip + slots."
        ),
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "parameters": params,
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
    }


# ─── Specialty families ─────────────────────────────────────────────────

def polish_assembly(fam_id: str, discipline: str, accent_color: str) -> dict:
    """Pipe / Duct / Conduit / Hanger fabrication assembly title block.
    A1 landscape, BOM strip on the right (200 mm), bottom title strip
    (80 mm tall × 641 mm wide) with fab metadata."""
    w, h = 841, 594
    bom_w  = 200
    title_h = 80
    drawable_w = w - bom_w - 5
    drawable_h = h - title_h - 5
    desc_disc = {"PIPE": "pipe spool", "DUCT": "duct spool",
                 "COND": "conduit / cable assembly", "HANGER": "hanger / support"}[discipline]

    lines = [
        # Outer
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        # Title strip top edge
        {"from": [0, title_h], "to": [w, title_h], "style": "Medium Lines"},
        # BOM strip left edge
        {"from": [w - bom_w, title_h], "to": [w - bom_w, h], "style": "Medium Lines"},
        # Title strip internal: 5 cells
        {"from": [200, 0], "to": [200, title_h], "style": "Thin Lines"},
        {"from": [400, 0], "to": [400, title_h], "style": "Thin Lines"},
        {"from": [560, 0], "to": [560, title_h], "style": "Thin Lines"},
        {"from": [700, 0], "to": [700, title_h], "style": "Thin Lines"},
        # Title strip horizontal mid
        {"from": [0, title_h*0.5], "to": [200, title_h*0.5], "style": "Thin Lines"},
        # BOM header row
        {"from": [w - bom_w, h - 22], "to": [w, h - 22], "style": "Thin Lines"},
    ]
    # BOM table — 6 cols (item / qty / size / material / spec / weight)
    col_widths = [22, 18, 30, 50, 50, 30]
    cx = w - bom_w
    for cw in col_widths:
        cx += cw
        lines.append({"from": [cx, h - 22], "to": [cx, title_h], "style": "Thin Lines"})
    # BOM rows — 18 row dividers
    rh = (h - 22 - title_h) / 18.0
    for i in range(1, 18):
        y = round(title_h + i * rh, 1)
        lines.append({"from": [w - bom_w, y], "to": [w, y], "style": "Thin Lines"})

    static = [
        {"text": "SPOOL #",    "anchor": [4, title_h - 4], "size": 1.3},
        {"text": "DISCIPLINE", "anchor": [204, title_h - 4], "size": 1.0},
        {"text": "WEIGHT",     "anchor": [204, title_h*0.5 - 4], "size": 1.0},
        {"text": "FAB LOC",    "anchor": [404, title_h - 4], "size": 1.0},
        {"text": "STATUS",     "anchor": [404, title_h*0.5 - 4], "size": 1.0},
        {"text": "BOM REV",    "anchor": [564, title_h - 4], "size": 1.0},
        {"text": "ISSUE DATE", "anchor": [564, title_h*0.5 - 4], "size": 1.0},
        {"text": "PROJECT",    "anchor": [704, title_h - 4], "size": 1.0},
        {"text": "DRG NO.",    "anchor": [704, title_h*0.5 - 4], "size": 1.0},

        {"text": f"BILL OF MATERIALS — {discipline}", "anchor": [w - bom_w + 4, h - 8], "size": 1.6},
        {"text": "ITEM",   "anchor": [w - bom_w + 2, h - 28], "size": 0.9},
        {"text": "QTY",    "anchor": [w - bom_w + 24, h - 28], "size": 0.9},
        {"text": "SIZE",   "anchor": [w - bom_w + 44, h - 28], "size": 0.9},
        {"text": "MATERIAL","anchor": [w - bom_w + 76, h - 28], "size": 0.9},
        {"text": "SPEC",   "anchor": [w - bom_w + 128, h - 28], "size": 0.9},
        {"text": "WEIGHT", "anchor": [w - bom_w + 180, h - 28], "size": 0.9},
    ]
    labels = [
        {"param": "PRJ_TB_VARIANT_TXT",          "anchor": [4,   title_h - 18], "size": 3.0},
        {"param": "PRJ_TB_DISCIPLINE_TXT",       "anchor": [204, title_h - 18], "size": 1.6},
        {"param": "PRJ_TB_NOTES_LEGEND_REF_TXT", "anchor": [204, title_h*0.5 - 18], "size": 1.4},
        {"param": "STING_FEDERATION_STATUS_TXT", "anchor": [404, title_h - 18], "size": 1.4},
        {"param": "PRJ_TB_DELIVERABLE_STATUS_TXT","anchor":[404, title_h*0.5 - 18], "size": 1.4},
        {"param": "PRJ_TB_REVISION_NR_TXT",      "anchor": [564, title_h - 18], "size": 1.4},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",       "anchor": [564, title_h*0.5 - 18], "size": 1.4},
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",    "anchor": [704, title_h - 18], "size": 1.4},
        {"param": "STING_SHEET_FULL_REF_TXT",    "anchor": [704, title_h*0.5 - 18], "size": 1.4},
    ]
    filled_regions = [
        # Discipline accent stripe across the top of the title strip
        {"topLeft": [0, title_h], "bottomRight": [w - bom_w, title_h - 4],
         "fillTypeName": "Solid fill", "color": accent_color},
    ]
    slots = [
        {"id": "ISO", "anchor": [10, title_h + 5], "size": [drawable_w - 5, drawable_h - 5],
         "purposeTag": "fabrication-isometric", "category": "primary",
         "viewportType": "Title w/ Line", "scaleHint": 25,
         "description": f"Main fabrication isometric / spool drawing area for the {desc_disc}",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "BOM", "anchor": [w - bom_w + 2, title_h + 5],
         "size": [bom_w - 4, h - title_h - 30],
         "purposeTag": "bom", "category": "auxiliary",
         "viewportType": "No Title",
         "description": "Bill of Materials — Revit Schedule view inside the right-strip",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Fab_BuildBOMSchedule"},
        {"id": "CUT", "anchor": [w - bom_w + 2, h - 22],
         "size": [bom_w - 4, 18],
         "purposeTag": "cut-list", "category": "auxiliary",
         "viewportType": "No Title",
         "description": "Cut list / lengths summary — small schedule",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Fab_BuildCutList"},
        {"id": "REF", "anchor": [10, title_h + drawable_h - 28],
         "size": [60, 22],
         "purposeTag": "spool-refs", "category": "overlay",
         "viewportType": "No Title",
         "description": "Spool reference list — drafting view callout linking to other spools",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Fab_LinkSpoolRefs"},
    ]

    return {
        "id": fam_id,
        "extends": "A1_common_v2.0",
        "description": (f"Fabrication assembly title block — {desc_disc}. "
                        f"A1 landscape, 200 mm BOM strip on the right (6 cols × 18 rows), "
                        f"80 mm bottom title strip with fab metadata "
                        f"(SPOOL # / DISCIPLINE / WEIGHT / FAB LOC / STATUS / BOM REV / ISSUE DATE / PROJECT / DRG NO). "
                        f"Discipline accent stripe in {accent_color}. "
                        f"Drawable {drawable_w}×{drawable_h} mm — fits a typical 1:25 spool isometric."),
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",    "kind": "shared", "instance": True, "group": "IdentityData", "default": f"FAB-{discipline}"},
            {"name": "PRJ_TB_DISCIPLINE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": discipline},
            {"name": "STING_SHEET_BIM_MODE_TXT","kind": "shared", "instance": True, "group": "IdentityData", "default": "BIM"},
            {"name": "STING_SHEET_FULL_REF_TXT","kind": "shared", "instance": True, "group": "IdentityData", "default": f"STG-PLNS-ZZ-01-FB-{discipline[0]}-0001"},
            {"name": "PRJ_TB_REVISION_NR_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "P01"},
            {"name": "PRJ_TB_DELIVERABLE_STATUS_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "FAB"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
        "slots": slots,
    }


def polish_present(fam_id: str, mono: bool) -> dict:
    """Presentation title block — full-bleed render area, minimal corner watermark."""
    w, h = 841, 594
    accent = "#1F4E79" if not mono else "#222222"
    label_fg = "#FFFFFF" if not mono else "#FFFFFF"

    # Bottom-right corner watermark only — 280 × 60 mm
    cw, ch = 280, 60
    cx, cy = w - cw, 0

    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        {"from": [cx, ch], "to": [w, ch], "style": "Medium Lines"},
        {"from": [cx, 0],  "to": [cx, ch], "style": "Medium Lines"},
        {"from": [cx + 100, 0], "to": [cx + 100, ch], "style": "Thin Lines"},
        {"from": [cx + 200, 0], "to": [cx + 200, ch], "style": "Thin Lines"},
        {"from": [cx, ch * 0.5], "to": [w, ch * 0.5], "style": "Thin Lines"},
    ]
    static = [
        {"text": "PROJECT", "anchor": [cx + 4, ch - 4], "size": 1.0},
        {"text": "TITLE",   "anchor": [cx + 104, ch - 4], "size": 1.0},
        {"text": "REV",     "anchor": [cx + 204, ch - 4], "size": 1.0},
        {"text": "DATE",    "anchor": [cx + 4, ch*0.5 - 4], "size": 1.0},
        {"text": "DRAWN",   "anchor": [cx + 104, ch*0.5 - 4], "size": 1.0},
        {"text": "CLIENT",  "anchor": [cx + 204, ch*0.5 - 4], "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_ORG_PROJECT_NAME_TXT", "anchor": [cx + 4, ch - 18], "size": 1.6},
        {"param": "PRJ_ORG_PROJECT_NAME_TXT", "anchor": [cx + 104, ch - 18], "size": 1.6},
        {"param": "PRJ_TB_REVISION_NR_TXT",   "anchor": [cx + 204, ch - 18], "size": 1.6},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",    "anchor": [cx + 4, ch*0.5 - 18], "size": 1.4},
        {"param": "PRJ_TB_DRAWN_BY_TXT",      "anchor": [cx + 104, ch*0.5 - 18], "size": 1.4},
        {"param": "PRJ_TB_CLIENT_NAME_TXT",   "anchor": [cx + 204, ch*0.5 - 18], "size": 1.4},
    ]
    filled_regions = [
        # Coloured accent block under the watermark
        {"topLeft": [cx, ch], "bottomRight": [w, ch - 5],
         "fillTypeName": "Solid fill", "color": accent},
    ]
    slots = [
        {"id": "RENDER", "anchor": [10, 10], "size": [w - 20, h - 20],
         "purposeTag": "presentation-render", "category": "primary",
         "scaleHint": 50, "aspectLock": True,
         "description": "Full-bleed render / perspective area — aspect-locked",
         "createReferencePlanes": False, "showCornerMarker": False},
        {"id": "CAPTION", "anchor": [w - cw + 4, ch + 4], "size": [cw - 8, 14],
         "purposeTag": "caption", "category": "overlay",
         "viewportType": "No Title",
         "description": "Caption strip overlay — drawing title + scale annotation",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Legend_BuildCaption"},
    ]
    return {
        "id": fam_id,
        "extends": "A1_common_v2.0",
        "description": (f"Presentation title block ({'monochrome' if mono else 'colour'}) — "
                        f"A1 landscape, FULL-BLEED render area (entire sheet is the slot). "
                        f"Bottom-right 280×60 mm watermark with project / title / rev / date / drawn / client. "
                        f"Designed for renders, perspectives, full-page axonometrics — the watermark is "
                        f"deliberately minimal so the artwork dominates."),
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData",
             "default": "PRESENT-MONO" if mono else "PRESENT"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
        "slots": slots,
    }


def polish_cover() -> dict:
    """Cover sheet — large logo + project banner + deliverable code. No drawable zone."""
    w, h = 841, 594
    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        # Decorative bands
        {"from": [0, h - 60], "to": [w, h - 60], "style": "Wide Lines"},
        {"from": [0, 100],     "to": [w, 100],     "style": "Wide Lines"},
        {"from": [0, 60],      "to": [w, 60],      "style": "Medium Lines"},
        {"from": [w/2, 0], "to": [w/2, 100], "style": "Thin Lines"},
    ]
    static = [
        {"text": "PROJECT", "anchor": [4, h - 14], "size": 1.4},
        {"text": "DELIVERABLE", "anchor": [4, 80], "size": 1.0},
        {"text": "REVISION",    "anchor": [4, 40], "size": 1.0},
        {"text": "ISSUE DATE",  "anchor": [w/2 + 4, 80], "size": 1.0},
        {"text": "TRANSMITTAL", "anchor": [w/2 + 4, 40], "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",     "anchor": [w/2, h/2 + 30],   "size": 16.0, "hAlign": "Center"},
        {"param": "PRJ_ORG_PROJECT_ADDRESS_TXT",  "anchor": [w/2, h/2 + 5],    "size": 4.0,  "hAlign": "Center"},
        {"param": "PRJ_TB_CLIENT_NAME_TXT",       "anchor": [w/2, h/2 - 40],   "size": 6.0,  "hAlign": "Center"},
        {"param": "PRJ_ORG_RIBA_STAGE_TXT",       "anchor": [w/2, h/2 - 80],   "size": 3.0,  "hAlign": "Center"},
        {"param": "PRJ_DWG_ISSUE_PURPOSE_TXT",    "anchor": [w/2, h/2 - 110],  "size": 4.0,  "hAlign": "Center"},
        {"param": "PRJ_TB_DELIVERABLE_STATUS_TXT","anchor": [4, 70],           "size": 2.0},
        {"param": "PRJ_TB_REVISION_NR_TXT",       "anchor": [4, 30],           "size": 3.0},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",        "anchor": [w/2 + 4, 70],     "size": 2.0},
        {"param": "PRJ_TB_LAST_TRANSMITTAL_TXT",  "anchor": [w/2 + 4, 30],     "size": 2.0},
    ]
    filled_regions = [
        # Top accent banner
        {"topLeft": [0, h], "bottomRight": [w, h - 60],
         "fillTypeName": "Solid fill", "color": "#1F4E79"},
        # Bottom accent band
        {"topLeft": [0, 100], "bottomRight": [w, 95],
         "fillTypeName": "Solid fill", "color": "#F2A341"},
    ]
    slots = []  # No drawable zone on a cover sheet
    return {
        "id": "STING_TB_COVER_A1_v1.0",
        "extends": "A1_common_v2.0",
        "description": (
            "Project / package cover sheet — A1 landscape, NO drawable zone "
            "(viewport placement deliberately blocked). Top dark-blue banner, "
            "amber accent band, large centred project name + address + client + RIBA stage. "
            "Bottom strip carries deliverable status, revision, issue date, transmittal ref."
        ),
        "saveAs": "Families/TitleBlocks/STING_TB_COVER_A1_v1.0.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "COVER"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
        "slots": slots,
    }


def polish_divider() -> dict:
    """Discipline section divider — single big discipline label, minimal id strip."""
    w, h = 841, 594
    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        {"from": [0, 50], "to": [w, 50], "style": "Medium Lines"},
        {"from": [w/3, 0], "to": [w/3, 50], "style": "Thin Lines"},
        {"from": [2*w/3, 0], "to": [2*w/3, 50], "style": "Thin Lines"},
    ]
    static = [
        {"text": "DISCIPLINE",  "anchor": [4, 40], "size": 1.0},
        {"text": "PROJECT",     "anchor": [w/3 + 4, 40], "size": 1.0},
        {"text": "DELIVERABLE", "anchor": [2*w/3 + 4, 40], "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_TB_DISCIPLINE_TXT",     "anchor": [w/2, h/2],         "size": 60.0, "hAlign": "Center"},
        {"param": "PRJ_ORG_PROJECT_CODE_TXT",  "anchor": [w/2, h/2 - 75],    "size": 8.0,  "hAlign": "Center"},
        {"param": "PRJ_TB_DISCIPLINE_TXT",     "anchor": [4, 20],            "size": 2.0},
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",  "anchor": [w/3 + 4, 20],      "size": 2.0},
        {"param": "PRJ_TB_DELIVERABLE_DATADROP_TXT", "anchor": [2*w/3 + 4, 20], "size": 2.0},
    ]
    filled_regions = [
        # Big discipline-coded background panel (defaults to neutral; project sets via filter)
        {"topLeft": [40, h - 40], "bottomRight": [w - 40, 80],
         "fillTypeName": "Solid fill", "color": "#E8E8E8"},
    ]
    return {
        "id": "STING_TB_DIVIDER_A1_v1.0",
        "extends": "A1_common_v2.0",
        "description": (
            "Discipline section divider — A1 landscape, large grey background panel, "
            "huge centred discipline label (60 mm — e.g. 'MEP', 'STRUCTURAL', 'ARCHITECTURAL'), "
            "project code below. Minimal 50 mm bottom strip carries discipline / project / "
            "deliverable identifiers. Used between disciplines in a multi-discipline package."
        ),
        "saveAs": "Families/TitleBlocks/STING_TB_DIVIDER_A1_v1.0.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "DIVIDER"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
        "slots": [
            {"id": "BAND",
             "anchor": [40, 80], "size": [w - 80, h - 120],
             "purposeTag": "discipline-band", "category": "auxiliary",
             "viewportType": "No Title",
             "description": "Discipline-coloured panel — auto-tinted by the project's discipline band rule",
             "createReferencePlanes": False, "showCornerMarker": True,
             "respectShowToggle": True,
             "automationHook": "TB_ApplyDisciplineBand"},
        ],
    }


def polish_register() -> dict:
    """Drawing register sheet — table fills the page, identity strip at bottom."""
    w, h = 841, 594
    title_h = 60
    table_y0 = title_h
    table_y1 = h - 30  # leaves room for the schedule title
    rows = 24
    rh = (table_y1 - table_y0) / rows
    cols = [40, 280, 60, 50, 50, 80, 80, 80, 80]
    cumx = [0]
    for c in cols:
        cumx.append(cumx[-1] + c)
    # Scale columns to fit width
    factor = w / cumx[-1]
    cumx = [round(x * factor, 1) for x in cumx]

    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        {"from": [0, title_h], "to": [w, title_h], "style": "Medium Lines"},
        {"from": [0, table_y1], "to": [w, table_y1], "style": "Medium Lines"},
        {"from": [0, h - 30], "to": [w, h - 30], "style": "Thin Lines"},
    ]
    # Vertical column dividers
    for x in cumx[1:-1]:
        lines.append({"from": [x, table_y0], "to": [x, table_y1], "style": "Thin Lines"})
    # Horizontal row dividers
    for i in range(1, rows + 1):
        y = round(table_y0 + i * rh, 1)
        lines.append({"from": [0, y], "to": [w, y], "style": "Thin Lines"})

    headers = ["#", "DRAWING TITLE", "SHEET NO", "REV", "STATUS", "DATE", "DRAWN", "CHKD", "APVD"]
    static = [{"text": "DRAWING REGISTER", "anchor": [w/2, h - 12], "size": 4.0, "hAlign": "Center"},
              {"text": "PROJECT",      "anchor": [4, title_h - 4], "size": 1.0},
              {"text": "DELIVERABLE",  "anchor": [w/3 + 4, title_h - 4], "size": 1.0},
              {"text": "REV / DATE",   "anchor": [2*w/3 + 4, title_h - 4], "size": 1.0}]
    for i, hdr in enumerate(headers):
        static.append({"text": hdr, "anchor": [cumx[i] + 2, table_y1 + rh*0.4], "size": 1.2})

    labels = [
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",       "anchor": [4, title_h - 18], "size": 2.5},
        {"param": "PRJ_TB_DELIVERABLE_DATADROP_TXT","anchor": [w/3 + 4, title_h - 18], "size": 2.0},
        {"param": "PRJ_TB_REVISION_NR_TXT",         "anchor": [2*w/3 + 4, title_h - 18], "size": 2.0},
    ]
    register_slots = [
        {"id": "REGISTER",
         "anchor": [4, table_y0 + 2], "size": [w - 8, table_y1 - table_y0 - 4],
         "purposeTag": "schedule", "category": "primary",
         "viewportType": "No Title",
         "description": "Drawing-register schedule view — Revit Sheet List populated by ExportSheetRegister",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "ExportSheetRegister"},
    ]
    return {
        "id": "STING_TB_REGISTER_A1_v1.0",
        "extends": "A1_common_v2.0",
        "description": (
            f"Drawing register sheet — A1 landscape. Full-page 9-column × {rows}-row schedule "
            f"(# / DRAWING TITLE / SHEET NO / REV / STATUS / DATE / DRAWN / CHKD / APVD). "
            f"60 mm bottom strip carries project + deliverable + rev. The actual register is "
            f"populated by ExportSheetRegister at print time — this title block just provides "
            f"the static frame."
        ),
        "saveAs": "Families/TitleBlocks/STING_TB_REGISTER_A1_v1.0.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "REGISTER"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "BIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": [],
        "slots": register_slots,
    }


def polish_transmittal() -> dict:
    """Transmittal cover sheet — A4 landscape (297×210), recipient + drawings + signatures."""
    w, h = 297, 210
    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        # Header strip
        {"from": [0, h - 30], "to": [w, h - 30], "style": "Medium Lines"},
        # 4-column header
        {"from": [w*0.30, h - 30], "to": [w*0.30, h], "style": "Thin Lines"},
        {"from": [w*0.55, h - 30], "to": [w*0.55, h], "style": "Thin Lines"},
        {"from": [w*0.78, h - 30], "to": [w*0.78, h], "style": "Thin Lines"},
        # Recipient block
        {"from": [0, h - 70], "to": [w/2, h - 70], "style": "Thin Lines"},
        {"from": [w/2, h - 30], "to": [w/2, h - 70], "style": "Medium Lines"},
        # Body table separator
        {"from": [0, h - 100], "to": [w, h - 100], "style": "Medium Lines"},
        # Signature footer
        {"from": [0, 30], "to": [w, 30], "style": "Medium Lines"},
        {"from": [w/3, 0], "to": [w/3, 30], "style": "Thin Lines"},
        {"from": [2*w/3, 0], "to": [2*w/3, 30], "style": "Thin Lines"},
    ]
    static = [
        {"text": "TRANSMITTAL", "anchor": [4, h - 8], "size": 3.0},
        {"text": "TX REF",      "anchor": [w*0.30 + 2, h - 8], "size": 1.0},
        {"text": "DATE",        "anchor": [w*0.55 + 2, h - 8], "size": 1.0},
        {"text": "REV",         "anchor": [w*0.78 + 2, h - 8], "size": 1.0},
        {"text": "TO",          "anchor": [4, h - 35], "size": 1.0},
        {"text": "FROM",        "anchor": [w/2 + 4, h - 35], "size": 1.0},
        {"text": "ACCOMPANYING DOCUMENTS", "anchor": [4, h - 85], "size": 1.4},
        {"text": "SHEET NO / TITLE / REV / STATUS / DATE", "anchor": [4, h - 105], "size": 1.0},

        {"text": "SIGNED",        "anchor": [4, 25], "size": 1.0},
        {"text": "DATE RECEIVED", "anchor": [w/3 + 4, 25], "size": 1.0},
        {"text": "ACKNOWLEDGED",  "anchor": [2*w/3 + 4, 25], "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_TB_LAST_TRANSMITTAL_TXT",      "anchor": [w*0.30 + 2, h - 22], "size": 1.6},
        {"param": "PRJ_TB_LAST_TRANSMITTAL_DATE_TXT", "anchor": [w*0.55 + 2, h - 22], "size": 1.4},
        {"param": "PRJ_TB_REVISION_NR_TXT",           "anchor": [w*0.78 + 2, h - 22], "size": 1.6},
        {"param": "PRJ_TB_CLIENT_NAME_TXT",     "anchor": [4, h - 50], "size": 1.6},
        {"param": "PRJ_TB_CLIENT_ADDRESS_TXT",  "anchor": [4, h - 60], "size": 1.0},
        {"param": "PRJ_ORG_COMPANY_NAME_TXT",   "anchor": [w/2 + 4, h - 50], "size": 1.6},
        {"param": "PRJ_ORG_COMPANY_ADDRESS_TXT","anchor": [w/2 + 4, h - 60], "size": 1.0},
        {"param": "PRJ_TB_DRAWN_BY_TXT",        "anchor": [4, 8], "size": 1.6},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",      "anchor": [w/3 + 4, 8], "size": 1.4},
    ]
    return {
        "id": "STING_TB_TRANSMITTAL_A4_v1.0",
        "extends": "A1_common_v2.0",
        "description": (
            "Transmittal cover sheet — A4 landscape (297×210 mm). Header carries TX REF / DATE / REV. "
            "Two-column recipient block (TO / FROM) with name + address. Middle: 'ACCOMPANYING DOCUMENTS' "
            "table that ExportSheetSet populates with the drawings being transmitted. "
            "Bottom 30 mm signature strip with SIGNED / DATE RECEIVED / ACKNOWLEDGED columns."
        ),
        "saveAs": "Families/TitleBlocks/STING_TB_TRANSMITTAL_A4_v1.0.rfa",
        "templateRft": "Annotations/Titleblocks/A4 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A4"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "TRANSMITTAL"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": [
            # Title accent
            {"topLeft": [0, h], "bottomRight": [w, h - 30],
             "fillTypeName": "Solid fill", "color": "#1F4E79"},
        ],
        "slots": [
            {"id": "TO",
             "anchor": [4, h - 70], "size": [w/2 - 4, 40],
             "purposeTag": "recipient-to", "category": "auxiliary",
             "viewportType": "No Title",
             "description": "Recipient (TO) block — populated from Transmittal recipient list",
             "createReferencePlanes": False, "showCornerMarker": True,
             "automationHook": "Transmittal_PopulateRecipient"},
            {"id": "FROM",
             "anchor": [w/2 + 4, h - 70], "size": [w/2 - 8, 40],
             "purposeTag": "recipient-from", "category": "auxiliary",
             "viewportType": "No Title",
             "description": "Sender (FROM) block — auto-filled from PRJ_ORG_COMPANY_*",
             "createReferencePlanes": False, "showCornerMarker": True,
             "automationHook": "Transmittal_PopulateSender"},
            {"id": "DOCS",
             "anchor": [4, 35], "size": [w - 8, h - 100 - 35],
             "purposeTag": "schedule", "category": "primary",
             "viewportType": "No Title",
             "description": "Accompanying-documents schedule — populated from the export bundle",
             "createReferencePlanes": False, "showCornerMarker": True,
             "automationHook": "Transmittal_BuildAccompanying"},
        ],
    }


def polish_submission(authority: str, accent_color: str, full_name: str, statutory_text: str) -> dict:
    """Statutory submission title block (KCCA / ERA / NEMA) — A1 landscape with regulator banner."""
    w, h = 841, 594
    fam_id = f"STING_TB_SUBMISSION_{authority}_v1.0"
    banner_h = 80

    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        # Regulator banner separator
        {"from": [0, h - banner_h], "to": [w, h - banner_h], "style": "Wide Lines"},
        # Bottom identity strip
        {"from": [0, 60], "to": [w, 60], "style": "Medium Lines"},
        {"from": [w/4, 0], "to": [w/4, 60], "style": "Thin Lines"},
        {"from": [w/2, 0], "to": [w/2, 60], "style": "Thin Lines"},
        {"from": [3*w/4, 0], "to": [3*w/4, 60], "style": "Thin Lines"},
        # Mid disclaimer band
        {"from": [0, 100], "to": [w, 100], "style": "Thin Lines"},
        {"from": [0, 120], "to": [w, 120], "style": "Thin Lines"},
    ]
    static = [
        {"text": full_name, "anchor": [w/2, h - banner_h/2], "size": 6.0, "hAlign": "Center"},
        {"text": f"STATUTORY SUBMISSION — {authority}", "anchor": [w/2, h - banner_h + 12], "size": 1.6, "hAlign": "Center"},
        {"text": statutory_text, "anchor": [w/2, 110], "size": 1.0, "hAlign": "Center"},
        {"text": "PROJECT",       "anchor": [4, 56], "size": 1.0},
        {"text": "PERMIT NO",     "anchor": [w/4 + 4, 56], "size": 1.0},
        {"text": "SUBMISSION DT", "anchor": [w/2 + 4, 56], "size": 1.0},
        {"text": "AUTHORISED BY", "anchor": [3*w/4 + 4, 56], "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",     "anchor": [4, 35], "size": 2.4},
        {"param": "PRJ_TB_LAST_TRANSMITTAL_TXT",  "anchor": [w/4 + 4, 35], "size": 2.0},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",        "anchor": [w/2 + 4, 35], "size": 2.0},
        {"param": "STING_AUTHORISED_BY_TXT",      "anchor": [3*w/4 + 4, 35], "size": 2.0},
        {"param": "PRJ_ORG_PROJECT_ADDRESS_TXT",  "anchor": [4, 12], "size": 1.4},
    ]
    filled_regions = [
        # Regulator banner background
        {"topLeft": [0, h], "bottomRight": [w, h - banner_h],
         "fillTypeName": "Solid fill", "color": accent_color},
    ]
    slots = [
        {"id": "S01", "anchor": [10, 130], "size": [w - 20 - 80, h - banner_h - 145],
         "purposeTag": "main-plan", "category": "primary",
         "viewportType": "Title w/ Line", "scaleHint": 100,
         "description": f"Main drawing area for the {authority} submission",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "STAMP", "anchor": [w - 90, 130], "size": [70, 70],
         "purposeTag": "regulator-stamp", "category": "auxiliary",
         "viewportType": "No Title",
         "description": f"{authority} regulator stamp — placeholder for the official seal scanned in at submission time",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": f"Submission_PlaceStamp_{authority}"},
        {"id": "DECL", "anchor": [w - 90, 130 + 78], "size": [70, h - banner_h - 145 - 78 - 5],
         "purposeTag": "notes", "category": "auxiliary",
         "viewportType": "No Title",
         "description": "Statutory declaration / consultant signatures legend",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Legend_BuildStatutoryDeclaration"},
    ]
    return {
        "id": fam_id,
        "extends": "A1_common_v2.0",
        "description": (
            f"Statutory submission title block — {full_name} ({authority}). A1 landscape with a "
            f"prominent {banner_h} mm regulator banner across the top, statutory disclaimer band "
            f"under it, and a 4-cell bottom identity strip (PROJECT / PERMIT NO / SUBMISSION DATE / "
            f"AUTHORISED BY). Banner accent in {accent_color}. Drawable zone {w-20} × {h-banner_h-145} mm."
        ),
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A1"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData",
             "default": f"SUBMISSION-{authority}"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
            {"name": "STING_AUTHORISED_BY_TXT",  "kind": "shared", "instance": True, "group": "IdentityData"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": filled_regions,
        "slots": slots,
    }


def polish_clarification() -> dict:
    """RFI / clarification sketch sheet — A3 landscape (420 × 297)."""
    w, h = 420, 297
    lines = [
        {"from": [0, 0], "to": [w, 0], "style": "Wide Lines"},
        {"from": [w, 0], "to": [w, h], "style": "Wide Lines"},
        {"from": [w, h], "to": [0, h], "style": "Wide Lines"},
        {"from": [0, h], "to": [0, 0], "style": "Wide Lines"},
        {"from": [0, h - 25], "to": [w, h - 25], "style": "Medium Lines"},
        {"from": [w/2, 30], "to": [w/2, h - 25], "style": "Medium Lines"},
        # Bottom identity strip
        {"from": [0, 30], "to": [w, 30], "style": "Medium Lines"},
        {"from": [w/3, 0], "to": [w/3, 30], "style": "Thin Lines"},
        {"from": [2*w/3, 0], "to": [2*w/3, 30], "style": "Thin Lines"},
    ]
    static = [
        {"text": "RFI / CLARIFICATION", "anchor": [4, h - 10], "size": 2.4},
        {"text": "RFI REF",  "anchor": [3*w/4, h - 10], "size": 1.2},
        {"text": "QUERY",    "anchor": [4, h - 35],     "size": 1.4},
        {"text": "SKETCH",   "anchor": [w/2 + 4, h - 35], "size": 1.4},
        {"text": "PROJECT",  "anchor": [4, 24],          "size": 1.0},
        {"text": "DATE",     "anchor": [w/3 + 4, 24],    "size": 1.0},
        {"text": "REV",      "anchor": [2*w/3 + 4, 24],  "size": 1.0},
    ]
    labels = [
        {"param": "PRJ_TB_LAST_TRANSMITTAL_TXT", "anchor": [3*w/4, h - 22], "size": 2.0},
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",    "anchor": [4, 8], "size": 1.8},
        {"param": "PRJ_TB_DATE_DRAWN_TXT",       "anchor": [w/3 + 4, 8], "size": 1.6},
        {"param": "PRJ_TB_REVISION_NR_TXT",      "anchor": [2*w/3 + 4, 8], "size": 1.6},
    ]
    slots = [
        {"id": "QUERY",  "anchor": [10, 35],       "size": [w/2 - 15, h - 65],
         "purposeTag": "rfi-query", "category": "auxiliary",
         "scaleHint": 100,
         "description": "Left half — RFI query text area (legend or schedule view)",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Legend_BuildRFIQuery"},
        {"id": "SKETCH", "anchor": [w/2 + 5, 35], "size": [w/2 - 15, h - 65],
         "purposeTag": "rfi-sketch", "category": "primary",
         "scaleHint": 50,
         "description": "Right half — sketch / detail / annotated plan view",
         "createReferencePlanes": True, "showCornerMarker": True},
        {"id": "MARKUP", "anchor": [w/2 + 5, 35], "size": [w/2 - 15, h - 65],
         "purposeTag": "markup-plan", "category": "overlay",
         "viewportType": "No Title",
         "description": "Markup overlay — review-cloud detail view layered on top of SKETCH",
         "createReferencePlanes": False, "showCornerMarker": True,
         "automationHook": "Markup_AttachReviewCloud"},
        {"id": "REV", "anchor": [w/2 + 5, h - 28], "size": [w/2 - 15, 16],
         "purposeTag": "revision-history", "category": "auxiliary",
         "viewportType": "No Title",
         "description": "Tiny revision strip above the sketch — RFI lifecycle history",
         "createReferencePlanes": False, "showCornerMarker": True,
         "respectShowToggle": True,
         "automationHook": "Revisions_AutoPopulateSchedule"},
    ]
    return {
        "id": "STING_TB_CLARIFICATION_A3_v1.0",
        "extends": "A1_common_v2.0",
        "description": (
            "RFI / clarification sketch sheet — A3 landscape (420 × 297 mm). Two-pane layout: "
            "left half is a legend / schedule for the QUERY text, right half is a SKETCH slot for "
            "annotated plan or detail. 25 mm header carries 'RFI / CLARIFICATION' + RFI REF. "
            "30 mm footer carries PROJECT / DATE / REV."
        ),
        "saveAs": "Families/TitleBlocks/STING_TB_CLARIFICATION_A3_v1.0.rfa",
        "templateRft": "Annotations/Titleblocks/A3 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "A3"},
            {"name": "PRJ_TB_VARIANT_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "CLARIFICATION"},
            {"name": "STING_SHEET_BIM_MODE_TXT", "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
        ],
        "lines": lines,
        "staticText": static,
        "labels": labels,
        "filledRegions": [],
        "slots": slots,
    }


# ─── Main ───────────────────────────────────────────────────────────────

def main():
    with open(SPEC, "r", encoding="utf-8") as f:
        lib = json.load(f)

    # Working sheets — replace all 8 size+orientation commons + all 16
    # concrete BIM/NONBIM (everything except A1 LAND BIM/NONBIM and the
    # master A1_common which are hand-tuned).
    polished_count = 0
    skip_ids = {"A1_common_v2.0", "STING_TB_A1_BIM_v2.0", "STING_TB_A1_NONBIM_v2.0"}

    for size in ["A0", "A1", "A2", "A3", "A4"]:
        for orientation in ["LAND", "PORT"]:
            common = polish_working_common(size, orientation)
            common_id = common["id"]
            if common_id not in skip_ids:
                upsert(lib, common)
                polished_count += 1
            for mode in ["BIM", "NONBIM"]:
                concrete = polish_concrete(size, orientation, mode, common_id)
                if concrete["id"] not in skip_ids:
                    upsert(lib, concrete)
                    polished_count += 1

    # Specialty
    specialty_polished = [
        polish_assembly("STING_TB_ASSEMBLY_PIPE_v1.0",   "PIPE",   "#0076A8"),
        polish_assembly("STING_TB_ASSEMBLY_DUCT_v1.0",   "DUCT",   "#F2A341"),
        polish_assembly("STING_TB_ASSEMBLY_COND_v1.0",   "COND",   "#7B3F00"),
        polish_assembly("STING_TB_ASSEMBLY_HANGER_v1.0", "HANGER", "#5A5A5A"),
        polish_present("STING_TB_PRESENT_A1_v1.0",      mono=False),
        polish_present("STING_TB_PRESENT_A1_MONO_v1.0", mono=True),
        polish_cover(),
        polish_divider(),
        polish_register(),
        polish_transmittal(),
        polish_submission("KCCA", "#006937",
                          "KAMPALA CAPITAL CITY AUTHORITY",
                          "Submitted under the Building Control Act 2013 (Uganda) — Physical Planning Act, 2010"),
        polish_submission("ERA",  "#1F4E79",
                          "ELECTRICITY REGULATORY AUTHORITY",
                          "Submitted under the Electricity Act, Cap 145 — installation compliance per the Electricity (Installation Permits) Regulations 2003"),
        polish_submission("NEMA", "#7B3F00",
                          "NATIONAL ENVIRONMENT MANAGEMENT AUTHORITY",
                          "Submitted under the National Environment Act 2019 — environmental impact assessment per the EIA Regulations 2020"),
        polish_clarification(),
    ]
    for fam in specialty_polished:
        upsert(lib, fam)
        polished_count += 1

    lib["lastUpdated"] = "2026-05-01"

    with open(SPEC, "w", encoding="utf-8") as f:
        json.dump(lib, f, indent=2, ensure_ascii=False)
    print(f"Polished {polished_count} families.")
    print(f"Total in catalogue: {len(lib['families'])}")


if __name__ == "__main__":
    main()
