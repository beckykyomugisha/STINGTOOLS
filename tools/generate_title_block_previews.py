#!/usr/bin/env python3
"""
STING Title-Block Preview Generator
====================================

Reads StingTools/Data/STING_TITLE_BLOCKS.json and emits one SVG preview
per family into docs/title_blocks/previews/.

Each SVG shows the family at 1 SVG-px = 1 mm, with:
  - Sheet outline (paper bounds)
  - All declared lines with weight-coded stroke
  - Static text labels (rendered as actual text)
  - Parameter labels (placeholder text shows {param-name})
  - Filled regions (with declared colour)
  - Viewport slots (dashed bbox + slot id at top-left)
  - Watermark with family id + mode + paper size

Outputs are vanilla SVG (no dependencies). Open in any browser, export
to PDF/PNG via browser Print or rsvg-convert if needed.

Run from repo root:
    python3 tools/generate_title_block_previews.py

Or for a single family:
    python3 tools/generate_title_block_previews.py STING_TB_A1_BIM_v2.0
"""

import json
import os
import sys
import re
import html
from pathlib import Path

REPO  = Path(__file__).resolve().parent.parent
SPEC  = REPO / "StingTools" / "Data" / "STING_TITLE_BLOCKS.json"
OUT   = REPO / "docs" / "title_blocks" / "previews"

# Paper size lookup — width x height in mm, default to A1 landscape if unknown.
# Detection happens via family id (A0/A1/A2/A3/A4 + optional _PORT).
PAPER_BY_BASENAME = {
    "A0": (1189, 841),   "A0_PORT": (841, 1189),
    "A1": (841,  594),   "A1_PORT": (594, 841),
    "A2": (594,  420),   "A2_PORT": (420, 594),
    "A3": (420,  297),   "A3_PORT": (297, 420),
    "A4": (297,  210),   "A4_PORT": (210, 297),
}


def detect_paper(fam_id: str, fam: dict) -> tuple:
    """Return (width_mm, height_mm) inferred from id / template / spec."""
    # Explicit declaration via PRJ_TB_PAPER_SZ_TXT default takes precedence.
    for p in fam.get("parameters", []):
        if p.get("name") == "PRJ_TB_PAPER_SZ_TXT" and p.get("default"):
            sz = p["default"].upper().strip()
            if sz in PAPER_BY_BASENAME:
                return PAPER_BY_BASENAME[sz]
    # Match by suffix on the id.
    upper = fam_id.upper()
    for size in ["A0", "A1", "A2", "A3", "A4"]:
        if f"_{size}_PORT" in upper or upper.endswith(f"_{size}_PORT_COMMON_V2.0") or upper.endswith(f"{size}_PORTRAIT") or upper.endswith(f"{size}_PORT"):
            return PAPER_BY_BASENAME[f"{size}_PORT"]
        if f"_{size}_" in upper or upper.endswith(f"_{size}_COMMON_V2.0") or upper.endswith(size) or upper.endswith(f"_{size}_LAND_COMMON_V2.0"):
            return PAPER_BY_BASENAME[size]
    return PAPER_BY_BASENAME["A1"]


