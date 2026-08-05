#!/usr/bin/env python3
"""
STING Title-Block Catalogue Expander
====================================

One-off generator that takes the existing STING_TITLE_BLOCKS.json (which
ships A1 LAND BIM/NONBIM with full layouts) and expands it to the full
kit of ~50 families:

  Working sheets — 5 sizes × 2 orientations × 2 modes = 20 concrete
                   plus 10 abstract size+orientation commons

  Specialty      — 4 fabrication (PIPE/DUCT/COND/HANGER)
                   2 presentation (color + mono)
                   1 cover, 1 divider, 1 register
                   1 transmittal cover, 3 submission, 1 clarification
                   = 13 concrete + 1 abstract specialty common

The existing A1 LAND entries are preserved verbatim — this script ADDS
entries; it does not modify existing ones.

Run from repo root:
    python3 tools/expand_title_block_catalogue.py
    python3 tools/generate_title_block_previews.py    # then re-render
"""

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SPEC = REPO / "StingTools" / "Data" / "STING_TITLE_BLOCKS.json"

# --- Sheet sizes (mm, paper W x H, landscape orientation) ---
SIZES = {
    "A0": (1189, 841),
    "A1": (841,  594),
    "A2": (594,  420),
    "A3": (420,  297),
    "A4": (297,  210),
}

# Strip height per size (mm) — bottom-strip layout, scales with paper.
STRIP_H = {
    "A0": 130, "A1": 110, "A2": 90, "A3": 70, "A4": 60,
}


