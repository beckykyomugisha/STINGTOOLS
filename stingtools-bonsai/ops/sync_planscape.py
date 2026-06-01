"""Sync the active IFC model's STING-tagged elements to Planscape Server.

Pipeline:
  1. Resolve the active IFC file via BonsaiBridge.
  2. Collect every IfcElement carrying Pset_StingTags.
  3. Validate each element with the stingtools-core tools that already
     exist — SpatialChecker (cross-entity / spatial / SEQ rules) and the
     tag-grammar validator (per-segment enum membership) — and attach the
     resulting errors to the element payload.
  4. POST the batch to /api/projects/{id}/ifc/data via PlanscapeClient
     with host="blender".
  5. Report server-side counts back to the Blender UI.

Network + substrate failures are caught and surfaced as operator errors
rather than tracebacks.
"""

from __future__ import annotations

import json

import bpy

# Active RIBA stage the validators run at. Stage_3 is the substrate's main
# production gate (matches SpatialChecker's own default).
DEFAULT_STAGE = "Stage_3"


class StingSyncPlanscapeOperator(bpy.types.Operator):
    """Validate STING-tagged elements and push them to Planscape Server."""

    bl_idname = "sting.sync_planscape"
    bl_label = "Sync to Planscape"
    bl_description = (
        "Validate every element carrying Pset_StingTags and ingest it into "
        "the configured Planscape project (host=blender)"
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        # ── 1. preferences ─────────────────────────────────────────────
        try:
            from ..prefs import get_prefs
            prefs = get_prefs(context)
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"Could not read add-on preferences: {e}")
            return {"CANCELLED"}

        server_url = (prefs.server_url or "").strip()
        project_id = (prefs.project_id or "").strip()
        token = (prefs.api_token or "").strip()
        if not server_url:
            self.report({"ERROR"}, "Set the Planscape Server URL in add-on preferences")
            return {"CANCELLED"}
        if not project_id:
            self.report({"ERROR"}, "Set the Planscape Project ID in add-on preferences")
            return {"CANCELLED"}

        # ── 2. core + ifcopenshell imports ─────────────────────────────
        try:
            from stingtools_core import EnumRegistry, SpatialChecker, Tag, TagGrammar
            from stingtools_core.planscape import PlanscapeClient
            from stingtools_core.exceptions import PlanscapeError
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools-core not available: {e}")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ue  # type: ignore
        except ImportError as e:
            self.report({"ERROR"}, f"ifcopenshell not available (is Bonsai installed?): {e}")
            return {"CANCELLED"}

        # ── 3. active IFC model ────────────────────────────────────────
        from ..core import bonsai
        model = bonsai.active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        # ── 4. validators ──────────────────────────────────────────────
        # Enum registry is best-effort: if shared/ifc can't be located we
        # still run the structural SpatialChecker (which doesn't need it)
        # and skip the per-segment enum-membership pass.
        enum_registry = None
        grammar = None
        try:
            enum_registry = EnumRegistry().load()
            grammar = TagGrammar(enum_registry)
        except Exception as e:  # noqa: BLE001 — substrate optional for sync
            print(f"[STING] enum registry unavailable, tag-grammar checks skipped: {e}")

        checker = SpatialChecker(model, stage=DEFAULT_STAGE, enum_registry=enum_registry)

        # Group spatial mismatches by element GlobalId (one model-wide pass).
        errors_by_gid: dict[str, list[dict]] = {}
        try:
            for m in checker.check_all_elements():
                errors_by_gid.setdefault(m.ifc_global_id, []).append(
                    {"rule": m.rule_id, "segment": m.segment, "message": m.message}
                )
        except Exception as e:  # noqa: BLE001
            print(f"[STING] spatial check failed, continuing without it: {e}")

        # ── 5. collect Pset_StingTags-bearing elements ─────────────────
        elements: list[dict] = []
        for el in model.by_type("IfcElement"):
            psets = ue.get_psets(el) or {}
            tags = psets.get("Pset_StingTags")
            if not tags:
                continue

            gid = getattr(el, "GlobalId", "") or ""
            tag = Tag.from_pset(tags)

            el_errors = list(errors_by_gid.get(gid, []))
            if grammar is not None:
                try:
                    res = grammar.validate(tag, stage=DEFAULT_STAGE)
                    for msg in res.errors:
                        el_errors.append({"rule": "TAG_GRAMMAR", "segment": "", "message": msg})
                except Exception as e:  # noqa: BLE001
                    print(f"[STING] tag validation failed for {gid}: {e}")

            is_complete = tag.is_complete()
            elements.append({
                "ifc_global_id": gid,
                "host_element_id": bonsai.host_element_id(el),
                "host_display_label": getattr(el, "Name", None),
                "discipline": tag.discipline,
                "location": tag.location,
                "zone": tag.zone,
                "level": tag.level,
                "system": tag.system,
                "function": tag.function,
                "product": tag.product,
                "sequence": tag.sequence,
                "full_tag": tags.get("FullTag") or tag.to_full_tag(),
                "ifc_class": el.is_a(),
                "is_complete": is_complete,
                "is_fully_resolved": is_complete and not el_errors,
                "is_stale": False,
                "validation_errors": json.dumps(el_errors) if el_errors else None,
            })

        if not elements:
            self.report({"WARNING"}, "No elements carry Pset_StingTags — nothing to sync")
            return {"CANCELLED"}

        # ── 6. push to Planscape ───────────────────────────────────────
        client = PlanscapeClient(server_url, access_token=token or None, client_type="blender")
        try:
            resp = client.ingest_ifc_data(
                project_id,
                elements,
                host_document_guid=_blend_doc_guid(),
                plugin_version=_plugin_version(),
                user_name=_user_name(),
            )
        except PlanscapeError as e:
            self.report({"ERROR"}, f"Planscape sync failed: {e}")
            return {"CANCELLED"}
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"Unexpected error during sync: {e}")
            return {"CANCELLED"}

        # ── 7. report ──────────────────────────────────────────────────
        flagged = sum(1 for el in elements if el["validation_errors"])
        new_m = resp.get("newMappings", 0) + resp.get("updatedMappings", 0)
        new_e = resp.get("newElements", 0) + resp.get("updatedElements", 0)
        skipped = resp.get("skipped", 0)
        summary = (
            f"Synced {len(elements)} element(s): {new_m} mapping(s), {new_e} element(s)"
            f"{f', {skipped} skipped' if skipped else ''}"
            f"{f' · {flagged} flagged with validation errors' if flagged else ''}"
        )
        self.report({"INFO"}, summary)
        print(f"[STING] {summary}")
        for w in (resp.get("warnings") or [])[:10]:
            print(f"[STING]   warning: {w}")
        return {"FINISHED"}


def _plugin_version() -> str:
    try:
        from .. import bl_info
        return ".".join(str(p) for p in bl_info.get("version", ()))
    except Exception:  # noqa: BLE001
        return ""


def _user_name() -> str:
    try:
        import getpass
        return getpass.getuser()
    except Exception:  # noqa: BLE001
        return ""


def _blend_doc_guid() -> str | None:
    """A stable per-document id derived from the .blend path. None for an
    unsaved session (matches the DTO's 'may be null' contract)."""
    path = bpy.data.filepath
    if not path:
        return None
    import hashlib
    return hashlib.sha1(path.encode("utf-8")).hexdigest()[:16]
