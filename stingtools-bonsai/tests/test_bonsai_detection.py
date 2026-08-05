"""Regression test for Bonsai detection across packaging forms.

The bug: `_probe` only tried the top-level names `bonsai` / `bonsai_bim` /
`blenderbim`. On Blender 4.2 Bonsai installs as an EXTENSION, importable only
as `bl_ext.<repo>.bonsai`, so detection failed and the panel showed
"Bonsai is required" even with Bonsai 0.8.4 enabled. The fix discovers the
extension namespace from the enabled add-ons via `_bonsai_prefixes()`.
"""
from __future__ import annotations

import importlib
import importlib.util
import pathlib
import sys
import types

ROOT = pathlib.Path(__file__).resolve().parents[1]  # stingtools-bonsai/
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

PKG = "stingtools_bonsai"


def _stub_bpy_with_addons(addon_keys):
    """Install/refresh a minimal bpy whose preferences.addons.keys() is known."""
    bpy = sys.modules.get("bpy") or types.ModuleType("bpy")
    bpy.types = getattr(bpy, "types", types.SimpleNamespace(
        Operator=type("Operator", (), {}), Panel=object, Context=object))
    bpy.context = types.SimpleNamespace(
        preferences=types.SimpleNamespace(
            addons=types.SimpleNamespace(keys=lambda: list(addon_keys))))
    sys.modules["bpy"] = bpy
    return bpy


def _load_addon_package():
    if PKG in sys.modules:
        return sys.modules[PKG]
    spec = importlib.util.spec_from_file_location(
        PKG, ROOT / "__init__.py", submodule_search_locations=[str(ROOT)])
    mod = importlib.util.module_from_spec(spec)
    sys.modules[PKG] = mod
    spec.loader.exec_module(mod)
    return mod


_stub_bpy_with_addons([])
_load_addon_package()

from core.bonsai import _bonsai_prefixes, BonsaiBridge  # noqa: E402


def test_prefixes_discover_the_bl_ext_extension_namespace():
    """The exact fix: Bonsai's 4.2 extension key is discovered and tried first,
    while the legacy top-level names remain as fallback."""
    key = "bl_ext.user_default.bonsai"
    _stub_bpy_with_addons([key, "bl_ext.user_default.stingtools_bonsai", "cycles"])
    prefixes = _bonsai_prefixes()
    assert key in prefixes, "Bonsai's extension namespace must be discovered"
    assert prefixes[0] == key, "the extension form should be tried before legacy names"
    assert "bonsai" in prefixes and "blenderbim" in prefixes, "legacy fallbacks kept"


def test_prefixes_are_just_legacy_names_when_no_extension_present():
    _stub_bpy_with_addons(["cycles", "node_wrangler"])
    prefixes = _bonsai_prefixes()
    assert prefixes == ["bonsai", "bonsai_bim", "blenderbim"]


def test_probe_detects_bonsai_installed_as_an_extension():
    """End to end: with a fake bl_ext.user_default.bonsai in sys.modules, the
    bridge reports installed=True (before the fix it was False — 'required')."""
    key = "bl_ext.user_default.bonsai"
    _stub_bpy_with_addons([key])

    # Build the fake extension package tree the probe imports.
    created = []
    for name in (key, f"{key}.bim", f"{key}.bim.ifc", f"{key}.tool"):
        m = types.ModuleType(name)
        sys.modules[name] = m
        created.append(name)
    sys.modules[key].__version__ = "0.8.4"
    sys.modules[f"{key}.bim.ifc"].IfcStore = type("IfcStore", (), {"get_file": staticmethod(lambda: None)})
    sys.modules[f"{key}.tool"].Ifc = type("Ifc", (), {"run": staticmethod(lambda *a, **k: None)})

    try:
        caps = BonsaiBridge().refresh()
        assert caps.installed is True, "Bonsai-as-extension must be detected as installed"
        assert caps.version == "0.8.4"
        assert caps.has_blender_context is True, "IfcStore must resolve under the extension prefix"
    finally:
        for name in created:
            sys.modules.pop(name, None)


def test_reexported_bonsai_is_the_bridge_instance_not_a_wrapper():
    """Guards the panel bug: `from ..core import bonsai` re-exports the
    BonsaiBridge INSTANCE, so the panel must read `_bridge.capabilities`
    directly. `_bridge.bonsai.capabilities` (the old code) hit a nonexistent
    attribute and the panel's bare except hid it → 'Bonsai is required' forever."""
    from core import bonsai as _bridge
    assert hasattr(_bridge, "capabilities"), "re-export must expose .capabilities"
    assert hasattr(_bridge, "refresh"), "re-export must be the instance (has .refresh)"
    assert not hasattr(_bridge, "bonsai"), (
        "the instance has no .bonsai attribute — panel code accessing "
        "_bridge.bonsai.capabilities is the bug this test guards"
    )


if __name__ == "__main__":
    import traceback
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    fails = 0
    for t in tests:
        try:
            t()
            print(f"  OK   {t.__name__}")
        except Exception:
            fails += 1
            print(f"  FAIL {t.__name__}")
            traceback.print_exc()
    print(f"\n{len(tests)} tests, {fails} failures")
    sys.exit(1 if fails else 0)