def make_size_common(size: str, orientation: str) -> dict:
    """Generate an abstract common for a size+orientation. Bottom-strip layout
    parametrised by paper dimensions. Inherits the parameter universe by
    extending the master A1_common_v2.0 (which carries the full 40-param
    Group A + Group C identity universe). The size-specific common adds the
    geometry only."""
    is_port = orientation == "PORT"
    if is_port:
        w, h = SIZES[size][1], SIZES[size][0]   # swap
    else:
        w, h = SIZES[size]
    sh = STRIP_H[size]
    fam_id = f"{size}_{'PORT' if is_port else 'LAND'}_common_v2.0"
    save_safe_id = fam_id.replace("/", "_")
    desc = (f"{size} {'portrait' if is_port else 'landscape'} working sheet "
            f"({w} × {h} mm) — common base shared between BIM and NONBIM. "
            f"Inherits the full identity-data parameter universe from "
            f"A1_common_v2.0 and adds geometry sized for this paper.")

    # Geometry: outer border + bottom-strip rule + 5-cell strip dividers.
    # Cell layout (proportions match the A1 baseline scaled to this paper):
    #   col 1: identity (CLIENT / PROJECT / consultants / contractor) - 24% w
    #   col 2: notes / project ref / discipline                       - 24% w
    #   col 3: drawing title                                          - 26% w
    #   col 4: dates / drawn / checked / approved                     - 18% w
    #   col 5: sheet number block (BIM-only — variants override)      - 8% w
    c1 = round(w * 0.24)
    c2 = round(w * 0.48)
    c3 = round(w * 0.74)
    c4 = round(w * 0.92)

    lines = [
        # Outer border
        {"from": [0, 0],     "to": [w, 0],     "style": "Wide Lines"},
        {"from": [w, 0],     "to": [w, h],     "style": "Wide Lines"},
        {"from": [w, h],     "to": [0, h],     "style": "Wide Lines"},
        {"from": [0, h],     "to": [0, 0],     "style": "Wide Lines"},
        # Strip top edge
        {"from": [0, sh],    "to": [w, sh],    "style": "Medium Lines"},
        # Vertical dividers
        {"from": [c1, 0],    "to": [c1, sh],   "style": "Thin Lines"},
        {"from": [c2, 0],    "to": [c2, sh],   "style": "Thin Lines"},
        {"from": [c3, 0],    "to": [c3, sh],   "style": "Thin Lines"},
        {"from": [c4, 0],    "to": [c4, sh],   "style": "Thin Lines"},
    ]
    # Horizontal cell dividers within col 1 (5 rows: client, project, arch,
    # struct, mep, contractor).
    row_h_c1 = sh / 5
    for i in range(1, 5):
        y = round(i * row_h_c1, 1)
        lines.append({"from": [0, y], "to": [c1, y], "style": "Thin Lines"})

    # Static text labels — column headers
    static_text = [
        {"text": "CLIENT",      "anchor": [4, sh - 4], "size": 1.4},
        {"text": "PROJECT",     "anchor": [4, sh - row_h_c1*1 - 4], "size": 1.2},
        {"text": "ARCHITECT",   "anchor": [4, sh - row_h_c1*2 - 4], "size": 1.2},
        {"text": "STRUCTURAL",  "anchor": [4, sh - row_h_c1*3 - 4], "size": 1.2},
        {"text": "MEP",         "anchor": [4, sh - row_h_c1*4 - 4], "size": 1.2},
        {"text": "CONTRACTOR",  "anchor": [4, 2],                   "size": 1.2},

        {"text": "NOTES",         "anchor": [c1+4, sh-4],   "size": 1.4},
        {"text": "DRAWING TITLE", "anchor": [c2+4, sh-4],   "size": 1.4},

        {"text": "DATE",        "anchor": [c3+4, sh-4],   "size": 1.2},
        {"text": "DRAWN BY",    "anchor": [c3+4, sh-15],  "size": 1.0},
        {"text": "CHECKED BY",  "anchor": [c3+4, sh-25],  "size": 1.0},
        {"text": "APPROVED BY", "anchor": [c3+4, sh-35],  "size": 1.0},

        {"text": "SHEET",       "anchor": [c4+2, sh-4],   "size": 1.2},
    ]

    # Labels — bind to existing PRJ_TB_* / PRJ_ORG_* params per the A1 baseline
    labels = [
        {"param": "PRJ_TB_CLIENT_NAME_TXT",     "anchor": [4, sh-12], "size": 1.8},
        {"param": "PRJ_TB_CLIENT_ADDRESS_TXT",  "anchor": [4, sh-18], "size": 1.0},
        {"param": "PRJ_ORG_PROJECT_NAME_TXT",   "anchor": [4, sh-row_h_c1*1-12], "size": 1.6},
        {"param": "PRJ_ORG_PROJECT_ADDRESS_TXT","anchor": [4, sh-row_h_c1*1-18], "size": 0.9},

        {"param": "PRJ_TB_CONSULTANT_NAME_TXT",          "anchor": [c1*0.45, sh-row_h_c1*2-6], "size": 1.2},
        {"param": "PRJ_TB_CONSULTANT_ADDRESS_TXT",       "anchor": [c1*0.45, sh-row_h_c1*2-12], "size": 0.8},
        {"param": "PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT",     "anchor": [c1*0.45, sh-row_h_c1*3-6], "size": 1.2},
        {"param": "PRJ_TB_STRUCTURAL_CONSULTANTS_ADDRESS_TXT",  "anchor": [c1*0.45, sh-row_h_c1*3-12], "size": 0.8},
        {"param": "PRJ_TB_MEP_CONSULTANTS_NAME_TXT",     "anchor": [c1*0.45, sh-row_h_c1*4-6], "size": 1.2},
        {"param": "PRJ_TB_MEP_CONSULTANTS_ADDRESS_TXT",  "anchor": [c1*0.45, sh-row_h_c1*4-12], "size": 0.8},
        {"param": "PRJ_TB_CONTRACTOR_NAME_TXT",          "anchor": [c1*0.45, 8], "size": 1.2},
        {"param": "PRJ_TB_CONTRACTOR_ADDRESS_TXT",       "anchor": [c1*0.45, 2], "size": 0.8},

        {"param": "PRJ_TB_NOTES_LEGEND_REF_TXT", "anchor": [c1+4, sh-12], "size": 1.2},

        {"param": "PRJ_ORG_PROJECT_NAME_TXT",    "anchor": [c2+4, sh-12], "size": 2.5},
        {"param": "PRJ_TB_DISCIPLINE_TXT",       "anchor": [c2+4, sh-22], "size": 1.4},
        {"param": "PRJ_ORG_RIBA_STAGE_TXT",      "anchor": [c2+4, sh-32], "size": 1.2},

        {"param": "PRJ_TB_DATE_DRAWN_TXT",       "anchor": [c3+4, sh-10], "size": 1.2},
        {"param": "PRJ_TB_DRAWN_BY_TXT",         "anchor": [c3+4, sh-21], "size": 1.0},
        {"param": "PRJ_TB_CHECKED_BY_TXT",       "anchor": [c3+4, sh-31], "size": 1.0},
        {"param": "PRJ_TB_APVD_BY_TXT",          "anchor": [c3+4, sh-41], "size": 1.0},

        {"param": "PRJ_TB_SCALE_OVERRIDE_TXT",   "anchor": [c3+4, sh-50], "size": 1.0},
    ]

    # Slots — full-bleed main area + half-split + quad + key-plan pocket
    drawable_y = sh + 5
    drawable_h = h - sh - 5
    slot_main  = {"id": "S01", "anchor": [10, drawable_y], "size": [w-20, drawable_h-5],
                  "purposeTag": "main-plan", "viewportType": "Title w/ Line", "scaleHint": 100,
                  "description": f"Main drawing area — full-bleed plan / 3D / section ({size} {'portrait' if is_port else 'landscape'})",
                  "createReferencePlanes": True, "showCornerMarker": True}
    half_w = (w - 30) // 2
    slot_half_l = {"id": "S02", "anchor": [10, drawable_y], "size": [half_w, drawable_h-5],
                   "purposeTag": "main-plan-half-left", "viewportType": "Title w/ Line", "scaleHint": 100,
                   "description": "Left half — 50/50 split layout",
                   "createReferencePlanes": True, "showCornerMarker": True}
    slot_half_r = {"id": "S03", "anchor": [20+half_w, drawable_y], "size": [half_w, drawable_h-5],
                   "purposeTag": "main-plan-half-right", "viewportType": "Title w/ Line", "scaleHint": 100,
                   "description": "Right half — 50/50 split layout",
                   "createReferencePlanes": True, "showCornerMarker": True}
    quad_w = (w - 30) // 2
    quad_h = (drawable_h - 15) // 2
    slot_q_bl = {"id": "S04", "anchor": [10, drawable_y], "size": [quad_w, quad_h],
                 "purposeTag": "quad-bottom-left", "scaleHint": 200,
                 "description": "Bottom-left quadrant — 4-up grid",
                 "createReferencePlanes": False, "showCornerMarker": True}
    slot_q_br = {"id": "S05", "anchor": [20+quad_w, drawable_y], "size": [quad_w, quad_h],
                 "purposeTag": "quad-bottom-right", "scaleHint": 200,
                 "description": "Bottom-right quadrant — 4-up grid",
                 "createReferencePlanes": False, "showCornerMarker": True}
    slot_q_tl = {"id": "S06", "anchor": [10, drawable_y+quad_h+5], "size": [quad_w, quad_h],
                 "purposeTag": "quad-top-left", "scaleHint": 200,
                 "description": "Top-left quadrant — 4-up grid",
                 "createReferencePlanes": False, "showCornerMarker": True}
    slot_q_tr = {"id": "S07", "anchor": [20+quad_w, drawable_y+quad_h+5], "size": [quad_w, quad_h],
                 "purposeTag": "quad-top-right", "scaleHint": 200,
                 "description": "Top-right quadrant — 4-up grid",
                 "createReferencePlanes": False, "showCornerMarker": True}
    kp_w, kp_h = round(w*0.15), round(drawable_h*0.18)
    slot_kp = {"id": "KP", "anchor": [w-kp_w-15, drawable_y+5], "size": [kp_w, kp_h],
               "purposeTag": "key-plan", "scaleHint": 500,
               "description": "Key-plan pocket — small location overview",
               "createReferencePlanes": False, "showCornerMarker": True}

    return {
        "id": fam_id,
        "abstract": True,
        "extends": "A1_common_v2.0",
        "description": desc,
        "templateRft": f"Annotations/Titleblocks/{size} metric{'_PORT' if is_port else ''}.rft" if not is_port else f"Annotations/Titleblocks/{size} metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            # Override paper-size default to match this orientation
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True,
             "group": "IdentityData", "default": f"{size}{'P' if is_port else ''}"},
        ],
        "lines": lines,
        "staticText": static_text,
        "labels": labels,
        "filledRegions": [],
        "slots": [slot_main, slot_half_l, slot_half_r, slot_q_bl, slot_q_br,
                  slot_q_tl, slot_q_tr, slot_kp],
    }