def resolve_extends(lib: dict, fam: dict) -> dict:
    """Walk the extends chain, deep-merge parents oldest→newest, child on top."""
    if not fam.get("extends"):
        return fam
    visited = set()
    chain = []
    cur = fam
    while cur and cur.get("extends"):
        if cur.get("id") in visited:
            break
        visited.add(cur.get("id"))
        parent = next((f for f in lib["families"] if f["id"].lower() == cur["extends"].lower()), None)
        if not parent:
            break
        chain.append(parent)
        cur = parent
    chain.reverse()
    merged = {
        "id":          fam.get("id"),
        "description": fam.get("description"),
        "mode":        fam.get("mode"),
        "templateRft": fam.get("templateRft"),
        "category":    fam.get("category"),
        "parameters":  [],
        "lines":       [],
        "staticText":  [],
        "labels":      [],
        "filledRegions": [],
        "slots":       [],
    }
    for src in chain + [fam]:
        for k in ("templateRft", "category", "mode"):
            if not merged.get(k) and src.get(k):
                merged[k] = src[k]
        # parameters merge by name (child wins)
        params = {p["name"].lower(): p for p in merged.get("parameters", []) if p.get("name")}
        for p in src.get("parameters", []) or []:
            if p.get("name"):
                params[p["name"].lower()] = p
        merged["parameters"] = list(params.values())
        # slots merge by id
        slots = {s["id"].lower(): s for s in merged.get("slots", []) if s.get("id")}
        for s in src.get("slots", []) or []:
            if s.get("id"):
                slots[s["id"].lower()] = s
        merged["slots"] = list(slots.values())
        # everything else concatenates
        for k in ("lines", "staticText", "labels", "filledRegions"):
            merged[k] = (merged.get(k) or []) + (src.get(k) or [])
    return merged


def stroke_for_style(style: str) -> tuple:
    """(stroke_width_mm, colour) per BS 1192 Annex A."""
    s = (style or "").lower()
    if "wide"   in s: return (0.7, "#000")
    if "medium" in s: return (0.35, "#222")
    if "thin"   in s: return (0.18, "#444")
    return (0.25, "#666")


# Slot colour palette — keyed by purposeTag prefix. The renderer falls
# through to a default if no match. Categories also drive stroke style
# (primary = solid, auxiliary = dashed, symbol = dotted, overlay = dot-dash).
PURPOSE_PALETTE = {
    # Primary drawable
    "main-plan":              "#1F4E79",
    "main-plan-half":         "#5fa8d3",
    "quad":                   "#5fa8d3",
    # Auxiliary content
    "key-plan":               "#4CAF50",
    "aerial-key":             "#8BC34A",
    "notes":                  "#607D8B",
    "discipline-legend":      "#FF9800",
    "material-legend":        "#FFB74D",
    "fire-legend":            "#E53935",
    "accessibility-legend":   "#7B1FA2",
    "falls-legend":           "#0288D1",
    "lighting-legend":        "#FFC107",
    "schedule":               "#9C27B0",
    "bom":                    "#9C27B0",
    "revision-history":       "#F44336",
    "caption":                "#795548",
    # Symbol slots
    "north-arrow":            "#009688",
    "scale-bar":              "#009688",
    "qr-code":                "#37474F",
    "discipline-band":        "#FF9800",
    # Specialty
    "presentation-render":    "#FFEB3B",
    "presentation-perspective":"#FBC02D",
    "fabrication-isometric":  "#E91E63",
    "cut-list":               "#AD1457",
    "spool-refs":             "#880E4F",
    "rfi-query":              "#FF5722",
    "rfi-sketch":             "#00BCD4",
    "markup-plan":            "#00BCD4",
}

CATEGORY_DASH = {
    "primary":   "",
    "auxiliary": "3,2",
    "symbol":    "1,1.5",
    "overlay":   "5,1.5,1.5,1.5",
}

CATEGORY_OPACITY = {
    "primary":   "1.0",
    "auxiliary": "0.85",
    "symbol":    "0.95",
    "overlay":   "0.65",
}


def slot_colour(slot: dict) -> str:
    """Resolve preview colour for a slot — explicit override beats palette
    lookup; falls back to neutral grey if nothing matches."""
    if slot.get("previewColor"):
        return slot["previewColor"]
    tag = (slot.get("purposeTag") or "").lower()
    if not tag:
        return "#888"
    # Try longest-prefix match
    for key in sorted(PURPOSE_PALETTE.keys(), key=len, reverse=True):
        if tag.startswith(key):
            return PURPOSE_PALETTE[key]
    return "#888"


def safe_xml(s: str) -> str:
    return html.escape(s or "", quote=True)


