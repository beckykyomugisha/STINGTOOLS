"""Every STING property set the add-on writes is declared, and written as declared.

DSCH-38 follow-up. ops/mep_ops.py wrote Pset_StingMEP, a set no template in
shared/ifc/psets declared, so nothing could validate it and other hosts could not
know its properties. It is now declared (shared/ifc/psets/Pset_StingMEP.xml) under
the property names the add-on already used, so files it wrote stay readable.

These tests fail when the add-on writes or reads a Pset_Sting* set or property
the contract does not declare, or writes a number as text.
"""
import ast
import importlib
import pathlib
import re
import sys
import types
from unittest.mock import MagicMock
from xml.etree import ElementTree as ET

import pytest

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent
PSETS = REPO / "shared" / "ifc" / "psets"
NS = "{https://stingtools.io/schema/ifc/psets/v1}"

# Reuse the package loader and bpy stub of the arity tests.
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import test_pset_write_arity as _arity  # noqa: E402

PKG = _arity.PKG


def _contract():
    out = {}
    for f in PSETS.glob("Pset_Sting*.xml"):
        root = ET.parse(f).getroot()
        name = root.find(f"{NS}Identity/{NS}Name").text.strip()
        out[name] = {
            p.attrib["name"]: (p.findtext(f"{NS}DataType") or "IfcLabel").strip()
            for p in root.find(f"{NS}Properties").findall(f"{NS}Property")
        }
    return out


CONTRACT = _contract()
SOURCES = sorted(p for p in ROOT.rglob("*.py") if "_vendor" not in p.parts and "tests" not in p.parts)


def test_contract_is_loaded():
    assert "Pset_StingTags" in CONTRACT and "Pset_StingMEP" in CONTRACT, sorted(CONTRACT)


def test_every_sting_pset_name_in_the_addon_is_declared():
    used = {}
    for src in SOURCES:
        for m in re.finditer(r"[\"'](Pset_Sting[A-Za-z0-9_]+)[\"']", src.read_text(encoding="utf-8")):
            used.setdefault(m.group(1), src.relative_to(ROOT).as_posix())
    assert used, "no Pset_Sting* names found - the scan is broken"
    missing = {n: f for n, f in used.items() if n not in CONTRACT}
    assert not missing, f"undeclared Pset_Sting* sets (add shared/ifc/psets/<name>.xml): {missing}"


def _mep_keys():
    """Property names mep_ops writes (dicts passed to _write_mep_pset) and reads (mep.get)."""
    tree = ast.parse((ROOT / "ops" / "mep_ops.py").read_text(encoding="utf-8"))
    written, read = set(), set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "_write_mep_pset":
            for arg in node.args:
                if isinstance(arg, ast.Dict):
                    written |= {k.value for k in arg.keys if isinstance(k, ast.Constant)}
        if (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "get"
                and isinstance(node.func.value, ast.Name) and node.func.value.id == "mep"
                and node.args and isinstance(node.args[0], ast.Constant)):
            read.add(node.args[0].value)
    return written, read


def test_every_pset_stingmep_property_the_addon_uses_is_declared():
    written, read = _mep_keys()
    assert written and read, "the AST scan found nothing - it is broken"
    undeclared = (written | read) - set(CONTRACT["Pset_StingMEP"])
    assert not undeclared, f"Pset_StingMEP properties used by ops/mep_ops.py but not declared: {sorted(undeclared)}"


PY_TYPE = {"IfcReal": float, "IfcInteger": int, "IfcLabel": str, "IfcText": str, "IfcIdentifier": str}


def _run_operator(cls_name, elements, monkeypatch):
    mod = importlib.import_module(f"{PKG}.ops.mep_ops")
    written = []
    monkeypatch.setattr(mod, "_write_mep_pset", lambda el, props: written.append(props) or True)
    fake_ifc = MagicMock()
    fake_ifc.by_type.side_effect = lambda t: elements.get(t, [])
    monkeypatch.setattr(mod, "_get_ifc", lambda: fake_ifc)
    op = getattr(mod, cls_name).__new__(getattr(mod, cls_name))
    op.report = lambda *a, **k: None
    assert op.execute(MagicMock()) == {"FINISHED"}
    return written


@pytest.mark.parametrize("cls_name,ifc_class,attrs", [
    ("StingCalcPipeFlowOperator", "IfcPipeSegment", {}),
    ("StingCalcDrainageUnitsOperator", "IfcSanitaryTerminal", {"PredefinedType": "WC", "Name": "WC"}),
    ("StingCalcConduitFillOperator", "IfcCableCarrierSegment", {}),
])
def test_mep_operators_write_values_of_the_declared_type(cls_name, ifc_class, attrs, monkeypatch):
    pytest.importorskip("ifcopenshell")
    el = MagicMock(**attrs)
    written = _run_operator(cls_name, {ifc_class: [el]}, monkeypatch)
    assert written, f"{cls_name} wrote nothing"
    declared = CONTRACT["Pset_StingMEP"]
    for props in written:
        for k, v in props.items():
            want = PY_TYPE[declared[k]]
            ok = isinstance(v, want) and not isinstance(v, bool)
            assert ok, f"{cls_name} wrote {k}={v!r} ({type(v).__name__}); Pset_StingMEP declares {declared[k]}"
