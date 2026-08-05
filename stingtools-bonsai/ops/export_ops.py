"""Export operators — tag register CSV, compliance snapshot, BOQ, audit log."""

from __future__ import annotations

import csv
import json
import pathlib
import hashlib
import datetime

import bpy


def _bim_coord_dir(ifc_path: str) -> pathlib.Path:
    """Return the _BIM_COORD directory next to the IFC file."""
    p = pathlib.Path(ifc_path)
    d = p.parent / "_BIM_COORD"
    d.mkdir(exist_ok=True)
    return d


def _ts() -> str:
    return datetime.datetime.utcnow().strftime("%Y%m%d_%H%M%S")


# ---------------------------------------------------------------------------
# Tag register export
# ---------------------------------------------------------------------------

class StingExportTagRegisterOperator(bpy.types.Operator):
    """Export all tagged IFC elements to a CSV tag register."""

    bl_idname = "sting.export_tag_register"
    bl_label = "Export Tag Register"
    bl_description = "Write all Pset_StingTags values to a CSV file in _BIM_COORD/"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            from ..core.bonsai import bonsai as _bridge
            ifc = _bridge.active_ifc()
        except Exception as exc:
            self.report({"ERROR"}, f"Cannot access IFC model: {exc}")
            return {"CANCELLED"}

        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded — open a model in Bonsai first")
            return {"CANCELLED"}

        try:
            ifc_path = ifc.path
        except AttributeError:
            self.report({"ERROR"}, "Cannot resolve IFC path")
            return {"CANCELLED"}

        pset_name = "Pset_StingTags"
        fields = ["Discipline", "Location", "Zone", "Level", "System", "Function", "Product", "Sequence", "FullTag"]
        rows = []

        try:
            import ifcopenshell  # provided by Bonsai
            import ifcopenshell.util.element as ifc_util
            for el in ifc.by_type("IfcElement"):
                psets = ifc_util.get_psets(el)
                stag = psets.get(pset_name, {})
                if not stag:
                    continue
                rows.append({
                    "GlobalId": el.GlobalId,
                    "IfcClass": el.is_a(),
                    "Name": el.Name or "",
                    **{f: stag.get(f, "") for f in fields},
                })
        except Exception as exc:
            self.report({"ERROR"}, f"IFC traversal failed: {exc}")
            return {"CANCELLED"}

        out_path = _bim_coord_dir(ifc_path) / f"tag_register_{_ts()}.csv"
        try:
            with out_path.open("w", newline="", encoding="utf-8") as fh:
                writer = csv.DictWriter(fh, fieldnames=["GlobalId", "IfcClass", "Name"] + fields)
                writer.writeheader()
                writer.writerows(rows)
        except OSError as exc:
            self.report({"ERROR"}, f"Cannot write CSV: {exc}")
            return {"CANCELLED"}

        self.report({"INFO"}, f"Tag register exported — {len(rows)} rows → {out_path.name}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Compliance snapshot export
# ---------------------------------------------------------------------------

class StingExportComplianceSnapshotOperator(bpy.types.Operator):
    """Export a JSON compliance snapshot and optionally push to Planscape."""

    bl_idname = "sting.export_compliance_snapshot"
    bl_label = "Export Compliance Snapshot"
    bl_description = "Write tag-completeness JSON snapshot; optionally push to Planscape Server"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            from ..core.bonsai import bonsai as _bridge
            ifc = _bridge.active_ifc()
        except Exception as exc:
            self.report({"ERROR"}, f"Cannot access IFC model: {exc}")
            return {"CANCELLED"}

        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            ifc_path = ifc.path
        except AttributeError:
            self.report({"ERROR"}, "Cannot resolve IFC path")
            return {"CANCELLED"}

        pset_name = "Pset_StingTags"
        required_fields = ["Discipline", "Location", "Zone", "Level", "System", "Function", "Product", "Sequence"]

        total = complete = incomplete = untagged = 0
        by_disc: dict[str, dict] = {}

        try:
            import ifcopenshell
            import ifcopenshell.util.element as ifc_util
            for el in ifc.by_type("IfcElement"):
                psets = ifc_util.get_psets(el)
                stag = psets.get(pset_name, {})
                total += 1
                if not stag:
                    untagged += 1
                    continue
                disc = stag.get("Discipline", "XX")
                filled = sum(1 for f in required_fields if stag.get(f, "XX") not in ("", "XX"))
                if filled == len(required_fields):
                    complete += 1
                else:
                    incomplete += 1
                d = by_disc.setdefault(disc, {"total": 0, "complete": 0, "incomplete": 0})
                d["total"] += 1
                if filled == len(required_fields):
                    d["complete"] += 1
                else:
                    d["incomplete"] += 1
        except Exception as exc:
            self.report({"ERROR"}, f"IFC traversal failed: {exc}")
            return {"CANCELLED"}

        pct = round(complete / total * 100, 1) if total else 0.0
        snapshot = {
            "captured_at": datetime.datetime.utcnow().isoformat() + "Z",
            "total": total,
            "complete": complete,
            "incomplete": incomplete,
            "untagged": untagged,
            "compliance_pct": pct,
            "by_discipline": by_disc,
        }

        out_path = _bim_coord_dir(ifc_path) / f"compliance_{_ts()}.json"
        try:
            out_path.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
        except OSError as exc:
            self.report({"ERROR"}, f"Cannot write snapshot: {exc}")
            return {"CANCELLED"}

        # Attempt Planscape push (non-fatal if offline)
        try:
            from .. import prefs as _p
            pr = _p.get_prefs(context)
            token = pr.api_token or ""
            project_id = pr.project_id or ""
            if token and project_id:
                from stingtools_core.planscape import PlanscapeClient  # type: ignore
                client = PlanscapeClient(token=token)
                client.push_compliance(project_id=project_id, snapshot=snapshot)
                self.report({"INFO"}, f"Snapshot pushed to Planscape — {pct}% compliant")
            else:
                self.report({"INFO"}, f"Snapshot saved locally — {pct}% compliant ({out_path.name})")
        except Exception:
            self.report({"INFO"}, f"Snapshot saved locally (Planscape unavailable) — {pct}% compliant")

        return {"FINISHED"}


# ---------------------------------------------------------------------------
# BOQ export
# ---------------------------------------------------------------------------

class StingExportBOQOperator(bpy.types.Operator):
    """Export a Bill of Quantities CSV grouped by Discipline x System x IfcClass."""

    bl_idname = "sting.export_boq"
    bl_label = "Export BOQ"
    bl_description = "Write a Bill of Quantities CSV to _BIM_COORD/"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            from ..core.bonsai import bonsai as _bridge
            ifc = _bridge.active_ifc()
        except Exception as exc:
            self.report({"ERROR"}, f"Cannot access IFC model: {exc}")
            return {"CANCELLED"}

        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            ifc_path = ifc.path
        except AttributeError:
            self.report({"ERROR"}, "Cannot resolve IFC path")
            return {"CANCELLED"}

        pset_name = "Pset_StingTags"
        groups: dict[tuple, int] = {}

        try:
            import ifcopenshell
            import ifcopenshell.util.element as ifc_util
            for el in ifc.by_type("IfcElement"):
                psets = ifc_util.get_psets(el)
                stag = psets.get(pset_name, {})
                disc = stag.get("Discipline", "XX")
                sys_ = stag.get("System", "XX")
                ifc_class = el.is_a()
                key = (disc, sys_, ifc_class)
                groups[key] = groups.get(key, 0) + 1
        except Exception as exc:
            self.report({"ERROR"}, f"IFC traversal failed: {exc}")
            return {"CANCELLED"}

        out_path = _bim_coord_dir(ifc_path) / f"boq_{_ts()}.csv"
        try:
            with out_path.open("w", newline="", encoding="utf-8") as fh:
                writer = csv.writer(fh)
                writer.writerow(["Discipline", "System", "IfcClass", "Quantity", "Unit"])
                for (disc, sys_, cls_), qty in sorted(groups.items()):
                    writer.writerow([disc, sys_, cls_, qty, "Nr"])
        except OSError as exc:
            self.report({"ERROR"}, f"Cannot write BOQ: {exc}")
            return {"CANCELLED"}

        total_items = sum(groups.values())
        self.report({"INFO"}, f"BOQ exported — {total_items} items, {len(groups)} lines → {out_path.name}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Audit log export / integrity check
# ---------------------------------------------------------------------------

class StingAuditLogExportOperator(bpy.types.Operator):
    """Verify and export the STING SHA-256-chained audit log."""

    bl_idname = "sting.export_audit_log"
    bl_label = "Export Audit Log"
    bl_description = "Verify SHA-256 chain integrity and copy audit log to a timestamped file"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            from ..core.bonsai import bonsai as _bridge
            ifc = _bridge.active_ifc()
        except Exception as exc:
            self.report({"ERROR"}, f"Cannot access IFC model: {exc}")
            return {"CANCELLED"}

        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            ifc_path = ifc.path
        except AttributeError:
            self.report({"ERROR"}, "Cannot resolve IFC path")
            return {"CANCELLED"}

        src = _bim_coord_dir(ifc_path) / "sting_audit.jsonl"
        if not src.exists():
            self.report({"WARNING"}, "No audit log found at _BIM_COORD/sting_audit.jsonl")
            return {"CANCELLED"}

        lines = src.read_text(encoding="utf-8").splitlines()
        errors: list[str] = []
        prev_hash = ""
        for i, line in enumerate(lines):
            try:
                entry = json.loads(line)
            except json.JSONDecodeError:
                errors.append(f"Line {i+1}: JSON parse error")
                continue
            stored_hash = entry.get("hash", "")
            payload = json.dumps({k: v for k, v in entry.items() if k != "hash"}, sort_keys=True)
            expected = hashlib.sha256((prev_hash + payload).encode()).hexdigest()
            if stored_hash != expected:
                errors.append(f"Line {i+1}: hash mismatch (chain broken)")
            prev_hash = stored_hash

        out_path = _bim_coord_dir(ifc_path) / f"audit_export_{_ts()}.jsonl"
        try:
            import shutil
            shutil.copy2(src, out_path)
        except OSError as exc:
            self.report({"ERROR"}, f"Cannot copy audit log: {exc}")
            return {"CANCELLED"}

        if errors:
            self.report({"WARNING"}, f"Audit log has {len(errors)} chain error(s) — see system console")
            for err in errors[:5]:
                print(f"[STING AUDIT] {err}")
        else:
            self.report({"INFO"}, f"Audit log OK ({len(lines)} entries) → {out_path.name}")

        return {"FINISHED"}


CLASSES = (
    StingExportTagRegisterOperator,
    StingExportComplianceSnapshotOperator,
    StingExportBOQOperator,
    StingAuditLogExportOperator,
)
