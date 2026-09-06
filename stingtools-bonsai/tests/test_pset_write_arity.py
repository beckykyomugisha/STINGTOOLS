"""Regression tests for the Pset-write helpers in the new operator modules.

Guards the bug where spatial_ops / repair_ops / mep_ops called
``BonsaiBridge.add_pset(ifc, element, pset_name, props)`` with four arguments
against a three-argument signature. The resulting TypeError was swallowed by a
bare ``except``, so every one of those ten operators reported success while
writing nothing to the IFC.
"""
import importlib
import importlib.util
import os
import pathlib
import sys
import types
from unittest.mock import MagicMock

import pytest

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

#: The add-on directory is "stingtools-bonsai" — a hyphen, so not importable as a
#: module name. The operator modules use package-relative imports (``..core.bonsai``),
#: which only resolve when the add-on is loaded as a package. Bind it to this
#: synthetic name, mirroring how Blender loads it as bl_ext.user_default.stingtools_bonsai.
PKG = "stingtools_bonsai"


def _stub_bpy():
    """Minimal bpy so the operator modules import outside Blender."""
    if "bpy" in sys.modules:
        return
    bpy = types.ModuleType("bpy")
    bpy.types = types.SimpleNamespace(Operator=type("Operator", (), {}), Panel=object, Context=object)
    _p = lambda **kw: None  # noqa: E731
    bpy.props = types.SimpleNamespace(
        BoolProperty=_p, StringProperty=_p, EnumProperty=_p, IntProperty=_p,
        FloatProperty=_p, PointerProperty=_p, CollectionProperty=_p,
        FloatVectorProperty=_p,
    )
    bpy.utils = types.SimpleNamespace(register_class=lambda c: None, unregister_class=lambda c: None)
    bpy.app = types.SimpleNamespace(handlers=types.SimpleNamespace(load_post=[]))
    bpy.data = types.SimpleNamespace(filepath="")
    bpy.ops = types.SimpleNamespace()
    sys.modules["bpy"] = bpy


def _load_addon_package():
    """Import the add-on under PKG so ``..core.bonsai`` relative imports resolve."""
    if PKG in sys.modules:
        return sys.modules[PKG]
    spec = importlib.util.spec_from_file_location(
        PKG, ROOT / "__init__.py", submodule_search_locations=[str(ROOT)]
    )
    mod = importlib.util.module_from_spec(spec)
    sys.modules[PKG] = mod
    spec.loader.exec_module(mod)
    return mod


_stub_bpy()
_load_addon_package()

from core.bonsai import BonsaiBridge  # noqa: E402


# (module, helper name, expected pset name)
HELPERS = [
    (f"{PKG}.ops.spatial_ops", "_write_stag", "Pset_StingTags"),
    (f"{PKG}.ops.repair_ops", "_write_stag", "Pset_StingTags"),
    (f"{PKG}.ops.mep_ops", "_write_mep_pset", "Pset_StingMEP"),
]


@pytest.mark.parametrize("mod_name,helper_name,pset", HELPERS)
def test_helper_matches_add_pset_signature(mod_name, helper_name, pset, monkeypatch):
    """The helper must call add_pset(element, pset_name, properties) — 3 args."""
    mod = importlib.import_module(mod_name)
    helper = getattr(mod, helper_name)

    seen = {}

    def fake_add_pset(element, pset_name, properties):
        seen["element"] = element
        seen["pset_name"] = pset_name
        seen["properties"] = properties
        return True

    # core/__init__.py rebinds the name `core.bonsai` to the singleton instance,
    # shadowing the submodule — reach the module through sys.modules.
    importlib.import_module(f"{PKG}.core.bonsai")
    bridge_mod = sys.modules[f"{PKG}.core.bonsai"]
    monkeypatch.setattr(bridge_mod.bonsai, "add_pset", fake_add_pset)

    element = MagicMock()
    result = helper(element, {"Discipline": "M"})

    assert result is True, f"{mod_name}.{helper_name} must return add_pset's result"
    assert seen["element"] is element
    assert seen["pset_name"] == pset
    assert seen["properties"] == {"Discipline": "M"}


def test_add_pset_rejects_a_model_argument():
    """Pin the real signature the helpers must match: (element, pset_name, properties)."""
    import inspect

    params = list(inspect.signature(BonsaiBridge.add_pset).parameters)
    assert params == ["self", "element", "pset_name", "properties"]