def render_svg(fam: dict, paper_w: float, paper_h: float, lib: dict) -> str:
    """Return full SVG document string for the family."""
    eff = resolve_extends(lib, fam)
    fam_id = fam["id"]
    mode   = fam.get("mode") or eff.get("mode") or "—"
    desc   = fam.get("description") or eff.get("description") or ""
    is_abstract = fam.get("abstract", False)

    # Generate SVG with the sheet bottom-left at SVG (0, 0) by flipping Y in
    # the transform — the spec uses "y goes up" (Revit family convention),
    # SVG uses "y goes down".
    margin = 30      # mm
    title_h = 50     # mm reserved at top of canvas for the title bar
    canvas_w = paper_w + 2 * margin
    canvas_h = paper_h + 2 * margin + title_h

    out = []
    out.append(f'<?xml version="1.0" encoding="UTF-8"?>')
    out.append(f'<svg xmlns="http://www.w3.org/2000/svg" '
               f'viewBox="0 0 {canvas_w} {canvas_h}" '
               f'width="{canvas_w}mm" height="{canvas_h}mm" '
               f'style="background:#fafafa;font-family:Helvetica,Arial,sans-serif;">')
    # Watermark / title bar
    out.append(f'<rect x="0" y="0" width="{canvas_w}" height="{title_h}" fill="#1F4E79"/>')
    out.append(f'<text x="{canvas_w/2}" y="{title_h*0.45}" text-anchor="middle" '
               f'fill="#fff" font-size="14" font-weight="bold">'
               f'{safe_xml(fam_id)}</text>')
    subtitle = f'paper {paper_w:.0f} × {paper_h:.0f} mm   ·   mode={mode}'
    if is_abstract:
        subtitle = f'ABSTRACT BASE   ·   {subtitle}'
    out.append(f'<text x="{canvas_w/2}" y="{title_h*0.78}" text-anchor="middle" '
               f'fill="#cfe2ff" font-size="9">'
               f'{safe_xml(subtitle)}</text>')

    # Sheet group — translate to leave top margin then flip Y so spec's
    # "0, 0 = bottom-left" coords map correctly.
    sheet_origin_x = margin
    sheet_origin_y = title_h + margin + paper_h    # bottom of paper after flip
    out.append(f'<g transform="translate({sheet_origin_x},{sheet_origin_y}) scale(1,-1)">')

    # Paper outline (drop shadow + border)
    out.append(f'<rect x="0" y="0" width="{paper_w}" height="{paper_h}" '
               f'fill="#ffffff" stroke="#1a1a1a" stroke-width="0.6"/>')

    # Filled regions first (background)
    for fr in eff.get("filledRegions", []) or []:
        if not fr.get("topLeft") or not fr.get("bottomRight"):
            continue
        x1 = min(fr["topLeft"][0], fr["bottomRight"][0])
        x2 = max(fr["topLeft"][0], fr["bottomRight"][0])
        y1 = min(fr["topLeft"][1], fr["bottomRight"][1])
        y2 = max(fr["topLeft"][1], fr["bottomRight"][1])
        colour = fr.get("color") or "#cccccc"
        out.append(f'<rect x="{x1}" y="{y1}" width="{x2-x1}" height="{y2-y1}" '
                   f'fill="{colour}" opacity="0.85"/>')

    # Lines
    for ln in eff.get("lines", []) or []:
        if not ln.get("from") or not ln.get("to"):
            continue
        sw, col = stroke_for_style(ln.get("style", "Medium Lines"))
        out.append(f'<line x1="{ln["from"][0]}" y1="{ln["from"][1]}" '
                   f'x2="{ln["to"][0]}"  y2="{ln["to"][1]}" '
                   f'stroke="{col}" stroke-width="{sw}"/>')

    # Static text — render with the sheet flip in mind: counter-flip the
    # text via a nested transform so it reads upright.
    for st in eff.get("staticText", []) or []:
        if not st.get("anchor") or not st.get("text"):
            continue
        x, y = st["anchor"][0], st["anchor"][1]
        size = st.get("size", 1.8)
        txt  = st["text"]
        anchor_attr = "start"
        h = (st.get("hAlign") or "Left").lower()
        if h == "center" or h == "centre": anchor_attr = "middle"
        elif h == "right": anchor_attr = "end"
        out.append(f'<g transform="translate({x},{y}) scale(1,-1)">'
                   f'<text x="0" y="0" font-size="{size}" '
                   f'text-anchor="{anchor_attr}" fill="#000">'
                   f'{safe_xml(txt)}</text></g>')

    # Parameter labels — render the param name as visible placeholder text.
    for lbl in eff.get("labels", []) or []:
        if not lbl.get("anchor") or not lbl.get("param"):
            continue
        x, y = lbl["anchor"][0], lbl["anchor"][1]
        size = lbl.get("size", 2.5)
        param = lbl["param"]
        # Strip the prefix to keep cells readable
        short = param.replace("PRJ_ORG_", "ORG·").replace("PRJ_TB_", "TB·") \
                     .replace("PRJ_DWG_", "DWG·").replace("STING_", "ST·") \
                     .replace("_TXT", "")
        anchor_attr = "start"
        h = (lbl.get("hAlign") or "Left").lower()
        if h == "center" or h == "centre": anchor_attr = "middle"
        elif h == "right": anchor_attr = "end"
        out.append(f'<g transform="translate({x},{y}) scale(1,-1)">'
                   f'<text x="0" y="0" font-size="{size}" '
                   f'text-anchor="{anchor_attr}" fill="#1F4E79" '
                   f'font-style="italic">'
                   f'{{{safe_xml(short)}}}</text></g>')

    # Slots — colour-coded per purposeTag; stroke style varies by category.
    # Sort so primary slots render first (under) and overlays on top.
    cat_order = {"primary": 0, "auxiliary": 1, "symbol": 2, "overlay": 3}
    slots_sorted = sorted(eff.get("slots", []) or [],
                          key=lambda s: cat_order.get(s.get("category", "primary"), 0))
    palette_legend = []  # collect unique purposeTags for the title-bar legend
    for sl in slots_sorted:
        if not sl.get("anchor") or not sl.get("size"):
            continue
        x, y = sl["anchor"][0], sl["anchor"][1]
        w, h = sl["size"][0], sl["size"][1]
        col   = slot_colour(sl)
        cat   = sl.get("category", "primary")
        dash  = CATEGORY_DASH.get(cat, "3,2")
        op    = CATEGORY_OPACITY.get(cat, "0.85")
        # Soft fill behind primary + auxiliary so overlapping slots are visible
        fill_op = "0.05" if cat == "primary" else "0.10" if cat == "auxiliary" else "0.0"
        out.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" '
                   f'fill="{col}" fill-opacity="{fill_op}" '
                   f'stroke="{col}" stroke-width="0.5" '
                   f'stroke-dasharray="{dash}" opacity="{op}"/>')
        # corner badge — small solid rectangle behind the id text so it
        # stays readable against any background
        sl_id = sl.get("id", "?")
        badge_w = max(12, len(sl_id) * 2.5 + 4)
        badge_h = 7
        out.append(f'<rect x="{x}" y="{y+h-badge_h}" width="{badge_w}" '
                   f'height="{badge_h}" fill="{col}" opacity="0.9"/>')
        out.append(f'<g transform="translate({x+2},{y+h-1.5}) scale(1,-1)">'
                   f'<text x="0" y="0" font-size="4" font-weight="bold" '
                   f'fill="#fff">{safe_xml(sl_id)}</text></g>')
        # purpose tag below the badge
        tag = sl.get("purposeTag")
        if tag:
            out.append(f'<g transform="translate({x+2},{y+h-badge_h-2}) scale(1,-1)">'
                       f'<text x="0" y="0" font-size="3" '
                       f'fill="{col}" opacity="0.85">'
                       f'{safe_xml(tag)}</text></g>')
            if tag not in [pl[0] for pl in palette_legend]:
                palette_legend.append((tag, col))

    out.append('</g>')

    # Slot palette legend strip — sits below the title bar, above the
    # sheet. Shows up to 12 unique purposeTags with their colour swatches
    # so the reviewer can decode the slot outlines without referring to
    # the source file.
    if palette_legend:
        leg_y = title_h + 6
        leg_x = margin
        out.append(f'<text x="{leg_x}" y="{leg_y - 1}" font-size="6" '
                   f'fill="#666" font-weight="bold">slot legend:</text>')
        leg_x += 50
        max_show = min(12, len(palette_legend))
        for i, (tag, col) in enumerate(palette_legend[:max_show]):
            out.append(f'<rect x="{leg_x}" y="{leg_y - 5}" width="6" height="6" '
                       f'fill="{col}" opacity="0.85"/>')
            out.append(f'<text x="{leg_x + 9}" y="{leg_y - 1}" font-size="5" '
                       f'fill="#333">{safe_xml(tag)}</text>')
            leg_x += 12 + len(tag) * 3
        if len(palette_legend) > max_show:
            out.append(f'<text x="{leg_x}" y="{leg_y - 1}" font-size="5" '
                       f'fill="#999">+{len(palette_legend) - max_show} more</text>')

    # Footer with stats + description (under the sheet)
    counts = {
        "params": len(eff.get("parameters") or []),
        "lines":  len(eff.get("lines") or []),
        "labels": len(eff.get("labels") or []),
        "static": len(eff.get("staticText") or []),
        "fills":  len(eff.get("filledRegions") or []),
        "slots":  len(eff.get("slots") or []),
    }
    stat_y = canvas_h - margin/2
    out.append(f'<text x="{margin}" y="{stat_y}" font-size="7" fill="#444">'
               f'parameters {counts["params"]}  ·  lines {counts["lines"]}  ·  '
               f'labels {counts["labels"]}  ·  static-text {counts["static"]}  ·  '
               f'fills {counts["fills"]}  ·  slots {counts["slots"]}</text>')
    if desc:
        # Wrap description ~140 chars
        wrap = (desc[:200] + "…") if len(desc) > 200 else desc
        out.append(f'<text x="{margin}" y="{stat_y+10}" font-size="6" fill="#666">'
                   f'{safe_xml(wrap)}</text>')

    out.append('</svg>')
    return "\n".join(out)


def main():
    if not SPEC.exists():
        print(f"ERROR: {SPEC} not found", file=sys.stderr)
        sys.exit(1)
    OUT.mkdir(parents=True, exist_ok=True)
    with open(SPEC, "r", encoding="utf-8") as f:
        lib = json.load(f)

    target = sys.argv[1] if len(sys.argv) > 1 else None
    written = 0
    skipped = 0
    for fam in lib.get("families", []):
        fam_id = fam.get("id")
        if not fam_id:
            skipped += 1
            continue
        if target and fam_id != target:
            continue
        paper_w, paper_h = detect_paper(fam_id, fam)
        svg = render_svg(fam, paper_w, paper_h, lib)
        path = OUT / f"{fam_id}.svg"
        with open(path, "w", encoding="utf-8") as out:
            out.write(svg)
        written += 1
        print(f"  ✓ {fam_id}.svg  ({paper_w:.0f}×{paper_h:.0f} mm)")
    print(f"\nWrote {written} SVG previews to {OUT}")
    print(f"Open one in a browser, e.g. file://{OUT}/{lib['families'][0]['id']}.svg")
    if skipped:
        print(f"Skipped {skipped} families with no id")


if __name__ == "__main__":
    main()
