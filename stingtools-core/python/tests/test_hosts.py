"""Tests for the host adapter layer (Phase A3) — pure, no bpy / no ifcopenshell."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import pytest

from stingtools_core import HostAdapter, IfcFileHostAdapter, GeorefDescriptor, ChangeDelta, Tag
from stingtools_core.hosts import inference


# ── inference (extracted from the Bonsai operator) ────────────────────────────

def test_discipline_for_class_known_codes():
    assert inference.discipline_for_class("IfcAirTerminal") == "M"
    assert inference.discipline_for_class("IfcPipeSegment") == "P"
    assert inference.discipline_for_class("IfcLightFixture") == "E"
    assert inference.discipline_for_class("IfcWall") == "A"
    assert inference.discipline_for_class("IfcColumn") == "S"
    assert inference.discipline_for_class("IfcAlarm") == "FP"


def test_discipline_for_unknown_class_is_sentinel():
    assert inference.discipline_for_class("IfcFurniture") == "XX"
    assert inference.discipline_for_class("") == "XX"


def test_level_for_storey_name():
    assert inference.level_for_storey_name("Roof") == "RF"
    assert inference.level_for_storey_name("Ground Floor") == "GF"
    assert inference.level_for_storey_name("Level 2") == "L02"
    assert inference.level_for_storey_name("Plant Room") == "PR"
    assert inference.level_for_storey_name("Mezzanine") == "MZ"
    assert inference.level_for_storey_name("Basement 2") == "B2"
    assert inference.level_for_storey_name(None) == "XX"


def test_level_from_elevation_fallback():
    assert inference.level_for_storey_name("Unnamed", elevation=-3.0) == "B1"
    assert inference.level_for_storey_name("Unnamed", elevation=0.0) == "GF"
    assert inference.level_for_storey_name("Unnamed", elevation=6.0) == "L02"


def test_system_for_name():
    assert inference.system_for_name("Supply Air HVAC") == "HVAC"
    assert inference.system_for_name("Foul Drainage") == "SAN"
    assert inference.system_for_name("Domestic Cold Water DCW") == "DCW"
    assert inference.system_for_name("LV Power") == "ELC"
    assert inference.system_for_name("Sprinkler") == "FP"
    assert inference.system_for_name("Lobby") == "XX"


def test_product_for_type_name():
    assert inference.product_for_type_name("Air Handling Unit") == "AIR"
    assert inference.product_for_type_name("dbpanel") == "DBP"
    assert inference.product_for_type_name("123") == "XX"
    assert inference.product_for_type_name(None) == "XX"


# ── SequenceAllocator (replaces the module-global counter) ────────────────────

def test_sequence_allocator_monotonic_per_group():
    alloc = inference.SequenceAllocator()
    assert alloc.next("p", "M", "HVAC", "L01") == "0001"
    assert alloc.next("p", "M", "HVAC", "L01") == "0002"
    # different group is independent
    assert alloc.next("p", "E", "ELC", "L01") == "0001"


def test_sequence_allocator_observe_high_water_mark():
    alloc = inference.SequenceAllocator()
    alloc.observe("p", "M", "HVAC", "L01", 42)
    assert alloc.next("p", "M", "HVAC", "L01") == "0043"


# ── contract + adapter shape ──────────────────────────────────────────────────

def test_host_adapter_is_abstract():
    with pytest.raises(TypeError):
        HostAdapter()  # cannot instantiate the ABC


def test_ifc_file_adapter_implements_contract():
    a = IfcFileHostAdapter(model=None, host_name="tekla")
    assert a.host_name == "tekla"
    assert isinstance(a, HostAdapter)
    # GlobalId / host id are defensive on a None model
    assert a.global_id(object()) == ""
    # non-tag review deltas are accepted by the sync layer, not mutated locally
    assert a.apply_remote_change(ChangeDelta(kind="issue", global_id="X")) is True


def test_georef_descriptor_defaults():
    g = GeorefDescriptor()
    assert g.logeoref_tier == 0 and g.scale == 1.0 and g.length_unit == "mm"


def test_change_delta_carries_review_payload():
    d = ChangeDelta(kind="bcf", global_id="2O2Fr$t4X7Zf8NOew3FNr2",
                    payload={"topic": "Clash 12"})
    assert d.kind == "bcf" and d.payload["topic"] == "Clash 12"


def test_modules_import_without_bpy_or_ifcopenshell():
    # The whole point of core: no bpy, no hard ifcopenshell dependency.
    assert "bpy" not in sys.modules
