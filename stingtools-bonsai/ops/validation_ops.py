"""VALIDATE operators — IDS pipeline, dashboard, and result management."""

from __future__ import annotations

import json

import bpy

_RESULTS_KEY = "sting_validation_results"


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _active_ifc():
    from ..core.bonsai import bonsai
    return bonsai.active_ifc()


def _get_pset(element, pset_name: str) -> dict:
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        return ifc_util.get_pset(element, pset_name) or {}
    except Exception:
        return {}


# ---------------------------------------------------------------------------
# 11 — Run IDS Validation
# ---------------------------------------------------------------------------

class StingValidateIDSOperator(bpy.types.Operator):
    """Validate all IFC elements against Pset_StingTags grammar and STING rules."""

    bl_idname = "sting.validate_ids"
    bl_label = "Run IDS Validation"
    bl_description = "Check every element's 8-segment tag against the STING enum registry"
    bl_options = {"REGISTER"}

    stage: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="RIBA Stage",
        description="Minimum completeness gate (higher stages require more segments)",
        items=[
            ("Stage_2", "Stage 2 — Concept", "Disc + Location + Level required"),
            ("Stage_3", "Stage 3 — Developed", "Disc + Loc + Zone + Level + Sys + Seq required"),
            ("Stage_4", "Stage 4 — Technical", "All 8 segments required"),
        ],
        default="Stage_3",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        # Load substrate
        try:
            from stingtools_core import TagGrammar, EnumRegistry  # type: ignore
            from stingtools_core.tag_grammar import Tag  # type: ignore
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools_core not available: {e}")
            return {"CANCELLED"}

        from ..core.state import StingState
        state = StingState.get()
        grammar = state.tag_grammar
        if grammar is None:
            self.report({"ERROR"}, "EnumRegistry failed to load — check substrate path")
            return {"CANCELLED"}

        # Run validation
        results = {
            "stage": self.stage,
            "total": 0,
            "valid": 0,
            "invalid": 0,
            "warnings_only": 0,
            "no_pset": 0,
            "issues": [],   # list of {ifc_id, ifc_class, tag, errors, warnings}
        }

        try:
            elements = list(model.by_type("IfcElement"))
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        results["total"] = len(elements)

        for el in elements:
            pset = _get_pset(el, "Pset_StingTags")
            if not pset:
                results["no_pset"] += 1
                results["issues"].append({
                    "ifc_id": el.id(),
                    "ifc_class": el.is_a(),
                    "tag": "",
                    "errors": ["No Pset_StingTags found"],
                    "warnings": [],
                })
                results["invalid"] += 1
                continue

            try:
                tag = Tag.from_pset(pset)
            except Exception as e:
                results["invalid"] += 1
                results["issues"].append({
                    "ifc_id": el.id(),
                    "ifc_class": el.is_a(),
                    "tag": str(pset.get("FullTag", "")),
                    "errors": [f"Tag parse error: {e}"],
                    "warnings": [],
                })
                continue

            result = grammar.validate(tag, stage=self.stage)

            entry = {
                "ifc_id": el.id(),
                "ifc_class": el.is_a(),
                "tag": tag.to_full_tag(),
                "errors": result.errors,
                "warnings": result.warnings,
            }

            if result.is_valid and not result.warnings:
                results["valid"] += 1
            elif result.is_valid and result.warnings:
                results["warnings_only"] += 1
                results["issues"].append(entry)
            else:
                results["invalid"] += 1
                results["issues"].append(entry)

        # Cap stored issues to 200 to keep scene data manageable
        results["issues"] = results["issues"][:200]
        context.scene[_RESULTS_KEY] = json.dumps(results)

        self.report(
            {"INFO"},
            f"Validation complete: {results['valid']} valid, "
            f"{results['invalid']} invalid, "
            f"{results['warnings_only']} warnings-only, "
            f"{results['no_pset']} missing pset (of {results['total']} total)",
        )
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 12 — Validation Dashboard
# ---------------------------------------------------------------------------

class StingValidationDashboardOperator(bpy.types.Operator):
    """Show the top validation issues from the last IDS run in the info header."""

    bl_idname = "sting.validation_dashboard"
    bl_label = "Validation Dashboard"
    bl_description = "Print the top 20 validation issues from the last run to the Info header"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        raw = context.scene.get(_RESULTS_KEY)
        if not raw:
            self.report({"WARNING"}, "No validation results — run 'Run IDS Validation' first")
            return {"CANCELLED"}

        try:
            data = json.loads(raw)
        except Exception:
            self.report({"ERROR"}, "Validation results corrupted — re-run IDS validation")
            return {"CANCELLED"}

        total = data.get("total", 0)
        valid = data.get("valid", 0)
        invalid = data.get("invalid", 0)
        warn_only = data.get("warnings_only", 0)
        no_pset = data.get("no_pset", 0)
        stage = data.get("stage", "?")

        lines = [
            f"=== STING IDS Validation ({stage}) ===",
            f"Total: {total}  |  Valid: {valid}  |  Invalid: {invalid}  "
            f"|  Warnings: {warn_only}  |  No Pset: {no_pset}",
            "--- Top issues ---",
        ]

        issues = data.get("issues", [])
        for issue in issues[:20]:
            tag = issue.get("tag", "(no tag)")
            cls = issue.get("ifc_class", "?")
            ifc_id = issue.get("ifc_id", "?")
            for err in issue.get("errors", []):
                lines.append(f"  [{ifc_id}] {cls}  {tag}  ERROR: {err}")
            for w in issue.get("warnings", []):
                lines.append(f"  [{ifc_id}] {cls}  {tag}  WARN:  {w}")

        if len(issues) > 20:
            lines.append(f"  ... {len(issues) - 20} more (re-run and check System Console)")

        # Print to system console for full detail
        full_report = "\n".join(lines)
        print(f"\n{full_report}\n")

        # Blender INFO area only shows one line — show the summary
        pct = round(valid / total * 100, 1) if total else 0
        self.report(
            {"INFO"},
            f"IDS {stage}: {pct}% valid ({valid}/{total}) — see System Console for detail",
        )
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 13 — Clear Validation Results
# ---------------------------------------------------------------------------

class StingClearValidationOperator(bpy.types.Operator):
    """Remove cached IDS validation results from the scene."""

    bl_idname = "sting.clear_validation"
    bl_label = "Clear Validation Results"
    bl_description = "Delete stored validation results from this Blender scene"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        if _RESULTS_KEY in context.scene:
            del context.scene[_RESULTS_KEY]
            self.report({"INFO"}, "Validation results cleared")
        else:
            self.report({"INFO"}, "No validation results to clear")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# registration
# ---------------------------------------------------------------------------

CLASSES = (
    StingValidateIDSOperator,
    StingValidationDashboardOperator,
    StingClearValidationOperator,
)
