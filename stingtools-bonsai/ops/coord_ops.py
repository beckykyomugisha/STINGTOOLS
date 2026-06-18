"""COORD operators — Planscape login + cross-host IFC push + issue raise.

All HTTP goes through ``..planscape.client.PlanscapeClient`` which is
Python-stdlib-only (urllib) — no ``requests`` / ``stingtools_core`` /
``_vendor`` needed, so these work on a stock Blender 4.2+ with Bonsai.

The push posts to ``/api/projects/{id}/ifc/data`` with ``host="bonsai"``,
keyed on each element's 22-char IFC ``GlobalId`` (the cross-host key),
carrying the host element id, and NO ``revitElementId``.
"""

from __future__ import annotations

import datetime
import json
import os
from pathlib import Path

import bpy

# Host string for every Bonsai-originated push. Matches
# Planscape.Core.Constants.MappingHosts.Bonsai on the server.
HOST = "bonsai"

# Session mirror keys (panel status only; the source of truth is prefs).
_TOKEN_KEY = "sting_planscape_token"
_EMAIL_KEY = "sting_planscape_email"


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _prefs(context):
    from .. import prefs as _p
    return _p.get_prefs(context)


def _client(context, token=None):
    from ..planscape.client import PlanscapeClient
    p = _prefs(context)
    return PlanscapeClient(p.server_url, token=token if token is not None else (p.api_token or None))


def _active_ifc():
    from ..core.bonsai import bonsai
    return bonsai.active_ifc()


def _host_id_fn():
    from ..core.bonsai import bonsai
    return bonsai.host_element_id


def _blend_dir() -> Path:
    path = bpy.data.filepath
    return Path(path).parent if path else Path(os.path.expanduser("~"))


def _local_issues_path() -> Path:
    return _blend_dir() / "_BIM_COORD" / "issues.json"


# ---------------------------------------------------------------------------
# 14 — Planscape Login
# ---------------------------------------------------------------------------

class StingPlanscapeLoginOperator(bpy.types.Operator):
    """Authenticate with Planscape Server (email+password from prefs) and store a token."""

    bl_idname = "sting.planscape_login"
    bl_label = "Planscape Login"
    bl_description = "Log in to Planscape Server using the email/password in STING preferences and store the access token"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        p = _prefs(context)
        if not p.server_url:
            self.report({"ERROR"}, "Server URL is empty — set it in STING preferences")
            return {"CANCELLED"}
        if not p.email or not p.password:
            self.report({"ERROR"}, "Email and password are required in STING preferences")
            return {"CANCELLED"}

        from ..planscape.client import PlanscapeClient, PlanscapeError
        try:
            client = PlanscapeClient(p.server_url)
            token, resp = client.login(p.email, p.password)
        except PlanscapeError as e:
            self.report({"ERROR"}, f"Login failed: {e}")
            return {"CANCELLED"}

        p.api_token = token
        # Clear the stored password once exchanged for a token.
        p.password = ""
        context.scene[_TOKEN_KEY] = token
        context.scene[_EMAIL_KEY] = p.email
        self.report({"INFO"}, f"Logged in as {resp.get('userName') or p.email}")

        # Phase A4 — substrate drift-check. Warn (never block) if this host
        # reads a different shared/ifc vocabulary than the server.
        self._check_substrate_drift(context, client)
        return {"FINISHED"}

    def _check_substrate_drift(self, context, client) -> None:
        """Compare local substrate hash with the server's; warn on mismatch."""
        try:
            from stingtools_core.planscape.client import check_substrate_drift
        except ImportError:
            # stingtools-core not importable in this Blender env — skip silently.
            return
        try:
            ok, message = check_substrate_drift(client)
        except Exception as e:  # noqa: BLE001 — drift-check must never break login
            print(f"[STING] substrate drift-check skipped: {e}")
            return
        if not ok:
            self.report({"WARNING"}, message)
            context.scene["sting_substrate_drift"] = message
        print(f"[STING] substrate: {message}")


# ---------------------------------------------------------------------------
# 15 — Push to Planscape (host=bonsai)
# ---------------------------------------------------------------------------

