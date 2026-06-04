"""Add-on preferences — Planscape Server connection settings.

Stores the server URL, API token, and project id used by the
sync-to-Planscape operator. Lives in Blender's add-on preferences
(Edit → Preferences → Add-ons → StingTools for Bonsai) so the values
persist across sessions without being committed to the .blend file.

``bl_idname`` MUST equal the add-on's package name. ``__package__`` of
this submodule resolves to that package name for both legacy-addon
installs (``stingtools-bonsai``) and the extensions system
(``bl_ext.<repo>.stingtools_bonsai``), so we never hardcode it.

NOTE: this module deliberately does NOT do ``from __future__ import
annotations``. Blender reads ``cls.__annotations__`` at registration
time expecting live ``bpy.props`` descriptor objects; PEP-563 would
stringify them and the properties would silently fail to register.
"""

import bpy


class StingAddonPreferences(bpy.types.AddonPreferences):
    bl_idname = __package__

    server_url: bpy.props.StringProperty(
        name="Server URL",
        description="Base URL of the Planscape Server, e.g. https://planscape.example.com",
        default="http://localhost:5000",
    )
    api_token: bpy.props.StringProperty(
        name="API Token",
        description="Bearer access token for the Planscape Server (from /api/auth/login)",
        default="",
        subtype="PASSWORD",
    )
    project_id: bpy.props.StringProperty(
        name="Project ID",
        description="Planscape project GUID to ingest IFC data into",
        default="",
    )
    email: bpy.props.StringProperty(
        name="Email",
        description="Planscape login email. Used by 'Planscape Login' to fetch a token.",
        default="",
    )
    password: bpy.props.StringProperty(
        name="Password",
        description="Planscape login password (used once to fetch a token; not required if a token is set above)",
        default="",
        subtype="PASSWORD",
    )

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout
        col = layout.column()
        col.prop(self, "server_url")
        col.separator()
        col.label(text="Login (fetches a token)", icon="URL")
        col.prop(self, "email")
        col.prop(self, "password")
        col.operator("sting.planscape_login", icon="URL")
        col.separator()
        col.prop(self, "api_token")
        col.prop(self, "project_id")
        col.label(
            text="Token auto-filled by Login (POST /api/auth/login); project GUID from /api/projects.",
            icon="INFO",
        )


def get_prefs(context: bpy.types.Context) -> "StingAddonPreferences":
    """Fetch this add-on's preferences for the running session.

    Raises KeyError only if the add-on isn't registered, which can't
    happen from an operator the add-on itself registers.
    """
    return context.preferences.addons[__package__].preferences  # type: ignore[return-value]


def register() -> None:
    bpy.utils.register_class(StingAddonPreferences)


def unregister() -> None:
    bpy.utils.unregister_class(StingAddonPreferences)
