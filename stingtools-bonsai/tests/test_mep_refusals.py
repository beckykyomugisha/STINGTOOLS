"""The MEP operators refuse an element whose inputs are missing (DSCH-38 follow-up).

They used to invent them: a pipe with no design flow was sized for 0.5 L/s, an
unrecognised sanitary terminal got 0.5 DU, and a conduit with no data was a 25 mm
conduit holding one 6 mm cable. Each of those wrote a plausible number into the IFC
that nobody had asked for. Now each element without its inputs is skipped, nothing is
written to it, and the operator's WARNING names it and what it lacks.
"""
import importlib
import pathlib
import sys
import types

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import test_pset_write_arity as _arity  # noqa: E402  (bpy stub + package loader)

PKG = _arity.PKG


def _el(gid, name="", ptype=None, mep=None):
    return types.SimpleNamespace(GlobalId=gid, Name=name, PredefinedType=ptype, _mep=dict(mep or {}))


def _run(cls_name, ifc_class, elements, monkeypatch):
    mod = importlib.import_module(f"{PKG}.ops.mep_ops")
    written, reports = {}, []
    monkeypatch.setattr(mod, "_write_mep_pset", lambda el, props: written.setdefault(el.GlobalId, {}).update(props) or True)
    monkeypatch.setattr(mod, "_get_mep_pset", lambda el: el._mep)
    fake = types.SimpleNamespace(by_type=lambda t: elements if t == ifc_class else [])
    monkeypatch.setattr(mod, "_get_ifc", lambda: fake)
    cls = getattr(mod, cls_name)
    op = cls.__new__(cls)
    op.report = lambda level, msg: reports.append((sorted(level)[0], msg))
    assert op.execute(None) == {"FINISHED"}
    return written, reports


def _warning(reports):
    w = [m for lvl, m in reports if lvl == "WARNING"]
    assert len(w) == 1, reports
    return w[0]


@pytest.fixture(autouse=True)
def _ifcopenshell():
    pytest.importorskip("ifcopenshell")


# ── pipe flow ────────────────────────────────────────────────────────────────

def test_pipe_without_design_flow_is_refused_not_sized_for_an_assumed_flow(monkeypatch):
    els = [_el("P-NOFLOW", "Branch A"), _el("P-BLANK", mep={"PLM_SUP_FLOW_LS": " "}),
           _el("P-ZERO", mep={"PLM_SUP_FLOW_LS": 0}), _el("P-OK", mep={"PLM_SUP_FLOW_LS": 0.5})]
    written, reports = _run("StingCalcPipeFlowOperator", "IfcPipeSegment", els, monkeypatch)
    assert set(written) == {"P-OK"}
    w = _warning(reports)
    assert "3 pipe segment(s) skipped" in w and "P-NOFLOW (Branch A)" in w and "P-BLANK" in w and "P-ZERO" in w
    assert "PLM_SUP_FLOW_LS" in w


def test_pipe_sizing_never_overwrites_the_design_flow(monkeypatch):
    els = [_el("P1", mep={"PLM_SUP_FLOW_LS": 0.5})]
    written, _ = _run("StingCalcPipeFlowOperator", "IfcPipeSegment", els, monkeypatch)
    assert "PLM_SUP_FLOW_LS" not in written["P1"]
    assert isinstance(written["P1"]["PLM_SUP_DN"], int)
    # the reported velocity is the design flow over the chosen bore
    import math
    dn = written["P1"]["PLM_SUP_DN"]
    assert written["P1"]["PLM_SUP_VEL_MS"] == pytest.approx(0.5e-3 / (math.pi * (dn / 2000.0) ** 2), abs=1e-3)
    assert written["P1"]["PLM_SUP_VEL_MS"] <= 3.0


def test_pipe_flow_beyond_the_largest_dn_is_refused_not_given_the_largest(monkeypatch):
    els = [_el("P-HUGE", mep={"PLM_SUP_FLOW_LS": 10000})]
    written, reports = _run("StingCalcPipeFlowOperator", "IfcPipeSegment", els, monkeypatch)
    assert written == {}
    assert "P-HUGE" in _warning(reports) and "DN300" in _warning(reports)


# ── drainage units ───────────────────────────────────────────────────────────

def test_unrecognised_sanitary_terminal_gets_no_default_du(monkeypatch):
    els = [_el("S-WC", "WC 1", ptype="WC"), _el("S-UNKNOWN", "Drinking fountain", ptype="NOTDEFINED")]
    written, reports = _run("StingCalcDrainageUnitsOperator", "IfcSanitaryTerminal", els, monkeypatch)
    assert written == {"S-WC": {"PLM_DRN_DU": 2.0}}
    w = _warning(reports)
    assert "1 sanitary terminal(s) skipped" in w and "S-UNKNOWN (Drinking fountain)" in w


# ── conduit fill ─────────────────────────────────────────────────────────────

FULL = {"ELC_CONDUIT_DN_MM": 25, "ELC_CABLE_COUNT": 3, "ELC_CABLE_OD_MM": 6}


@pytest.mark.parametrize("drop", ["ELC_CONDUIT_DN_MM", "ELC_CABLE_COUNT", "ELC_CABLE_OD_MM"])
def test_conduit_missing_any_input_is_refused_and_named(drop, monkeypatch):
    mep = {k: v for k, v in FULL.items() if k != drop}
    written, reports = _run("StingCalcConduitFillOperator", "IfcCableCarrierSegment",
                            [_el("C1", "Tray run 4", mep=mep)], monkeypatch)
    assert written == {}
    w = _warning(reports)
    assert "C1 (Tray run 4)" in w and drop in w


def test_conduit_with_no_data_at_all_is_not_a_25mm_conduit_with_one_cable(monkeypatch):
    written, reports = _run("StingCalcConduitFillOperator", "IfcCableCarrierSegment", [_el("C0")], monkeypatch)
    assert written == {}
    w = _warning(reports)
    assert all(k in w for k in FULL)


def test_conduit_with_all_inputs_is_checked(monkeypatch):
    written, _ = _run("StingCalcConduitFillOperator", "IfcCableCarrierSegment",
                      [_el("C2", mep=FULL), _el("C3", mep={**FULL, "ELC_CABLE_COUNT": 30})], monkeypatch)
    assert written["C2"]["ELC_FILL_STATUS"] == "OK"
    assert written["C2"]["ELC_FILL_PCT"] == pytest.approx(round(3 * 36 / 625 * 100, 1))
    assert written["C3"]["ELC_FILL_STATUS"] == "OVERLOADED"


def test_fractional_cable_count_is_refused(monkeypatch):
    written, reports = _run("StingCalcConduitFillOperator", "IfcCableCarrierSegment",
                            [_el("C4", mep={**FULL, "ELC_CABLE_COUNT": 2.5})], monkeypatch)
    assert written == {}
    assert "whole number" in _warning(reports)
