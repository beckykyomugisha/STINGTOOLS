"""Export operators — tag register CSV, BOQ CSV, compliance snapshot, audit log export."""

from __future__ import annotations

import csv
import hashlib
import json
import os
from datetime import datetime, timezone
from pathlib import Path

import bpy


# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------

def _get_ifc():
    try:
        from ..core.bonsai import bonsai as _bridge
        return _bridge.active_ifc()
    except Exception:
        return None


def _bim_coord_dir(ifc_path: str) -> Path:
    """Return <ifc_dir>/_BIM_COORD/, creating it if absent."""
    p = Path(ifc_path).parent / "_BIM_COORD"
    p.mkdir(parents=True, exist_ok=True)
    return p


def _ts() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


# ---------------------------------------------------------------------------
# Tag Register CSV export
# ---------------------------------------------------------------------------

class StingExportTagRegisterOperator(bpy.types.Operator):
    """Export Pset_StingTags for every IfcElement to a CSV tag register."""

    bl_idname = "sting.export_tag_register"
    bl_label = "Export Tag Register"
    bl_description = (
        "Write a CSV of all IfcElement Pset_StingTags to "
        "<ifc_dir>/_BIM_COORD/tag_register_<ts>.csv"
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        ifc_path = getattr(ifc, "path", None) or ""
        if not ifc_path:
            self.report({"WARNING"}, "IFC path unknown — writing to Blender temp dir")
            out_dir = Path(bpy.app.tempdir)
        else:
            out_dir = _bim_coord_dir(ifc_path)

        out_path = out_dir / f"tag_register_{_ts()}.csv"

        fields = [
            "GlobalId", "IfcClass", "Name",
            "Discipline", "Location", "Zone", "Level",
            "System", "Function", "Product", "Sequence", "FullTag",
        ]

        rows_written = 0
        with out_path.open("w", newline="", encoding="utf-8") as fh:
            writer = csv.DictWriter(fh, fieldnames=fields)
            writer.writeheader()
            for el in ifc.by_type("IfcElement"):
                psets = ifc_util.get_psets(el)
                stag = psets.get("Pset_StingTags", {})
                writer.writerow({
                    "GlobalId":   getattr(el, "GlobalId", ""),
                    "IfcClass":   el.is_a(),
                    "Name":       getattr(el, "Name", "") or "",
                    "Discipline": stag.get("Discipline", ""),
                    "Location":   stag.get("Location", ""),
                    "Zone":       stag.get("Zone", ""),
                    "Level":      stag.get("Level", ""),
                    "System":     stag.get("System", ""),
                    "Function":   stag.get("Function", ""),
                    "Product":    stag.get("Product", ""),
                    "Sequence":   stag.get("Sequence", ""),
                    "FullTag":    stag.get("FullTag", ""),
                })
                rows_written += 1

        self.report({"INFO"}, f"Tag register exported: {rows_written} rows → {out_path.name}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Bill of Quantities CSV export
# ---------------------------------------------------------------------------

class StingExportBOQOperator(bpy.types.Operator):
    """Export a simple Bill of Quantities grouped by Discipline / System / IfcClass."""

    bl_idname = "sting.export_boq"
    bl_label = "Export BOQ"
    bl_description = (
        "Group IfcElements by (Discipline, System, IfcClass) and write a "
        "count-based BOQ to <ifc_dir>/_BIM_COORD/boq_<ts>.csv"
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        from collections import Counter
        counts: Counter = Counter()

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get("Pset_StingTags", {})
            disc = stag.get("Discipline", "XX")
            sys_ = stag.get("System", "XX")
            ifc_class = el.is_a()
            counts[(disc, sys_, ifc_class)] += 1

        ifc_path = getattr(ifc, "path", None) or ""
        out_dir = _bim_coord_dir(ifc_path) if ifc_path else Path(bpy.app.tempdir)
        out_path = out_dir / f"boq_{_ts()}.csv"

        with out_path.open("w", newline="", encoding="utf-8") as fh:
            writer = csv.writer(fh)
            writer.writerow(["Discipline", "System", "IfcClass", "Quantity"])
            for (disc, sys_, ifc_class), qty in sorted(counts.items()):
                writer.writerow([disc, sys_, ifc_class, qty])

        self.report({"INFO"}, f"BOQ exported: {sum(counts.values())} elements → {out_path.name}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Compliance Snapshot JSON export
# ---------------------------------------------------------------------------

class StingExportComplianceSnapshotOperator(bpy.types.Operator):
    """Snapshot tag completeness metrics to a JSON file and optionally push to Planscape."""

    bl_idname = "sting.export_compliance_snapshot"
    bl_label = "Export Compliance Snapshot"
    bl_description = (
        "Count complete / incomplete / untagged elements and write a JSON "
        "snapshot to <ifc_dir>/_BIM_COORD/compliance_<ts>.json. "
        "Pushes to Planscape when a token is configured in add-on prefs."
    )
    bl_options = {"REGISTER"}

    _REQUIRED = ["Discipline", "Location", "Zone", "Level",
                 "System", "Function", "Product", "Sequence"]
    _SENTINEL = {"", "XX"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        total = complete = incomplete = untagged = 0
        discipline_counts: dict[str, int] = {}

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get("Pset_StingTags", {})
            total += 1
            disc = stag.get("Discipline", "XX")
            if not stag or disc in self._SENTINEL:
                untagged += 1
                continue
            missing = [f for f in self._REQUIRED if stag.get(f, "XX") in self._SENTINEL]
            if missing:
                incomplete += 1
            else:
                complete += 1
                discipline_counts[disc] = discipline_counts.get(disc, 0) + 1

        pct = round(complete / total * 100, 1) if total else 0.0
        snapshot = {
            "timestamp": _ts(),
            "total": total,
            "complete": complete,
            "incomplete": incomplete,
            "untagged": untagged,
            "compliance_pct": pct,
            "by_discipline": discipline_counts,
        }

        ifc_path = getattr(ifc, "path", None) or ""
        out_dir = _bim_coord_dir(ifc_path) if ifc_path else Path(bpy.app.tempdir)
        out_path = out_dir / f"compliance_{_ts()}.json"
        out_path.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")

        # Optional Planscape push
        try:
            from .. import prefs as _p
            pr = _p.get_prefs(context)
            if pr.api_token and pr.project_id:
                from stingtools_core.planscape import PlanscapeClient  # type: ignore
                client = PlanscapeClient(pr.api_token)
                client.push_compliance_snapshot(pr.project_id, snapshot)
        except Exception:
            pass

        self.report({"INFO"}, f"{pct}% compliant ({complete}/{total}) — saved to {out_path.name}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Audit Log export
# ---------------------------------------------------------------------------

class StingAuditLogExportOperator(bpy.types.Operator):
    """Verify SHA-256 chain integrity and export the audit JSONL to a timestamped copy."""

    bl_idname = "sting.export_audit_log"
    bl_label = "Export Audit Log"
    bl_description = (
        "Read <ifc_dir>/_BIM_COORD/sting_audit.jsonl, verify the SHA-256 "
        "tamper-evidence chain, and copy to sting_audit_export_<ts>.jsonl"
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        ifc_path = getattr(ifc, "path", None) or ""
        if not ifc_path:
            self.report({"ERROR"}, "IFC path unknown — cannot locate audit log")
            return {"CANCELLED"}

        bim_dir = _bim_coord_dir(ifc_path)
        audit_path = bim_dir / "sting_audit.jsonl"

        if not audit_path.exists():
            self.report({"WARNING"}, "No audit log found — nothing to export")
            return {"CANCELLED"}

        lines = audit_path.read_text(encoding="utf-8").splitlines()
        tampered = 0
        prev_hash = ""
        for line in lines:
            if not line.strip():
                continue
            try:
                entry = json.loads(line)
                stored_prev = entry.get("prev_hash", "")
                if stored_prev != prev_hash:
                    tampered += 1
                prev_hash = hashlib.sha256(line.encode()).hexdigest()
            except json.JSONDecodeError:
                tampered += 1

        out_path = bim_dir / f"sting_audit_export_{_ts()}.jsonl"
        import shutil
        shutil.copy2(audit_path, out_path)

        if tampered:
            self.report({"WARNING"}, f"Exported {len(lines)} entries — {tampered} CHAIN VIOLATIONS detected")
        else:
            self.report({"INFO"}, f"Audit log exported: {len(lines)} entries, chain OK → {out_path.name}")
        return {"FINISHED"}


CLASSES = (
    StingExportTagRegisterOperator,
    StingExportBOQOperator,
    StingExportComplianceSnapshotOperator,
    StingAuditLogExportOperator,
)
