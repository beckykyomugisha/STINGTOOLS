"""Cross-entity + cross-element rule checks for Pset_StingTags +
Pset_StingSpatialCodes — the STING-side closeout for rules IDS v1.0
can't express directly.

Six checks implemented:
  - LOC_MATCHES_BUILDING:        element.Location == containing IfcBuilding.LocationCode
  - LVL_MATCHES_STOREY:          element.Level    == containing IfcBuildingStorey.LevelCode
  - ZONE_MATCHES_ASSIGNEDZONE:   element.Zone     ∈ {assigned IfcZone.ZoneCode for each Zone the element is in}
  - SYS_MATCHES_IFCSYSTEM:       when element is member of an IfcSystem, element.System aligns with the system's classification
  - SEQ_UNIQUE_WITHIN_GROUP:     Sequence is unique within each (Discipline, System, Level) tuple
  - FULLTAG_CONSISTENT:          when FullTag is set, it equals the dash-joined 8 source segments

Two rules from Pset_StingTags.xml are NOT implemented here because they
are *behavioural* assertions about the host-side auto-tagger, not
properties of a static IFC snapshot:
  - TOKEN_LOCK_HONORED — guarantees the auto-populator won't overwrite locked tokens
  - TAG_HISTORY_PROVIDED — guarantees PreviousTag + ModifiedAt are updated on retag
Both ship enforced inside each host plugin's tag-write path (Revit's
TokenAutoPopulator, the Bonsai add-on's port). See Pset_StingTags.xml
for the cardinality / Source-Of-Enforcement annotation.

Uses ifcopenshell when available; the API is dependency-aware (importing
this module does NOT require ifcopenshell — only calling .check_*() does).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional


@dataclass(frozen=True)
class SpatialMismatch:
    """Generic mismatch result. Used for all 6 checks; `rule_id` distinguishes them."""
    rule_id: str
    ifc_global_id: str     # the failing element
    segment: str           # Location | Level | Zone | System | Sequence | FullTag
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

    def _assigned_systems(self, el: Any) -> list[Any]:
        """Return the IfcSystem (or IfcDistributionSystem) memberships of an element."""
        out: list[Any] = []
        for rel in getattr(el, "HasAssignments", []) or []:
            if not rel.is_a("IfcRelAssignsToGroup"):
                continue
            grp = getattr(rel, "RelatingGroup", None)
            if grp and grp.is_a("IfcSystem"):
                out.append(grp)
        return out

    # System-name → expected STING System token. Keep lower-case keys for
    # case-insensitive match. Matches the StingSystemCodes enum members.
    _SYSTEM_NAME_TO_SYS = {
        "hvac":       "HVAC",
        "ventilation": "HVAC",
        "air":        "HVAC",
        "dcw":        "DCW",
        "cold water": "DCW",
        "dhw":        "DHW",
        "hot water":  "DHW",
        "hws":        "HWS",
        "san":        "SAN",
        "sanitary":   "SAN",
        "soil":       "SAN",
        "rwd":        "RWD",
        "stormwater": "RWD",
        "gas":        "GAS",
        "fp":         "FP",
        "sprinkler":  "FP",
        "fls":        "FLS",
        "fire alarm": "FLS",
        "lv":         "LV",
        "power":      "LV",
        "lighting":   "LV",
        "com":        "COM",
        "comms":      "COM",
        "ict":        "ICT",
        "data":       "ICT",
        "ncl":        "NCL",
        "nurse call": "NCL",
        "sec":        "SEC",
        "security":   "SEC",
        "mgs":        "MGS",
        "medical gas": "MGS",
    }

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

        # 4. SYS_MATCHES_IFCSYSTEM — when element is in an IfcSystem,
        #    the System token must align with the system's classification.
        sys_value = tags.get("System")
        if sys_value and sys_value not in ("XX", "ARC"):
            systems = self._assigned_systems(el)
            for system in systems:
                # Map system name to expected token
                sys_name = (getattr(system, "Name", "") or "").lower()
                expected = None
                for needle, token in self._SYSTEM_NAME_TO_SYS.items():
                    if needle in sys_name:
                        expected = token
                        break
                if expected and str(sys_value) != expected:
                    out.append(SpatialMismatch(
                        rule_id="SYS_MATCHES_IFCSYSTEM",
                        ifc_global_id=global_id,
                        segment="System",
                        element_value=str(sys_value),
                        expected_values=(expected,),
                        message=(
                            f"element SYS {sys_value!r} doesn't align with assigned "
                            f"IfcSystem {system.Name!r} (expected {expected!r})"
                        ),
                    ))

        # 5. FULLTAG_CONSISTENT — if FullTag is stored, verify it matches
        #    the dash-joined source segments.
        full_tag = tags.get("FullTag")
        if full_tag:
            segments = [
                tags.get("Discipline") or "",
                tags.get("Location")   or "",
                tags.get("Zone")       or "",
                tags.get("Level")      or "",
                tags.get("System")     or "",
                tags.get("Function")   or "",
                tags.get("Product")    or "",
                tags.get("Sequence")   or "",
            ]
            expected_full = "-".join(str(s) for s in segments)
            if str(full_tag) != expected_full:
                out.append(SpatialMismatch(
                    rule_id="FULLTAG_CONSISTENT",
                    ifc_global_id=global_id,
                    segment="FullTag",
                    element_value=str(full_tag),
                    expected_values=(expected_full,),
                    message=(
                        f"FullTag {full_tag!r} does not match dash-joined "
                        f"segments {expected_full!r}"
                    ),
                ))

        return out

    def check_all_elements(self) -> list[SpatialMismatch]:
        """Walk every IfcElement in the model. Runs per-element checks
        (LOC / LVL / ZONE / SYS / FullTag) + the model-level
        SEQ_UNIQUE_WITHIN_GROUP check."""
        out: list[SpatialMismatch] = []
        for el in self._model.by_type("IfcElement"):
            out.extend(self.check_element(el))
        out.extend(self.check_seq_uniqueness())
        return out

    def check_seq_uniqueness(self) -> list[SpatialMismatch]:
        """SEQ_UNIQUE_WITHIN_GROUP — within each (Discipline, System,
        Level) tuple, every element's Sequence must be unique.

        Active from Stage_3 per Pset_StingTags.xml. Reports one
        SpatialMismatch per duplicate.
        """
        out: list[SpatialMismatch] = []
        # (disc, sys, lvl) -> { seq -> [ifc_global_id, ...] }
        groups: dict[tuple[str, str, str], dict[str, list[str]]] = {}

        for el in self._model.by_type("IfcElement"):
            tags = self._pset(el, "Pset_StingTags")
            if not tags:
                continue
            disc = str(tags.get("Discipline") or "")
            sysv = str(tags.get("System") or "")
            lvl  = str(tags.get("Level") or "")
            seq  = str(tags.get("Sequence") or "")
            if not (disc and sysv and lvl and seq):
                continue
            # sentinels skip
            if disc == "XX" or sysv == "XX" or lvl == "XX" or seq == "0000":
                continue
            key = (disc, sysv, lvl)
            global_id = getattr(el, "GlobalId", "") or ""
            groups.setdefault(key, {}).setdefault(seq, []).append(global_id)

        for (disc, sysv, lvl), seq_map in groups.items():
            for seq, global_ids in seq_map.items():
                if len(global_ids) > 1:
                    for gid in global_ids:
                        out.append(SpatialMismatch(
                            rule_id="SEQ_UNIQUE_WITHIN_GROUP",
                            ifc_global_id=gid,
                            segment="Sequence",
                            element_value=seq,
                            expected_values=(),
                            message=(
                                f"duplicate Sequence {seq!r} in group "
                                f"(Discipline={disc!r}, System={sysv!r}, Level={lvl!r}); "
                                f"{len(global_ids)} elements share this SEQ"
                            ),
                        ))
        return out