def make_concrete(size: str, orientation: str, mode: str, common_id: str) -> dict:
    """Generate a concrete BIM or NONBIM family that extends the size+orientation common."""
    suffix = "_PORT_" if orientation == "PORT" else "_"
    fam_id = f"STING_TB_{size}{suffix}{mode}_v2.0"
    desc_o = "portrait" if orientation == "PORT" else "landscape"
    is_bim = mode == "BIM"

    if is_bim:
        # BIM: add 7-segment ID, suitability chip, status band, revision strip,
        # AUTHORISED BY signature row.
        if orientation == "PORT":
            paper_w = SIZES[size][1]
        else:
            paper_w = SIZES[size][0]
        sh = STRIP_H[size]
        # Sheet ID strip across the top of the bottom strip (between sh and
        # sh + 30 mm — only present on BIM variants)
        bim_strip_y0 = sh
        bim_strip_y1 = sh + 30

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

            {"name": "PRJ_DWG_SUITABILITY_COD_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "S2"},
            {"name": "STING_SUITABILITY_DESC_TXT",   "kind": "shared", "instance": True, "group": "IdentityData", "default": "Shared, Non-contractual"},
            {"name": "PRJ_DWG_ISSUE_PURPOSE_TXT",    "kind": "shared", "instance": True, "group": "IdentityData", "default": "FOR INFORMATION"},
            {"name": "PRJ_TB_DELIVERABLE_STATUS_TXT","kind": "shared", "instance": True, "group": "IdentityData", "default": "Shared"},
            {"name": "PRJ_TB_REVISION_NR_TXT",       "kind": "shared", "instance": True, "group": "IdentityData", "default": "P01"},
            {"name": "PRJ_TB_REVISION_DATE_TXT",     "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_REVISION_DESCRIPTION_TXT","kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_LAST_TRANSMITTAL_TXT",  "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "PRJ_TB_DELIVERABLE_CDE_TXT",   "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "STING_FEDERATION_STATUS_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "Federated"},
            {"name": "STING_LOIN_LOD_TXT",           "kind": "shared", "instance": True, "group": "IdentityData", "default": "LOD 300"},
            {"name": "STING_AUTHORISED_BY_TXT",      "kind": "shared", "instance": True, "group": "IdentityData"},
            {"name": "STING_AUTHORISED_DATE_TXT",    "kind": "shared", "instance": True, "group": "IdentityData"},
        ]
        lines = [
            {"from": [0,            bim_strip_y1], "to": [paper_w, bim_strip_y1], "style": "Medium Lines"},
            {"from": [0,            bim_strip_y0+15], "to": [paper_w, bim_strip_y0+15], "style": "Thin Lines"},
        ]
        # 7-segment ID dividers — 7 cells across full width
        seg_w = paper_w / 7
        for i in range(1, 7):
            x = round(i*seg_w, 1)
            lines.append({"from": [x, bim_strip_y0], "to": [x, bim_strip_y0+15], "style": "Thin Lines"})

        static_text = [
            {"text": "STATUS",      "anchor": [4, bim_strip_y1-3], "size": 1.0},
            {"text": "SUITABILITY", "anchor": [paper_w*0.25, bim_strip_y1-3], "size": 1.0},
            {"text": "REV",         "anchor": [paper_w*0.5,  bim_strip_y1-3], "size": 1.0},
            {"text": "REV DATE",    "anchor": [paper_w*0.65, bim_strip_y1-3], "size": 1.0},
            {"text": "LOIN/LOD",    "anchor": [paper_w*0.78, bim_strip_y1-3], "size": 1.0},
            {"text": "FED",         "anchor": [paper_w*0.92, bim_strip_y1-3], "size": 1.0},
            {"text": "ISO 19650 SHEET ID — 7 SEGMENTS", "anchor": [4, bim_strip_y0+10], "size": 0.9},
        ]
        # Label cells in the BIM strip
        labels = [
            {"param": "PRJ_TB_DELIVERABLE_STATUS_TXT", "anchor": [4,                bim_strip_y1-15], "size": 1.6},
            {"param": "PRJ_DWG_SUITABILITY_COD_TXT",   "anchor": [paper_w*0.25,     bim_strip_y1-15], "size": 1.6},
            {"param": "PRJ_TB_REVISION_NR_TXT",        "anchor": [paper_w*0.5,      bim_strip_y1-15], "size": 1.6},
            {"param": "PRJ_TB_REVISION_DATE_TXT",      "anchor": [paper_w*0.65,     bim_strip_y1-15], "size": 1.4},
            {"param": "STING_LOIN_LOD_TXT",            "anchor": [paper_w*0.78,     bim_strip_y1-15], "size": 1.2},
            {"param": "STING_FEDERATION_STATUS_TXT",   "anchor": [paper_w*0.92,     bim_strip_y1-15], "size": 1.2},
            # 7-segment cells
            {"param": "STING_SHEET_PROJECT_TXT",       "anchor": [seg_w*0+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_ORIG_TXT",          "anchor": [seg_w*1+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_VOLUME_TXT",        "anchor": [seg_w*2+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_LEVEL_TXT",         "anchor": [seg_w*3+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_TYPE_TXT",          "anchor": [seg_w*4+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_ROLE_TXT",          "anchor": [seg_w*5+4, bim_strip_y0+5], "size": 1.4},
            {"param": "STING_SHEET_SEQ_TXT",           "anchor": [seg_w*6+4, bim_strip_y0+5], "size": 1.4},
        ]
        filled_regions = [
            # Suitability chip
            {"topLeft": [paper_w*0.245, bim_strip_y1-2], "bottomRight": [paper_w*0.345, bim_strip_y1-12],
             "fillTypeName": "Solid fill", "color": "#F2A341"},
            # Status band along top of BIM strip
            {"topLeft": [0, bim_strip_y1], "bottomRight": [paper_w, bim_strip_y1-3],
             "fillTypeName": "Solid fill", "color": "#1F4E79"},
        ]
    else:
        # NONBIM: just sheet number cell + revision in the rightmost column.
        params = [
            {"name": "STING_SHEET_BIM_MODE_TXT",  "kind": "shared", "instance": True, "group": "IdentityData", "default": "NONBIM"},
            {"name": "PRJ_TB_SHEET_NR_TXT",       "kind": "shared", "instance": True, "group": "IdentityData", "default": "A-001"},
            {"name": "PRJ_TB_REVISION_NR_TXT",    "kind": "shared", "instance": True, "group": "IdentityData", "default": "0"},
            {"name": "PRJ_TB_REVISION_DATE_TXT",  "kind": "shared", "instance": True, "group": "IdentityData"},
        ]
        lines = []
        static_text = []
        labels = [
            # The sheet number cell is in column 5 of the parent (c4 area).
            # Don't override coordinates — labels concat with parent.
        ]
        filled_regions = []

    return {
        "id": fam_id,
        "extends": common_id,
        "mode": mode,
        "description": (f"{size} {desc_o} working sheet — "
                        f"{'full ISO 19650 BIM identity (7-segment ID + suitability chip + revision-history strip + AUTHORISED BY)' if is_bim else 'minimal non-BIM identity (sheet number + revision)'} — extends {common_id}."),
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "parameters": params,
        "lines": lines,
        "staticText": static_text,
        "labels": labels,
        "filledRegions": filled_regions,
    }


SPECIALTY = [
    # id, paper_w, paper_h, description, save_basename, mode, extends_or_None
    ("STING_TB_ASSEMBLY_PIPE_v1.0",   841, 594,
     "Pipe spool fabrication title block — A1 landscape, BOM strip on the right, fabrication metadata (FAB_LOC, weight, BOM rev) in the bottom strip.",
     None),
    ("STING_TB_ASSEMBLY_DUCT_v1.0",   841, 594,
     "Duct spool fabrication title block — A1 landscape, gauge / insulation / connection-type strip.",
     None),
    ("STING_TB_ASSEMBLY_COND_v1.0",   841, 594,
     "Conduit / cable assembly fabrication title block — A1 landscape, cable schedule strip.",
     None),
    ("STING_TB_ASSEMBLY_HANGER_v1.0", 841, 594,
     "Hanger / support fabrication title block — A1 landscape, load / fixing schedule.",
     None),
    ("STING_TB_PRESENT_A1_v1.0",      841, 594,
     "Presentation title block — full-bleed render area, minimal corner watermark, colour-rich.",
     None),
    ("STING_TB_PRESENT_A1_MONO_v1.0", 841, 594,
     "Presentation title block (mono variant) — full-bleed render area, monochrome corner watermark.",
     None),
    ("STING_TB_COVER_A1_v1.0",        841, 594,
     "Project / package cover page — large logo, project banner, deliverable code, no drawable zone.",
     None),
    ("STING_TB_DIVIDER_A1_v1.0",      841, 594,
     "Discipline section divider — single big discipline label across centre, minimal identity strip at bottom.",
     None),
    ("STING_TB_REGISTER_A1_v1.0",     841, 594,
     "Drawing register sheet — full-page schedule of every sheet in the deliverable.",
     None),
    ("STING_TB_TRANSMITTAL_A4_v1.0",  297, 210,
     "Transmittal cover sheet — A4 landscape, recipient list + accompanying sheets list + signatures.",
     None),
    ("STING_TB_SUBMISSION_KCCA_v1.0", 841, 594,
     "KCCA (Kampala Capital City Authority) submission — statutory cells per KCCA building-permit requirements.",
     None),
    ("STING_TB_SUBMISSION_ERA_v1.0",  841, 594,
     "ERA (Electricity Regulatory Authority) submission — utility connection / electrical compliance cells.",
     None),
    ("STING_TB_SUBMISSION_NEMA_v1.0", 841, 594,
     "NEMA (National Environment Management Authority) submission — environmental compliance cells.",
     None),
    ("STING_TB_CLARIFICATION_A3_v1.0", 420, 297,
     "RFI / clarification sketch sheet — A3 landscape, query + sketch space + revision strip.",
     None),
]


def make_specialty(fam_id: str, w: int, h: int, desc: str) -> dict:
    """Generate a specialty title block with a minimal identity strip and the
    full common parameter universe via extends. The detailed layout for each
    specialty is left to a later phase; this stub gives the factory a usable
    .rfa shell with all the right shared parameters bound."""
    return {
        "id": fam_id,
        "extends": "A1_common_v2.0",
        "description": f"{desc} (Phase 172 layout — currently a stub that mints a valid .rfa with the full common parameter universe bound; layout polish per the design doc deferred.)",
        "saveAs": f"Families/TitleBlocks/{fam_id}.rfa",
        "templateRft": "Annotations/Titleblocks/A1 metric.rft" if h < w else "Annotations/Titleblocks/A1 metric.rft",
        "category": "OST_TitleBlocks",
        "parameters": [
            {"name": "PRJ_TB_PAPER_SZ_TXT", "kind": "shared", "instance": True,
             "group": "IdentityData", "default": ("A4" if (w == 297 and h == 210) or (w == 210 and h == 297) else
                                                  "A3" if (w == 420 and h == 297) or (w == 297 and h == 420) else "A1")},
            {"name": "PRJ_TB_VARIANT_TXT", "kind": "shared", "instance": True,
             "group": "IdentityData", "default": fam_id.split("_")[2] if len(fam_id.split("_")) > 2 else "WORKING"},
        ],
        "lines": [
            {"from": [0, 0],   "to": [w, 0],   "style": "Wide Lines"},
            {"from": [w, 0],   "to": [w, h],   "style": "Wide Lines"},
            {"from": [w, h],   "to": [0, h],   "style": "Wide Lines"},
            {"from": [0, h],   "to": [0, 0],   "style": "Wide Lines"},
            {"from": [0, 25],  "to": [w, 25],  "style": "Medium Lines"},
        ],
        "staticText": [
            {"text": fam_id.replace("STING_TB_", "").replace("_v1.0", ""),
             "anchor": [w/2, 12], "size": 2.0, "hAlign": "Center"},
        ],
        "labels": [
            {"param": "PRJ_ORG_PROJECT_NAME_TXT",   "anchor": [4, 18], "size": 1.4},
            {"param": "PRJ_TB_LAST_TRANSMITTAL_TXT","anchor": [w*0.7, 18], "size": 1.2},
        ],
        "filledRegions": [],
        "slots": [
            {"id": "S01", "anchor": [10, 30], "size": [w-20, h-40],
             "purposeTag": "main-plan", "scaleHint": 100,
             "description": "Main content area for this specialty title block",
             "createReferencePlanes": True, "showCornerMarker": True},
        ],
    }


def main():
    with open(SPEC, "r", encoding="utf-8") as f:
        lib = json.load(f)
    existing_ids = {f["id"] for f in lib["families"]}

    new_entries = []

    # Working sheets: 5 sizes × 2 orientations × (common + 2 modes)
    for size in ["A0", "A1", "A2", "A3", "A4"]:
        for orientation in ["LAND", "PORT"]:
            common = make_size_common(size, orientation)
            common_id = common["id"]
            if common_id not in existing_ids:
                new_entries.append(common)
                existing_ids.add(common_id)
            for mode in ["BIM", "NONBIM"]:
                concrete = make_concrete(size, orientation, mode, common_id)
                if concrete["id"] not in existing_ids:
                    new_entries.append(concrete)
                    existing_ids.add(concrete["id"])

    # Specialty
    for fam_id, w, h, desc, _ in SPECIALTY:
        if fam_id not in existing_ids:
            new_entries.append(make_specialty(fam_id, w, h, desc))
            existing_ids.add(fam_id)

    # Append to library
    lib["families"].extend(new_entries)
    lib["lastUpdated"] = "2026-05-01"
    lib["description"] = (
        "Generator spec for STING title-block .rfa families. Two-family BIM "
        "architecture: each working-sheet paper size ships in BOTH landscape "
        "and portrait orientations × BIM/NONBIM modes (5 × 2 × 2 = 20 working "
        "sheets), plus 14 specialty title blocks (4 fabrication assembly + "
        "2 presentation + cover + divider + register + transmittal cover + "
        "3 submission + clarification). Every concrete family extends a "
        "size+orientation abstract common which itself extends "
        "A1_common_v2.0 — the master identity-data parameter universe. "
        "Parameter set follows TITLE_BLOCK_FAMILY_DESIGN.md §2.1 — all "
        "shared params sourced from MR_PARAMETERS.txt, no fabricated GUIDs."
    )

    with open(SPEC, "w", encoding="utf-8") as f:
        json.dump(lib, f, indent=2, ensure_ascii=False)

    print(f"Added {len(new_entries)} new families. Total: {len(lib['families'])}.")
    by_kind = {"abstract": 0, "BIM": 0, "NONBIM": 0, "specialty": 0}
    for fam in lib["families"]:
        if fam.get("abstract"): by_kind["abstract"] += 1
        elif fam.get("mode") == "BIM": by_kind["BIM"] += 1
        elif fam.get("mode") == "NONBIM": by_kind["NONBIM"] += 1
        else: by_kind["specialty"] += 1
    for k, v in by_kind.items():
        print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
