"""COORD operators — Planscape login, tag sync, and issue raising."""

from __future__ import annotations

import json
import os
from pathlib import Path

import bpy

_TOKEN_KEY = "sting_planscape_token"
_URL_KEY = "sting_planscape_url"
_PROJECT_KEY = "sting_planscape_project_id"
_EMAIL_KEY = "sting_planscape_email"

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


def _blend_dir() -> Path:
    """Directory of the open .blend file, or temp dir as fallback."""
    path = bpy.data.filepath
    if path:
        return Path(path).parent
    return Path(os.path.expanduser("~"))


def _local_issues_path() -> Path:
    return _blend_dir() / "_BIM_COORD" / "issues.json"


def _load_local_issues() -> list:
    p = _local_issues_path()
    if p.exists():
        try:
            return json.loads(p.read_text())
        except Exception:
            pass
    return []


def _save_local_issues(issues: list) -> None:
    p = _local_issues_path()
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(issues, indent=2))


# ---------------------------------------------------------------------------
# 14 — Planscape Login
# ---------------------------------------------------------------------------

class StingPlanscapeLoginOperator(bpy.types.Operator):
    """Authenticate with Planscape Server and cache the access token."""

    bl_idname = "sting.planscape_login"
    bl_label = "Planscape Login"
    bl_description = "Log in to Planscape Server and store an access token for this session"
    bl_options = {"REGISTER"}

    server_url: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Server URL",
        default="https://api.planscape.io",
    )
    email: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Email",
        default="",
    )
    password: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Password",
        subtype="PASSWORD",
        default="",
    )
    project_id: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Project ID",
        description="Planscape project UUID — find it in the project URL",
        default="",
    )

    def invoke(self, context: bpy.types.Context, event):
        # Pre-fill from stored values if available
        self.server_url = context.scene.get(_URL_KEY, self.server_url)
        self.email = context.scene.get(_EMAIL_KEY, self.email)
        return context.window_manager.invoke_props_dialog(self, width=400)

    def execute(self, context: bpy.types.Context) -> set[str]:
        if not self.email or not self.password:
            self.report({"ERROR"}, "Email and password are required")
            return {"CANCELLED"}

        try:
            from stingtools_core.planscape.client import PlanscapeClient  # type: ignore
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools_core not available: {e}")
            return {"CANCELLED"}

        try:
            client = PlanscapeClient(self.server_url)
            access_token, _ = client.login(self.email, self.password)
        except Exception as e:
            self.report({"ERROR"}, f"Login failed: {e}")
            return {"CANCELLED"}

        context.scene[_TOKEN_KEY] = access_token
        context.scene[_URL_KEY] = self.server_url
        context.scene[_EMAIL_KEY] = self.email
        if self.project_id:
            context.scene[_PROJECT_KEY] = self.project_id

        self.report({"INFO"}, f"Logged in as {self.email}")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 15 — Sync Tags to Planscape
# ---------------------------------------------------------------------------

