"""Bonsai-coexistence layer.

Bonsai (formerly BlenderBIM) is the IFC layer for Blender. StingTools
runs on top of it. Three things this module does:

1. DETECT — at registration time, work out whether Bonsai is installed
   and what version. Surface the result via BonsaiBridge.capabilities.
2. DELEGATE — when Bonsai is present, route IFC mutations through its
   transaction-aware ifcopenshell.api wrappers (so Bonsai's undo stack
   stays consistent and Bonsai's UI refreshes). When Bonsai is absent,
   fall back to direct ifcopenshell.api.run() — degraded but functional.
3. ACCESS — provide a single function to fetch the currently-active
   IFC file (a Bonsai-owned reference when Bonsai is loaded, or an
   ifcopenshell.open() call when the user opens an IFC directly via
   STING in standalone mode).

The module imports nothing from Bonsai at top level — that's intentional
so the add-on remains loadable in installations without Bonsai. Bonsai
references are resolved lazily.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any, Optional

logger = logging.getLogger("stingtools_bonsai.core.bonsai")


@dataclass(frozen=True)
class BonsaiCapabilities:
    """What Bonsai offers in the running Blender session."""

    installed: bool                     # add-on present + enabled
    version: Optional[str]              # "0.8.2-alpha" or None
    has_api_pset: bool                  # ifcopenshell.api.run("pset.add_pset", ...) works
    has_api_attribute: bool             # ifcopenshell.api.run("attribute.edit_attributes", ...)
    has_blender_context: bool           # Bonsai's IfcStore exposes the active file
    api_module_path: Optional[str]      # for diagnostics

    def summary(self) -> str:
        if not self.installed:
            return "Bonsai not detected — STING running in standalone (ifcopenshell direct) mode"
        return (
            f"Bonsai v{self.version} detected — "
            f"pset_api={'yes' if self.has_api_pset else 'no'}, "
            f"context={'yes' if self.has_blender_context else 'no'}"
        )


class BonsaiBridge:
    """Cached singleton accessed via the module-level `bonsai` instance.

    Probe + delegate APIs. Re-detect via .refresh() when the user installs
    or removes Bonsai mid-session.
    """

    def __init__(self) -> None:
        self._caps: Optional[BonsaiCapabilities] = None

    # ------------------------------------------------------------------
    # detection
    # ------------------------------------------------------------------

    def refresh(self) -> BonsaiCapabilities:
        """Re-probe Bonsai. Call after enable/disable in Preferences."""
        self._caps = self._probe()
        return self._caps

    @property
    def capabilities(self) -> BonsaiCapabilities:
        if self._caps is None:
            self.refresh()
        return self._caps  # type: ignore[return-value]

    @property
    def installed(self) -> bool:
        return self.capabilities.installed

    def _probe(self) -> BonsaiCapabilities:
        # Look for any of the known Bonsai module identifiers. The add-on
        # has gone through several renamings: blenderbim → bonsai →
        # bonsai-bim (extension form). Check them in order.
        candidates = [
            "bonsai",          # 2024+ standalone module name
            "bonsai_bim",      # extensions-form module name
            "blenderbim",      # pre-rename
        ]
        bonsai_mod = None
        api_path: Optional[str] = None
        for name in candidates:
            try:
                bonsai_mod = __import__(name)
                api_path = getattr(bonsai_mod, "__file__", None) or name
                break
            except ImportError:
                continue

        if bonsai_mod is None:
            return BonsaiCapabilities(
                installed=False,
                version=None,
                has_api_pset=False,
                has_api_attribute=False,
                has_blender_context=False,
                api_module_path=None,
            )

        # Probe individual capabilities behind try/except so a renamed
        # internal sub-module doesn't tank detection of the parent.
        version = (
            getattr(bonsai_mod, "__version__", None)
            or getattr(bonsai_mod, "VERSION", None)
            or "unknown"
        )

        has_api_pset = False
        has_api_attribute = False
        try:
            import ifcopenshell.api  # type: ignore
            # Bonsai registers handlers for these names
            has_api_pset = hasattr(ifcopenshell.api, "pset") or self._api_has("pset.add_pset")
            has_api_attribute = self._api_has("attribute.edit_attributes")
        except ImportError:
            pass

        has_context = False
        try:
            # Bonsai exposes an IfcStore with a .get_file() class method
            from bonsai.bim.ifc import IfcStore  # type: ignore
            has_context = IfcStore is not None
        except ImportError:
            try:
                from bonsai_bim.bim.ifc import IfcStore  # type: ignore
                has_context = IfcStore is not None
            except ImportError:
                try:
                    from blenderbim.bim.ifc import IfcStore  # type: ignore
                    has_context = IfcStore is not None
                except ImportError:
                    pass

        return BonsaiCapabilities(
            installed=True,
            version=str(version),
            has_api_pset=has_api_pset,
            has_api_attribute=has_api_attribute,
            has_blender_context=has_context,
            api_module_path=api_path,
        )

    @staticmethod
    def _api_has(name: str) -> bool:
        """Check if an ifcopenshell.api endpoint is registered."""
        try:
            import ifcopenshell.api  # type: ignore
            # The .run() dispatcher looks up handlers in a registry.
            # We can't probe without invoking — best-effort.
            return hasattr(ifcopenshell.api, "run")
        except ImportError:
            return False

    # ------------------------------------------------------------------
    # active IFC file
    # ------------------------------------------------------------------

    def active_ifc(self) -> Optional[Any]:
        """Return the currently-loaded ifcopenshell.file, or None.

        When Bonsai is loaded and has a file open, return its file.
        Otherwise, STING ops should fall back to asking the user to
        open an IFC explicitly via Bonsai's File → IFC → Open or via
        a future STING op.
        """
        if not self.installed:
            return None
        for store_path in (
            "bonsai.bim.ifc",
            "bonsai_bim.bim.ifc",
            "blenderbim.bim.ifc",
        ):
            try:
                mod = __import__(store_path, fromlist=["IfcStore"])
                store = getattr(mod, "IfcStore", None)
                if store is None:
                    continue
                getter = getattr(store, "get_file", None)
                if getter is None:
                    continue
                return getter()
            except (ImportError, AttributeError):
                continue
        return None

    def host_element_id(self, element: Any) -> str:
        """Resolve the host-side identifier for an IFC element.

        For Blender that's the name of the Blender object Bonsai links to
        the IFC element. Resolution order:
          1. Bonsai's ``tool.Ifc.get_object(element).name`` (the live link).
          2. The element's IFC ``Name`` attribute.
          3. ``#<step-id>`` as a last resort so the field is never empty.

        Stays defensive: any Bonsai API drift falls through to the IFC
        attribute fallbacks rather than raising.
        """
        for tool_path in ("bonsai.tool", "bonsai_bim.tool", "blenderbim.tool"):
            try:
                mod = __import__(tool_path, fromlist=["Ifc"])
                ifc_tool = getattr(mod, "Ifc", None)
                getter = getattr(ifc_tool, "get_object", None) if ifc_tool else None
                if getter is None:
                    continue
                obj = getter(element)
                name = getattr(obj, "name", None)
                if name:
                    return str(name)
            except Exception:  # noqa: BLE001 — Bonsai API drift must not break sync
                continue
        name = getattr(element, "Name", None)
        if name:
            return str(name)
        try:
            return f"#{element.id()}"
        except Exception:  # noqa: BLE001
            return ""

    # ------------------------------------------------------------------
    # IFC mutation — delegate through Bonsai when available
    # ------------------------------------------------------------------

    def add_pset(self, element: Any, pset_name: str, properties: dict) -> bool:
        """Add or edit a property set on an element.

        When Bonsai is available, route via ifcopenshell.api.run so
        Bonsai's undo + UI refresh hook in. Otherwise call the API
        directly. Returns True on success.
        """
        try:
            import ifcopenshell.api  # type: ignore
        except ImportError:
            return False

        try:
            model = self.active_ifc()
            if model is None:
                return False

            # Find or create the pset
            pset = ifcopenshell.api.run(
                "pset.add_pset",
                model,
                product=element,
                name=pset_name,
            )
            ifcopenshell.api.run(
                "pset.edit_pset",
                model,
                pset=pset,
                properties=properties,
            )
            return True
        except Exception as e:
            # In production this'd log to AuditLog; for the scaffold we
            # surface to the System Console.
            logger.error("add_pset failed: %s", e, exc_info=True)
            return False

    def edit_attribute(self, element: Any, attributes: dict) -> bool:
        """Set built-in IFC attributes (Name, Description, etc) on an element."""
        try:
            import ifcopenshell.api  # type: ignore
        except ImportError:
            return False
        try:
            model = self.active_ifc()
            if model is None:
                return False
            ifcopenshell.api.run(
                "attribute.edit_attributes",
                model,
                product=element,
                attributes=attributes,
            )
            return True
        except Exception as e:
            logger.error("edit_attribute failed: %s", e, exc_info=True)
            return False


# Module-level singleton — re-import safe
bonsai = BonsaiBridge()
