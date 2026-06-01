"""Raise a Planscape issue from Blender.

Opens an invoke dialog (title / description / type / priority), then POSTs to
the real server endpoint:

    POST /api/projects/{projectId}/issues   (IssuesController.CreateIssue)

The server validates Type against ^[A-Z]{2,6}$ (RFI / NCR / CLASH / DEFECT /
TQ / SI), so the type field is an enum of valid codes rather than free text.
Auth is JWT bearer + project membership; failures surface as operator errors.

When a Blender object with a linked IFC element is selected, its GlobalId is
attached as the issue's 3D anchor (ModelElementGuid) and as LinkedElementIds,
so the issue resolves cross-host via ExternalElementMapping.

NOTE: this module deliberately does NOT use ``from __future__ import
annotations``. Blender reads ``cls.__annotations__`` at registration time
expecting live ``bpy.props`` descriptor objects; PEP-563 would stringify the
EnumProperty / StringProperty annotations below and the dialog fields would
silently fail to register (same trap prefs.py guards against).
"""

import json

import bpy


class StingRaiseIssueOperator(bpy.types.Operator):
    """Raise a coordination issue on the configured Planscape project."""

    bl_idname = "sting.raise_issue"
    bl_label = "Raise Planscape Issue"
    bl_description = (
        "Create an issue (RFI / NCR / clash / defect) on the configured "
        "Planscape project. Attaches the selected element's IFC GlobalId when "
        "one is linked."
    )
    bl_options = {"REGISTER"}

    # Type codes the server accepts (^[A-Z]{2,6}$). Keep labels human, values
    # uppercase so the POST body never trips the server-side regex.
    issue_type: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Type",
        description="Issue type code",
        items=[
            ("RFI", "RFI — Request for Information", ""),
            ("NCR", "NCR — Non-Conformance", ""),
            ("CLASH", "Clash", ""),
            ("DEFECT", "Defect", ""),
            ("TQ", "TQ — Technical Query", ""),
            ("SI", "SI — Site Instruction", ""),
        ],
        default="RFI",
    )
    priority: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Priority",
        description="Issue priority",
        items=[
            ("LOW", "Low", ""),
            ("MEDIUM", "Medium", ""),
            ("HIGH", "High", ""),
            ("CRITICAL", "Critical", ""),
        ],
        default="MEDIUM",
    )
    title: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Title",
        description="Short issue title (required)",
        default="",
    )
    description: bpy.props.StringProperty(  # type: ignore[valid-type]
        name="Description",
        description="Optional longer description",
        default="",
    )

    def invoke(self, context: bpy.types.Context, event) -> set[str]:
        return context.window_manager.invoke_props_dialog(self, width=420)

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout
        layout.prop(self, "title")
        layout.prop(self, "issue_type")
        layout.prop(self, "priority")
        layout.prop(self, "description")
        # Show the element that would be attached, if any.
        gid = self._selected_global_id(context)
        box = layout.box()
        if gid:
            box.label(text=f"Attaching element {gid}", icon="LINKED")
        else:
            box.label(text="No IFC element selected — issue will be model-wide.", icon="INFO")

    def execute(self, context: bpy.types.Context) -> set[str]:
        if not (self.title or "").strip():
            self.report({"ERROR"}, "Title is required")
            return {"CANCELLED"}

        # ── preferences ────────────────────────────────────────────────
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

        # ── core client ────────────────────────────────────────────────
        try:
            from stingtools_core.planscape import PlanscapeClient
            from stingtools_core.exceptions import PlanscapeError
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools-core not available: {e}")
            return {"CANCELLED"}

        # ── build the issue body (camelCase binds to the PascalCase DTO;
        #    ASP.NET Web JSON is case-insensitive) ───────────────────────
        issue: dict = {
            "type": self.issue_type,             # ^[A-Z]{2,6}$ — guaranteed by the enum
            "title": self.title.strip(),
            "description": (self.description or "").strip() or None,
            "priority": self.priority,
            "source": "blender",
        }
        gid = self._selected_global_id(context)
        if gid:
            issue["modelElementGuid"] = gid               # 3D anchor (cross-host key)
            issue["linkedElementIds"] = json.dumps([gid])  # server expects a JSON array string

        # ── POST via the existing client.raise_issue ───────────────────
        client = PlanscapeClient(server_url, access_token=token or None, client_type="blender")
        try:
            resp = client.raise_issue(project_id, issue)
        except PlanscapeError as e:
            self.report({"ERROR"}, f"Issue raise failed: {e}")
            return {"CANCELLED"}
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"Unexpected error raising issue: {e}")
            return {"CANCELLED"}

        code = (resp or {}).get("issueCode") or (resp or {}).get("code") or (resp or {}).get("id") or ""
        msg = f"Raised {self.issue_type} issue" + (f" ({code})" if code else "")
        if gid:
            msg += f" on element {gid}"
        self.report({"INFO"}, msg)
        print(f"[STING] {msg}")
        return {"FINISHED"}

    @staticmethod
    def _selected_global_id(context: bpy.types.Context) -> str | None:
        """GlobalId of the active (or first selected) object's IFC element, or
        None when nothing IFC-linked is selected / Bonsai is absent."""
        try:
            from ..core import bonsai
        except Exception:  # noqa: BLE001
            return None
        candidates = []
        if getattr(context, "active_object", None) is not None:
            candidates.append(context.active_object)
        candidates.extend(getattr(context, "selected_objects", None) or [])
        seen = set()
        for obj in candidates:
            if obj is None or id(obj) in seen:
                continue
            seen.add(id(obj))
            el = bonsai.element_for_object(obj)
            gid = getattr(el, "GlobalId", None) if el is not None else None
            if gid:
                return str(gid)
        return None