class StingSyncToPlanscapeOperator(bpy.types.Operator):
    """Push every IFC element (keyed on GlobalId) to Planscape as host=bonsai."""

    bl_idname = "sting.sync_to_planscape"
    bl_label = "Push to Planscape (bonsai)"
    bl_description = "Upload IFC elements (GlobalId + Pset_StingTags) to Planscape as host=bonsai for cross-host coordination"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        p = _prefs(context)
        if not p.api_token:
            self.report({"ERROR"}, "Not logged in — use 'Planscape Login' first")
            return {"CANCELLED"}
        if not p.project_id:
            self.report({"ERROR"}, "No Project ID set in STING preferences")
            return {"CANCELLED"}

        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai (File → IFC → Open) first")
            return {"CANCELLED"}

        from ..planscape import ingest
        from ..planscape.client import PlanscapeError

        try:
            elements = ingest.collect_elements(model, host_id_fn=_host_id_fn())
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"Cannot collect IFC elements: {e}")
            return {"CANCELLED"}

        if not elements:
            self.report({"WARNING"}, "No IFC elements with a GlobalId found to push")
            return {"CANCELLED"}

        doc_guid = ingest.document_guid(model)
        client = _client(context)
        try:
            resp = client.ingest_ifc(
                p.project_id, HOST, elements,
                host_document_guid=doc_guid,
                plugin_version="stingtools-bonsai/0.1.0",
                user_name=context.scene.get(_EMAIL_KEY, "") or "",
            )
        except PlanscapeError as e:
            self.report({"ERROR"}, f"Push failed: {e}")
            return {"CANCELLED"}

        new_m = resp.get("newMappings", 0)
        upd_m = resp.get("updatedMappings", 0)
        new_e = resp.get("newElements", 0)
        upd_e = resp.get("updatedElements", 0)
        skipped = resp.get("skipped", 0)
        self.report(
            {"INFO"},
            f"Pushed {len(elements)} element(s): {new_m + upd_m} mappings "
            f"({new_m} new), {new_e + upd_e} elements, {skipped} skipped",
        )
        print(f"[STING] bonsai push response: {json.dumps(resp)}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 16 — Raise Issue (best-effort; offline fallback)
# ---------------------------------------------------------------------------

_PRIORITY_ITEMS = [
    ("LOW", "Low", ""),
    ("MEDIUM", "Medium", ""),
    ("HIGH", "High", ""),
    ("CRITICAL", "Critical", ""),
]


class StingRaiseIssueOperator(bpy.types.Operator):
    """Create a BIM issue on Planscape Server (or save locally if offline)."""

    bl_idname = "sting.raise_issue"
    bl_label = "Raise Issue"
    bl_description = "Log a BIM coordination issue, linked to the selected element's GlobalId if possible"
    bl_options = {"REGISTER"}

    title: bpy.props.StringProperty(name="Title", default="")  # type: ignore[valid-type]
    description: bpy.props.StringProperty(name="Description", default="")  # type: ignore[valid-type]
    priority: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Priority", items=_PRIORITY_ITEMS, default="MEDIUM",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self, width=400)

    def execute(self, context: bpy.types.Context) -> set[str]:
        if not self.title:
            self.report({"ERROR"}, "Issue title is required")
            return {"CANCELLED"}

        element_global_id = ""
        model = _active_ifc()
        if model is not None:
            from ..core.bonsai import bonsai
            el = bonsai.element_for_object(context.active_object)
            if el is not None:
                element_global_id = getattr(el, "GlobalId", "") or ""

        issue = {
            "title": self.title,
            "description": self.description,
            "priority": self.priority,
            "elementGlobalId": element_global_id,
            "status": "OPEN",
        }

        p = _prefs(context)
        if p.api_token and p.project_id:
            from ..planscape.client import PlanscapeError
            try:
                client = _client(context)
                client._request("POST", f"/api/projects/{p.project_id}/issues", body=issue)
                self.report({"INFO"}, f"Issue raised on Planscape: {self.title}")
                return {"FINISHED"}
            except PlanscapeError as e:
                print(f"[STING] Planscape issue raise failed ({e}) — saving locally")

        # Offline fallback.
        issue["created_at"] = datetime.datetime.utcnow().isoformat() + "Z"
        issue["source"] = "bonsai_offline"
        path = _local_issues_path()
        try:
            existing = json.loads(path.read_text()) if path.exists() else []
        except Exception:  # noqa: BLE001
            existing = []
        existing.append(issue)
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(existing, indent=2))
            self.report({"INFO"}, f"Issue saved locally: {path}")
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"Could not save issue locally: {e}")
            return {"CANCELLED"}
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# registration
# ---------------------------------------------------------------------------

CLASSES = (
    StingPlanscapeLoginOperator,
    StingSyncToPlanscapeOperator,
    StingRaiseIssueOperator,
)