class StingSyncToPlanscapeOperator(bpy.types.Operator):
    """Push all IFC element tags to Planscape Server via the IFC ingest endpoint."""

    bl_idname = "sting.sync_to_planscape"
    bl_label = "Sync Tags to Planscape"
    bl_description = "Upload Pset_StingTags from the active IFC to Planscape Server"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        token = context.scene.get(_TOKEN_KEY)
        if not token:
            self.report({"ERROR"}, "Not logged in — use 'Planscape Login' first")
            return {"CANCELLED"}

        project_id = context.scene.get(_PROJECT_KEY, "")
        if not project_id:
            self.report({"ERROR"}, "No Project ID stored — log in again and enter your Project ID")
            return {"CANCELLED"}

        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        try:
            from stingtools_core.planscape.client import PlanscapeClient  # type: ignore
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools_core not available: {e}")
            return {"CANCELLED"}

        # Collect elements
        elements_payload = []
        try:
            for el in model.by_type("IfcElement"):
                pset = _get_pset(el, "Pset_StingTags")
                global_id = getattr(el, "GlobalId", "") or ""
                elements_payload.append({
                    "ifc_global_id": global_id,
                    "ifc_class": el.is_a(),
                    "pset_sting_tags": pset,
                    "host": "blender",
                    "host_document_guid": getattr(model, "schema", "IFC4"),
                })
        except Exception as e:
            self.report({"ERROR"}, f"Cannot collect IFC elements: {e}")
            return {"CANCELLED"}

        if not elements_payload:
            self.report({"WARNING"}, "No IFC elements found to sync")
            return {"CANCELLED"}

        server_url = context.scene.get(_URL_KEY, "https://api.planscape.io")
        client = PlanscapeClient(server_url, access_token=token)

        try:
            # POST to /api/projects/{id}/ifc/data
            response = client._request(
                "POST",
                f"/api/projects/{project_id}/ifc/data",
                body={
                    "elements": elements_payload,
                    "host": "blender",
                },
            )
            synced = response.get("synced", len(elements_payload))
            self.report({"INFO"}, f"Synced {synced} element(s) to Planscape")
        except Exception as e:
            self.report({"ERROR"}, f"Sync failed: {e}")
            return {"CANCELLED"}

        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 16 — Raise Issue
# ---------------------------------------------------------------------------

_PRIORITY_ITEMS = [
    ("LOW",      "Low",      ""),
    ("MEDIUM",   "Medium",   ""),
    ("HIGH",     "High",     ""),
    ("CRITICAL", "Critical", ""),
]


class StingRaiseIssueOperator(bpy.types.Operator):
    """Create a BIM issue on Planscape Server (or save locally if offline)."""

    bl_idname = "sting.raise_issue"
    bl_label = "Raise Issue"
    bl_description = "Log a BIM coordination issue, linked to the selected element if possible"
    bl_options = {"REGISTER"}

    title: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Title",
        default="",
    )
    description: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Description",
        default="",
    )
    priority: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Priority",
        items=_PRIORITY_ITEMS,
        default="MEDIUM",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self, width=400)

    def execute(self, context: bpy.types.Context) -> set[str]:
        if not self.title:
            self.report({"ERROR"}, "Issue title is required")
            return {"CANCELLED"}

        # Try to get the GlobalId of the active/selected element
        element_global_id = ""
        model = _active_ifc()
        if model:
            obj = context.active_object
            if obj:
                props = getattr(obj, "BIMObjectProperties", None)
                ifc_id = getattr(props, "ifc_definition_id", 0) if props else 0
                if ifc_id:
                    try:
                        el = model.by_id(ifc_id)
                        element_global_id = getattr(el, "GlobalId", "") or ""
                    except Exception:
                        pass

        issue = {
            "title": self.title,
            "description": self.description,
            "priority": self.priority,
            "element_global_id": element_global_id,
            "status": "OPEN",
        }

        token = context.scene.get(_TOKEN_KEY)
        project_id = context.scene.get(_PROJECT_KEY, "")

        if token and project_id:
            # Try server
            try:
                from stingtools_core.planscape.client import PlanscapeClient  # type: ignore
                server_url = context.scene.get(_URL_KEY, "https://api.planscape.io")
                client = PlanscapeClient(server_url, access_token=token)
                client._request(
                    "POST",
                    f"/api/projects/{project_id}/issues",
                    body=issue,
                )
                self.report({"INFO"}, f"Issue raised on Planscape: {self.title}")
                return {"FINISHED"}
            except Exception as e:
                print(f"[STING] Planscape issue raise failed ({e}) — saving locally")

        # Offline fallback — save to _BIM_COORD/issues.json
        import datetime
        issue["created_at"] = datetime.datetime.utcnow().isoformat() + "Z"
        issue["source"] = "blender_offline"
        existing = _load_local_issues()
        existing.append(issue)
        try:
            _save_local_issues(existing)
            self.report({"INFO"}, f"Issue saved locally: {_local_issues_path()}")
        except Exception as e:
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
