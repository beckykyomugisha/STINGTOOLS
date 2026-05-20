"""Cross-entity + cross-element rule checks across the 5 STING Psets.
The STING-side closeout for rules IDS v1.0 can't express directly.

Static checks implemented (12):

  Per-element (called via check_element):
    - DISC_NOT_EMPTY:              Pset_StingTags.Discipline ≠ "XX" at active stage
    - LOC_MATCHES_BUILDING:        element.Location == containing IfcBuilding.LocationCode
    - LVL_MATCHES_STOREY:          element.Level    == containing IfcBuildingStorey.LevelCode
    - ZONE_MATCHES_ASSIGNEDZONE:   element.Zone     ∈ {assigned IfcZone.ZoneCode}
    - SYS_MATCHES_IFCSYSTEM:       when element is member of an IfcSystem, System aligns
    - FULLTAG_CONSISTENT:          when FullTag is set, it equals dash-joined segments
    - DRAWING_TYPE_RESOLVABLE:     Pset_StingDrawing.DrawingTypeId format + registry lookup

  Model-level (called via check_all_elements):
    - SEQ_UNIQUE_WITHIN_GROUP:     Sequence is unique within (Discipline, System, Level)
    - BUILDING_LOC_UNIQUE:         each IfcBuilding's LocationCode is unique across project
    - STOREY_LVL_UNIQUE_WITHIN_BUILDING: each IfcBuildingStorey's LevelCode is unique
                                          within its containing IfcBuilding

  Project-level (called via check_all_elements):
    - PROJECTORG_PROJECT_CODE_REQUIRED: Pset_StingProjectOrg.ProjectCode non-empty + format
    - PROJECTORG_PHASE_VALID:           Pset_StingProjectOrg.Phase ∈ StingRibaStages

Behavioural rules (enforced-by="host" — NOT implemented here):
  - TOKEN_LOCK_HONORED          (Pset_StingTags) — auto-populator behaviour
  - TAG_HISTORY_PROVIDED        (Pset_StingTags) — retag-time contract
  - PROJECTORG_SINGLETON        (Pset_StingProjectOrg) — write-path contract
  - CROP_KIND_MATCHES_PROFILE   (Pset_StingDrawing) — DrawingDriftDetector territory
  - PACK_CHECKSUM_MATCHES       (Pset_StingDrawing) — ManagedTemplateSyncer territory
  - TAG7_NARRATIVE_CONSISTENT   (Pset_StingTag7) — TAG7Builder contract
  - TAG7_PARAGRAPH_STATE_EXCLUSIVE (Pset_StingTag7) — per-view, not per-IFC
  - TAG7_TECHNICAL_SPECS_BY_DISCIPLINE (Pset_StingTag7) — LABEL_DEFINITIONS lookup territory

Uses ifcopenshell when available; the API is dependency-aware (importing
this module does NOT require ifcopenshell — only calling .check_*() does).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Iterable, Optional


# DrawingType id format: lowercase alphanumeric + dash, 3-60 chars.
# Example: "arch-plan-A1-1to100", "pres-3d-axon-A1".
_DRAWING_TYPE_ID_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9\-]{2,59}$")

# Project code format per Pset_StingProjectOrg rule: 3-6 uppercase
# alphanumerics (extended to allow digits + a single hyphen for joint
# ventures, e.g. "AHS-01").
_PROJECT_CODE_RE = re.compile(r"^[A-Z][A-Z0-9\-]{2,5}$")

# Stage ordering — drives ActiveFrom gating.  Values match the
# StingRibaStages enum codes.
_STAGE_ORDER: dict[str, int] = {
    "Stage_0": 0, "Stage_1": 1, "Stage_2": 2, "Stage_3": 3,
    "Stage_4": 4, "Stage_5": 5, "Stage_6": 6, "Stage_7": 7,
}

# Per-rule ActiveFrom — kept in sync with the <ActiveFrom> elements in
# shared/ifc/psets/*.xml.  If a rule is added to a Pset without an
# entry here, _active() defaults its activation to Stage_3 (the
# substrate's main production gate).
_RULE_ACTIVE_FROM: dict[str, str] = {
    # Pset_StingTags
    "DISC_NOT_EMPTY":              "Stage_2",
    "LOC_MATCHES_BUILDING":        "Stage_3",
    "LVL_MATCHES_STOREY":          "Stage_3",
    "ZONE_MATCHES_ASSIGNEDZONE":   "Stage_3",
    "SYS_MATCHES_IFCSYSTEM":       "Stage_3",
    "SEQ_UNIQUE_WITHIN_GROUP":     "Stage_3",
    "FULLTAG_CONSISTENT":          "Stage_3",
    # Pset_StingSpatialCodes
    "BUILDING_LOC_UNIQUE":         "Stage_2",
    "STOREY_LVL_UNIQUE_WITHIN_BUILDING": "Stage_2",
    # Pset_StingDrawing
    "DRAWING_TYPE_RESOLVABLE":     "Stage_3",
    # Pset_StingProjectOrg
    "PROJECTORG_PROJECT_CODE_REQUIRED": "Stage_1",
    "PROJECTORG_PHASE_VALID":      "Stage_1",
}


class DrawingTypeRegistry:
    """Optional registry of known DrawingType ids — used to enforce
    DRAWING_TYPE_RESOLVABLE in SpatialChecker. Pass None when no
    registry is loaded; only the format check fires."""

    def __init__(self, known_ids: Iterable[str]):
        self._known = frozenset(known_ids)

    def is_known(self, drawing_type_id: str) -> bool:
        return drawing_type_id in self._known

    def __len__(self) -> int:
        return len(self._known)

    def __contains__(self, dt_id: str) -> bool:
        return dt_id in self._known


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

    def __init__(
        self,
        model: Any,
        stage: str = "Stage_3",
        enum_registry: Any = None,
        drawing_type_registry: Optional["DrawingTypeRegistry"] = None,
    ):
        """`model` is an ifcopenshell.file.file object. We don't import
        ifcopenshell here; callers pass the already-opened model.

        Optional context:
            stage                   — active RIBA stage (controls
                                      which rules fire). Default Stage_3.
            enum_registry           — stingtools_core.EnumRegistry for
                                      enum-membership checks (DISC,
                                      PROJECTORG_PHASE_VALID).
            drawing_type_registry   — DrawingTypeRegistry for
                                      DRAWING_TYPE_RESOLVABLE.
        """
        self._model = model
        self._stage = stage
        self._enum_registry = enum_registry
        self._drawing_type_registry = drawing_type_registry

    # ------------------------------------------------------------------

    def _active(self, rule_id: str) -> bool:
        """Return True when `rule_id` is active at the checker's stage.

        Compares the rule's ActiveFrom (from `_RULE_ACTIVE_FROM`) to the
        active stage on `self._stage`. Unknown rules default to Stage_3.
        Unknown stages default to Stage_3.
        """
        active_from = _RULE_ACTIVE_FROM.get(rule_id, "Stage_3")
        return (
            _STAGE_ORDER.get(self._stage, 3)
            >= _STAGE_ORDER.get(active_from, 3)
        )

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
        """Run per-element rules on a single element. Returns a list of
        mismatches; empty when the element passes all applicable rules."""
        out: list[SpatialMismatch] = []
        tags = self._pset(el, "Pset_StingTags")
        global_id = getattr(el, "GlobalId", "") or ""

        # Per-Pset_StingDrawing — DRAWING_TYPE_RESOLVABLE.
        # Applies to any element carrying Pset_StingDrawing (typically
        # IfcAnnotation), runs independently of Pset_StingTags presence.
        drawing = self._pset(el, "Pset_StingDrawing")
        if drawing and self._active("DRAWING_TYPE_RESOLVABLE"):
            dt_id = drawing.get("DrawingTypeId")
            if dt_id:
                if not _DRAWING_TYPE_ID_RE.match(str(dt_id)):
                    out.append(SpatialMismatch(
                        rule_id="DRAWING_TYPE_RESOLVABLE",
                        ifc_global_id=global_id,
                        segment="DrawingTypeId",
                        element_value=str(dt_id),
                        expected_values=(),
                        message=(
                            f"DrawingTypeId {dt_id!r} doesn't match the "
                            f"format ^[a-zA-Z][a-zA-Z0-9\\-]+$"
                        ),
                    ))
                elif self._drawing_type_registry is not None and not self._drawing_type_registry.is_known(str(dt_id)):
                    out.append(SpatialMismatch(
                        rule_id="DRAWING_TYPE_RESOLVABLE",
                        ifc_global_id=global_id,
                        segment="DrawingTypeId",
                        element_value=str(dt_id),
                        expected_values=(),
                        message=(
                            f"DrawingTypeId {dt_id!r} does not resolve in the "
                            f"DrawingType registry ({len(self._drawing_type_registry)} known ids)"
                        ),
                    ))

        if not tags:
            return out

        # DISC_NOT_EMPTY — Discipline must be set + non-XX at Stage_3+.
        # Whole rule gated by ActiveFrom=Stage_2; XX-exclusion is the
        # Stage_3+ sub-clause.
        disc_value = tags.get("Discipline")
        require_disc_set = self._active("DISC_NOT_EMPTY")
        forbid_xx = _STAGE_ORDER.get(self._stage, 3) >= _STAGE_ORDER["Stage_3"]
        if require_disc_set and (not disc_value):
            out.append(SpatialMismatch(
                rule_id="DISC_NOT_EMPTY",
                ifc_global_id=global_id,
                segment="Discipline",
                element_value="",
                expected_values=(),
                message=f"Discipline is empty at {self._stage}",
            ))
        elif forbid_xx and str(disc_value) == "XX":
            out.append(SpatialMismatch(
                rule_id="DISC_NOT_EMPTY",
                ifc_global_id=global_id,
                segment="Discipline",
                element_value="XX",
                expected_values=(),
                message=f"Discipline is sentinel 'XX' at {self._stage} (must be a real code)",
            ))
        elif require_disc_set and self._enum_registry is not None and disc_value:
            # Enum-membership check (uses the live EnumRegistry).
            # Same Stage_2+ gate as the empty/XX branches — keeps the
            # whole rule consistent with ActiveFrom.
            disc_enum = self._enum_registry.get("StingDisciplineCodes")
            if disc_enum is not None and str(disc_value) not in disc_enum:
                out.append(SpatialMismatch(
                    rule_id="DISC_NOT_EMPTY",
                    ifc_global_id=global_id,
                    segment="Discipline",
                    element_value=str(disc_value),
                    expected_values=tuple(sorted(disc_enum.codes(include_sentinels=False))),
                    message=f"Discipline {disc_value!r} not in StingDisciplineCodes",
                ))

        # 1. LOC
        loc_value = tags.get("Location")
        if loc_value and self._active("LOC_MATCHES_BUILDING"):
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
        if lvl_value and self._active("LVL_MATCHES_STOREY"):
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
        if zone_value and zone_value != "XX" and self._active("ZONE_MATCHES_ASSIGNEDZONE"):
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
        if sys_value and sys_value not in ("XX", "ARC") and self._active("SYS_MATCHES_IFCSYSTEM"):
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
        if full_tag and self._active("FULLTAG_CONSISTENT"):
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
        """Walk every IfcElement + IfcAnnotation in the model. Runs the
        per-element checks (DISC / LOC / LVL / ZONE / SYS / FullTag /
        DrawingType) + model-level SEQ + spatial-uniqueness checks +
        project-level org checks."""
        out: list[SpatialMismatch] = []
        for el in self._model.by_type("IfcElement"):
            out.extend(self.check_element(el))
        # IfcAnnotation isn't an IfcElement subtype in IFC4 — walk separately
        # so Pset_StingDrawing checks fire on annotation entities too.
        try:
            for ann in self._model.by_type("IfcAnnotation"):
                out.extend(self.check_element(ann))
        except RuntimeError:
            pass  # entity type doesn't exist in this schema
        out.extend(self.check_seq_uniqueness())
        out.extend(self.check_spatial_uniqueness())
        out.extend(self.check_project_org())
        return out

    def check_project_org(self) -> list[SpatialMismatch]:
        """Project-level rules from Pset_StingProjectOrg:
          - PROJECTORG_PROJECT_CODE_REQUIRED — non-empty + format ^[A-Z][A-Z0-9-]{2,5}$
          - PROJECTORG_PHASE_VALID — Phase ∈ StingRibaStages (when enum_registry available)

        Returns empty list when no IfcProject carries Pset_StingProjectOrg
        (rule only fires when the pset is present)."""
        out: list[SpatialMismatch] = []
        # Both rules share ActiveFrom=Stage_1 — bail early when neither is active.
        if not (self._active("PROJECTORG_PROJECT_CODE_REQUIRED")
                or self._active("PROJECTORG_PHASE_VALID")):
            return out
        for project in self._model.by_type("IfcProject"):
            pset = self._pset(project, "Pset_StingProjectOrg")
            if not pset:
                continue
            global_id = getattr(project, "GlobalId", "") or ""

            # PROJECTORG_PROJECT_CODE_REQUIRED
            project_code = pset.get("ProjectCode")
            if not self._active("PROJECTORG_PROJECT_CODE_REQUIRED"):
                pass  # rule inactive at this stage
            elif not project_code:
                out.append(SpatialMismatch(
                    rule_id="PROJECTORG_PROJECT_CODE_REQUIRED",
                    ifc_global_id=global_id,
                    segment="ProjectCode",
                    element_value="",
                    expected_values=(),
                    message="Pset_StingProjectOrg.ProjectCode is empty",
                ))
            elif not _PROJECT_CODE_RE.match(str(project_code)):
                out.append(SpatialMismatch(
                    rule_id="PROJECTORG_PROJECT_CODE_REQUIRED",
                    ifc_global_id=global_id,
                    segment="ProjectCode",
                    element_value=str(project_code),
                    expected_values=(),
                    message=(
                        f"Pset_StingProjectOrg.ProjectCode {project_code!r} doesn't match "
                        f"expected pattern ^[A-Z][A-Z0-9-]{{2,5}}$ (3-6 chars, uppercase, alphanumeric)"
                    ),
                ))

            # PROJECTORG_PHASE_VALID — ActiveFrom Stage_1, so the gate
            # is always satisfied except in the explicit Stage_0 case.
            phase = pset.get("Phase")
            if not self._active("PROJECTORG_PHASE_VALID"):
                pass  # rule inactive
            elif phase and self._enum_registry is not None:
                riba_enum = self._enum_registry.get("StingRibaStages")
                if riba_enum is not None and str(phase) not in riba_enum:
                    out.append(SpatialMismatch(
                        rule_id="PROJECTORG_PHASE_VALID",
                        ifc_global_id=global_id,
                        segment="Phase",
                        element_value=str(phase),
                        expected_values=tuple(sorted(riba_enum.codes(include_sentinels=False))),
                        message=f"Pset_StingProjectOrg.Phase {phase!r} not in StingRibaStages",
                    ))
            elif not phase:
                # Phase is required per cardinality — but only flag at Stage_2+
                if _STAGE_ORDER.get(self._stage, 3) >= _STAGE_ORDER["Stage_2"]:
                    out.append(SpatialMismatch(
                        rule_id="PROJECTORG_PHASE_VALID",
                        ifc_global_id=global_id,
                        segment="Phase",
                        element_value="",
                        expected_values=(),
                        message=f"Pset_StingProjectOrg.Phase is empty at {self._stage}",
                    ))

        return out

    def check_spatial_uniqueness(self) -> list[SpatialMismatch]:
        """Model-level spatial-code uniqueness rules:
          - BUILDING_LOC_UNIQUE — every IfcBuilding.LocationCode is unique across project
          - STOREY_LVL_UNIQUE_WITHIN_BUILDING — every IfcBuildingStorey.LevelCode
            is unique within its containing IfcBuilding

        Both active from Stage_2 per Pset_StingSpatialCodes.xml."""
        out: list[SpatialMismatch] = []
        check_loc = self._active("BUILDING_LOC_UNIQUE")
        check_lvl = self._active("STOREY_LVL_UNIQUE_WITHIN_BUILDING")
        if not (check_loc or check_lvl):
            return out

        # BUILDING_LOC_UNIQUE
        # Group buildings by LocationCode; any code with > 1 building is a collision.
        bldg_by_loc: dict[str, list[tuple[str, str]]] = {}
        if check_loc:
            for bldg in self._model.by_type("IfcBuilding"):
                spatial = self._pset(bldg, "Pset_StingSpatialCodes")
                if not spatial:
                    continue
                loc = spatial.get("LocationCode")
                if not loc:
                    continue
                global_id = getattr(bldg, "GlobalId", "") or ""
                name = getattr(bldg, "Name", "") or "(unnamed)"
                bldg_by_loc.setdefault(str(loc), []).append((global_id, name))

        for loc, occupants in bldg_by_loc.items():
            if len(occupants) > 1:
                names = [f"{n!r}" for _, n in occupants]
                for gid, name in occupants:
                    out.append(SpatialMismatch(
                        rule_id="BUILDING_LOC_UNIQUE",
                        ifc_global_id=gid,
                        segment="LocationCode",
                        element_value=loc,
                        expected_values=(),
                        message=(
                            f"LocationCode {loc!r} is shared by {len(occupants)} buildings: "
                            f"{', '.join(names)}"
                        ),
                    ))

        # STOREY_LVL_UNIQUE_WITHIN_BUILDING
        # Walk each building; group its decomposed storeys by LevelCode.
        if not check_lvl:
            return out
        for bldg in self._model.by_type("IfcBuilding"):
            storey_by_lvl: dict[str, list[tuple[str, str]]] = {}
            bldg_name = getattr(bldg, "Name", "") or getattr(bldg, "GlobalId", "")
            for rel in getattr(bldg, "IsDecomposedBy", []) or []:
                for storey in getattr(rel, "RelatedObjects", []) or []:
                    if not storey.is_a("IfcBuildingStorey"):
                        continue
                    spatial = self._pset(storey, "Pset_StingSpatialCodes")
                    if not spatial:
                        continue
                    lvl = spatial.get("LevelCode")
                    if not lvl:
                        continue
                    storey_id = getattr(storey, "GlobalId", "") or ""
                    storey_name = getattr(storey, "Name", "") or "(unnamed)"
                    storey_by_lvl.setdefault(str(lvl), []).append((storey_id, storey_name))

            for lvl, occupants in storey_by_lvl.items():
                if len(occupants) > 1:
                    names = [f"{n!r}" for _, n in occupants]
                    for gid, name in occupants:
                        out.append(SpatialMismatch(
                            rule_id="STOREY_LVL_UNIQUE_WITHIN_BUILDING",
                            ifc_global_id=gid,
                            segment="LevelCode",
                            element_value=lvl,
                            expected_values=(),
                            message=(
                                f"LevelCode {lvl!r} is shared by {len(occupants)} storeys "
                                f"in building {bldg_name!r}: {', '.join(names)}"
                            ),
                        ))

        return out

    def check_seq_uniqueness(self) -> list[SpatialMismatch]:
        """SEQ_UNIQUE_WITHIN_GROUP — within each (Discipline, System,
        Level) tuple, every element's Sequence must be unique.

        Active from Stage_3 per Pset_StingTags.xml. Reports one
        SpatialMismatch per duplicate.
        """
        out: list[SpatialMismatch] = []
        if not self._active("SEQ_UNIQUE_WITHIN_GROUP"):
            return out
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
