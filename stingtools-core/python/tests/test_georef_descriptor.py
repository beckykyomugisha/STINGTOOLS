"""TRACK B / P4 — georeferencing extraction from an IFC file.

THE DEFECT
----------
``IfcFileHostAdapter.georef_descriptor`` read only ``IfcMapConversion``'s
eastings / northings / height / scale. Three fields were left unset or wrong:

* ``crs_epsg`` — never read, so the coordinates had no named system. The
  server's confidence policy grades an unanchored survey origin LOW, stores
  the transform as a suggestion, and leaves the model at the project origin
  until a coordinator confirms it by hand — the exact manual step this track
  exists to remove.
* ``true_north_deg`` — never read, so a model's rotation was silently dropped.
  A building at the right easting and northing but rotated 20 degrees off is
  arguably worse than one left at the origin, because it *looks* placed.
* ``length_unit`` — defaulted to ``"mm"`` while eastings/northings are metres
  by IFC definition, so any consumer that trusted it was off by 1000.

NO ifcopenshell REQUIRED
------------------------
``ifcopenshell`` is not a dependency of core (that is the point of core), so
these tests drive the adapter with a tiny stand-in model exposing just the
``by_type`` surface the method uses. That is enough to pin the extraction and
the arithmetic, which is where the defects were. It does NOT prove the method
works against a real ``.ifc`` — a real-file check needs ifcopenshell installed
and is a separate, environment-gated concern.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from stingtools_core import IfcFileHostAdapter


class _Entity:
    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


class _Model:
    """Minimal stand-in exposing only ``by_type``."""

    def __init__(self, **by_type):
        self._by_type = by_type

    def by_type(self, name):
        return self._by_type.get(name, [])


def _adapter(**by_type):
    return IfcFileHostAdapter(model=_Model(**by_type), host_name="archicad")


def _map_conversion(**kw):
    base = dict(Eastings=432000.0, Northings=315000.0, OrthogonalHeight=12.5,
                XAxisAbscissa=1.0, XAxisOrdinate=0.0, Scale=1.0)
    base.update(kw)
    return _Entity(**base)


# ── the survey origin ─────────────────────────────────────────────────────────

def test_map_conversion_origin_is_read():
    g = _adapter(IfcMapConversion=[_map_conversion()]).georef_descriptor()
    assert g.easting == 432000.0
    assert g.northing == 315000.0
    assert g.elevation == 12.5


def test_no_map_conversion_is_tier_zero():
    g = _adapter().georef_descriptor()
    assert g.logeoref_tier == 0
    assert g.easting is None and g.northing is None


# ── true north — previously dropped entirely ──────────────────────────────────

def test_true_north_is_derived_from_the_x_axis_direction():
    # 30 degrees: XAxisAbscissa = cos(30), XAxisOrdinate = sin(30).
    mc = _map_conversion(XAxisAbscissa=math.cos(math.radians(30)),
                         XAxisOrdinate=math.sin(math.radians(30)))
    g = _adapter(IfcMapConversion=[mc]).georef_descriptor()
    assert g.true_north_deg is not None
    assert abs(g.true_north_deg - 30.0) < 1e-9


def test_true_north_matches_the_servers_formula():
    # The server computes atan2(XAxisOrdinate, XAxisAbscissa) in
    # IfcAlignmentValidator. The same file described here and ingested there
    # must not disagree about which way the building faces.
    xa, xo = 0.8660254037844387, -0.49999999999999994   # -30 degrees
    g = _adapter(IfcMapConversion=[_map_conversion(XAxisAbscissa=xa, XAxisOrdinate=xo)]).georef_descriptor()
    assert abs(g.true_north_deg - math.degrees(math.atan2(xo, xa))) < 1e-12


def test_an_unrotated_model_reports_zero_not_none():
    # (1, 0) is the IFC default and means "no rotation". Reporting None here
    # would make a correctly-unrotated model indistinguishable from one whose
    # rotation was never read.
    g = _adapter(IfcMapConversion=[_map_conversion()]).georef_descriptor()
    assert g.true_north_deg == 0.0


def test_missing_axis_fields_leave_true_north_unset():
    mc = _map_conversion()
    del mc.XAxisAbscissa
    del mc.XAxisOrdinate
    g = _adapter(IfcMapConversion=[mc]).georef_descriptor()
    assert g.true_north_deg is None


# ── CRS — the field that decides whether the model auto-places ────────────────

def test_projected_crs_name_is_read_and_raises_the_tier():
    g = _adapter(IfcMapConversion=[_map_conversion()],
                 IfcProjectedCRS=[_Entity(Name="EPSG:27700")]).georef_descriptor()
    assert g.crs_epsg == "EPSG:27700"
    assert g.logeoref_tier == 50


def test_without_a_crs_the_tier_is_lower():
    # Coordinates whose system is unstated are a weaker claim, and the tier is
    # what a consumer reads to decide how far to trust them.
    g = _adapter(IfcMapConversion=[_map_conversion()]).georef_descriptor()
    assert g.crs_epsg is None
    assert g.logeoref_tier == 30


def test_a_blank_crs_name_is_treated_as_absent():
    g = _adapter(IfcMapConversion=[_map_conversion()],
                 IfcProjectedCRS=[_Entity(Name="")]).georef_descriptor()
    assert g.crs_epsg is None


# ── scale ─────────────────────────────────────────────────────────────────────

def test_map_conversion_scale_is_carried():
    g = _adapter(IfcMapConversion=[_map_conversion(Scale=0.9996)]).georef_descriptor()
    assert abs(g.scale - 0.9996) < 1e-12


def test_a_missing_or_zero_scale_reads_as_one():
    # Zero would be a division-by-zero waiting to happen downstream, and a
    # missing scale means "no correction", not "collapse the model".
    assert _adapter(IfcMapConversion=[_map_conversion(Scale=None)]).georef_descriptor().scale == 1.0
    assert _adapter(IfcMapConversion=[_map_conversion(Scale=0)]).georef_descriptor().scale == 1.0


# ── length unit ───────────────────────────────────────────────────────────────

def test_length_unit_falls_back_to_metres_without_ifcopenshell():
    # ifcopenshell is not a core dependency, so calculate_unit_scale is
    # unavailable here and the fallback is what runs. Metres is the fail-safe:
    # "leave it alone" is the only safe response to an unknown unit.
    g = _adapter(IfcMapConversion=[_map_conversion()]).georef_descriptor()
    assert g.length_unit == "m"


# ── robustness ────────────────────────────────────────────────────────────────

def test_a_model_that_raises_on_by_type_degrades_quietly():
    class _Hostile:
        def by_type(self, name):
            raise RuntimeError("corrupt file")

    g = IfcFileHostAdapter(model=_Hostile(), host_name="tekla").georef_descriptor()
    assert g.logeoref_tier == 0
