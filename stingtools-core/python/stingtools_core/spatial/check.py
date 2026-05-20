"""Cross-entity equality closeout for Pset_StingSpatialCodes rules.

IDS v1.0 can verify "element has Pset_StingTags.Location" and "element is
contained in an IfcBuilding with Pset_StingSpatialCodes.LocationCode" —
but not "the two values are equal". This module closes that gap by
walking the IFC graph after IDS validation passes.

Three checks:
  - LOC_MATCHES_BUILDING:        element.Location == containing IfcBuilding.LocationCode
  - LVL_MATCHES_STOREY:          element.Level    == containing IfcBuildingStorey.LevelCode
  - ZONE_MATCHES_ASSIGNEDZONE:   element.Zone     ∈ {assigned IfcZone.ZoneCode for each Zone the element is in}

Uses ifcopenshell when available; the API is dependency-aware (importing
this module does NOT require ifcopenshell — only calling .check_*() does).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional


@dataclass(frozen=True)
class SpatialMismatch:
    rule_id: str           # LOC_MATCHES_BUILDING | LVL_MATCHES_STOREY | ZONE_MATCHES_ASSIGNEDZONE
    ifc_global_id: str     # the failing element
    segment: str           # Location | Level | Zone
    element_value: str
    expected_values: tuple[str, ...]  # what would have made it pass
    message: str


class SpatialChecker:
    """Walks an opened ifcopenshell model and reports mismatches."""

    def __init__(self, model: Any):
        """`model` is an ifcopenshell.file.file object. We don't import
        ifcopenshell here; callers pass the already-opened model."""
        self._model = model

    # ------------------------------------------------------------------

    @staticmethod
    def _pset(el: Any, pset_name: str) -> dict[str, Any]:
        """Best-effort property-set extraction.

        Uses ifcopenshell.util.element.get_psets when available; falls
        back to walking IsDefinedBy for portability.
        """
        try:
            import ifcopenshell.util.element as ue  # type: ignore
            psets = ue.get_psets(el) or {}
            return dict(psets.get(pset_name) or {})
        except ImportError:
            # Manual walk
            for rel in getattr(el, "IsDefinedBy", []) or []:
                definition = getattr(rel, "RelatingPropertyDefinition", None)
                if definition is None or definition.is_a() != "IfcPropertySet":
                    continue
                if definition.Name != pset_name:
                    continue
                out: dict[str, Any] = {}
                for prop in definition.HasProperties or []:
                    if prop.is_a() == "IfcPropertySingleValue":
                        v = prop.NominalValue
                        out[prop.Name] = v.wrappedValue if v else None
                return out
            return {}

    def _containing_building(self, el: Any) -> Optional[Any]:
        """Walk IfcRelContainedInSpatialStructure → IfcBuildingStorey → IfcBuilding (parent)."""
        storey = self._containing_storey(el)
        if storey is None:
            return None
        # IfcBuildingStorey is decomposed FROM an IfcBuilding via IfcRelAggregates
        for rel in getattr(storey, "Decomposes", []) or []:
            relating = getattr(rel, "RelatingObject", None)
            if relating and relating.is_a("IfcBuilding"):
                return relating
        return None

    def _containing_storey(self, el: Any) -> Optional[Any]:
        for rel in getattr(el, "ContainedInStructure", []) or []:
            structure = getattr(rel, "RelatingStructure", None)
            if structure and structure.is_a("IfcBuildingStorey"):
                return structure
        return None

    def _assigned_zones(self, el: Any) -> list[Any]:
        zones: list[Any] = []
        for rel in getattr(el, "HasAssignments", []) or []:
            if not rel.is_a("IfcRelAssignsToGroup"):
                continue
            grp = getattr(rel, "RelatingGroup", None)
            if grp and grp.is_a("IfcZone"):
                zones.append(grp)
        return zones

    # ------------------------------------------------------------------

    def check_element(self, el: Any) -> list[SpatialMismatch]:
        """Run all three cross-entity rules on a single element."""
        out: list[SpatialMismatch] = []
        tags = self._pset(el, "Pset_StingTags")
        if not tags:
            return out

        global_id = getattr(el, "GlobalId", "") or ""

        # 1. LOC
        loc_value = tags.get("Location")
        if loc_value:
            building = self._containing_building(el)
            if building is None:
                out.append(SpatialMismatch(
                    rule_id="LOC_MATCHES_BUILDING",
                    ifc_global_id=global_id,
                    segment="Location",
                    element_value=str(loc_value),
                    expected_values=(),
                    message="element is not contained in any IfcBuilding",
                ))
            else:
                spatial = self._pset(building, "Pset_StingSpatialCodes")
                building_loc = spatial.get("LocationCode")
                if str(loc_value) != str(building_loc or ""):
                    out.append(SpatialMismatch(
                        rule_id="LOC_MATCHES_BUILDING",
                        ifc_global_id=global_id,
                        segment="Location",
                        element_value=str(loc_value),
                        expected_values=(str(building_loc or ""),),
                        message=f"element LOC {loc_value!r} != building.LocationCode {building_loc!r}",
                    ))

        # 2. LVL
        lvl_value = tags.get("Level")
        if lvl_value:
            storey = self._containing_storey(el)
            if storey is None:
                out.append(SpatialMismatch(
                    rule_id="LVL_MATCHES_STOREY",
                    ifc_global_id=global_id,
                    segment="Level",
                    element_value=str(lvl_value),
                    expected_values=(),
                    message="element is not contained in any IfcBuildingStorey",
                ))
            else:
                spatial = self._pset(storey, "Pset_StingSpatialCodes")
                storey_lvl = spatial.get("LevelCode")
                if str(lvl_value) != str(storey_lvl or ""):
                    out.append(SpatialMismatch(
                        rule_id="LVL_MATCHES_STOREY",
                        ifc_global_id=global_id,
                        segment="Level",
                        element_value=str(lvl_value),
                        expected_values=(str(storey_lvl or ""),),
                        message=f"element LVL {lvl_value!r} != storey.LevelCode {storey_lvl!r}",
                    ))

        # 3. ZONE
        zone_value = tags.get("Zone")
        if zone_value and zone_value != "XX":
            zones = self._assigned_zones(el)
            zone_codes: list[str] = []
            for z in zones:
                spatial = self._pset(z, "Pset_StingSpatialCodes")
                zc = spatial.get("ZoneCode")
                if zc:
                    zone_codes.append(str(zc))
            if str(zone_value) not in zone_codes:
                out.append(SpatialMismatch(
                    rule_id="ZONE_MATCHES_ASSIGNEDZONE",
                    ifc_global_id=global_id,
                    segment="Zone",
                    element_value=str(zone_value),
                    expected_values=tuple(zone_codes),
                    message=(
                        f"element ZONE {zone_value!r} not in assigned-zone codes {zone_codes!r}"
                        if zone_codes else
                        f"element ZONE {zone_value!r} but no IfcZone assignment carries a ZoneCode"
                    ),
                ))

        return out

    def check_all_elements(self) -> list[SpatialMismatch]:
        """Walk every IfcElement in the model."""
        out: list[SpatialMismatch] = []
        for el in self._model.by_type("IfcElement"):
            out.extend(self.check_element(el))
        return out
