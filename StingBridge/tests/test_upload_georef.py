"""TRACK B / P4 — the model upload carries the host's georeferencing.

``GeorefDescriptor`` existed with ZERO consumers: nothing built a full one and
nothing sent one anywhere. So an ArchiCAD or Tekla IFC pushed through the bridge
arrived at the server with no survey position, landed at the project origin, and
had to be placed by hand — which is the manual step the whole placement track
exists to remove.

These tests pin the wire contract, because that is where it silently breaks: the
field NAMES must match the server's ``UploadModelRequest`` properties exactly
(``GeorefEastingM`` and friends). A typo there is not an error at either end —
ASP.NET model binding simply leaves the property null, the server writes no
transform, and the model quietly stays at the origin. Nothing fails; the building
is just in the wrong place.
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))
_CORE = _REPO_ROOT / "stingtools-core" / "python"
if str(_CORE) not in sys.path:
    sys.path.insert(0, str(_CORE))

from StingBridge.planscape.client import PlanscapeClient  # noqa: E402
from stingtools_core.hosts.adapter import GeorefDescriptor  # noqa: E402


def _fields(georef):
    return PlanscapeClient._georef_fields(georef)


def _anchored(**kw):
    base = dict(logeoref_tier=50, crs_epsg="EPSG:27700",
                easting=432000.0, northing=315000.0, elevation=12.5,
                true_north_deg=3.25, scale=1.0, length_unit="m")
    base.update(kw)
    return GeorefDescriptor(**base)


# ── the wire contract ─────────────────────────────────────────────────────────

def test_field_names_match_the_servers_upload_request():
    # These strings are the contract. If the server renames a property, this
    # test is what fails — instead of the model silently landing at 0,0,0.
    f = _fields(_anchored())
    assert set(f) == {
        "GeorefEastingM", "GeorefNorthingM", "GeorefElevationM",
        "GeorefTrueNorthDeg", "GeorefCrsEpsg", "GeorefLengthUnit",
        "GeorefExportMode",
    }


def test_values_survive_as_full_precision_strings():
    f = _fields(_anchored())
    assert float(f["GeorefEastingM"]) == 432000.0
    assert float(f["GeorefNorthingM"]) == 315000.0
    assert float(f["GeorefElevationM"]) == 12.5
    assert float(f["GeorefTrueNorthDeg"]) == 3.25
    assert f["GeorefCrsEpsg"] == "EPSG:27700"
    assert f["GeorefLengthUnit"] == "m"


def test_a_survey_easting_is_not_rounded_away():
    # repr() rather than str(): a UK easting carries 6 significant figures
    # before the decimal point, and losing the fractional part is a
    # centimetre-to-metre error at the far end of a site.
    f = _fields(_anchored(easting=432123.456789))
    assert float(f["GeorefEastingM"]) == 432123.456789


# ── the "send nothing" cases, which matter more ───────────────────────────────

def test_no_descriptor_sends_no_georef():
    assert _fields(None) == {}


def test_a_descriptor_without_a_survey_origin_sends_no_georef():
    # The load-bearing case. A model with no IfcMapConversion must upload
    # WITHOUT a georef block so the server leaves it at the project origin.
    # An un-placed model at the origin is visibly un-placed; a model placed on
    # a guess looks correct and costs an investigation.
    assert _fields(GeorefDescriptor(logeoref_tier=0)) == {}
    assert _fields(_anchored(easting=None)) == {}
    assert _fields(_anchored(northing=None)) == {}


def test_optional_fields_are_omitted_rather_than_sent_empty():
    # An absent CRS must not arrive as "" — the server's confidence policy
    # compares CRS strings, and "" is not the same claim as "unknown".
    f = _fields(_anchored(crs_epsg=None, elevation=None, true_north_deg=None))
    assert "GeorefCrsEpsg" not in f
    assert "GeorefElevationM" not in f
    assert "GeorefTrueNorthDeg" not in f
    # The origin itself still goes.
    assert float(f["GeorefEastingM"]) == 432000.0


def test_export_mode_is_project_internal():
    # IFC geometry is authored about the file's own origin, with
    # IfcMapConversion describing where that origin sits — so the server must
    # apply the origin, not assume the geometry is already in the survey frame.
    # Sending "SharedCoordinates" here would make the server skip the transform
    # and leave the model unplaced.
    assert _fields(_anchored())["GeorefExportMode"] == "ProjectInternal"


def test_a_zero_true_north_is_sent_not_dropped():
    # 0.0 is a real, meaningful value ("aligned with grid north"). A falsy
    # check instead of an is-None check would drop it and lose the distinction
    # between "no rotation" and "rotation unknown".
    f = _fields(_anchored(true_north_deg=0.0))
    assert float(f["GeorefTrueNorthDeg"]) == 0.0
