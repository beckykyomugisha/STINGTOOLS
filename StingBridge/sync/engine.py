"""
Main sync orchestrator for the STING ArchiCAD bridge.

Flow:
  1. Discover ArchiCAD on port 19723-19726
  2. For each syncable element type, fetch GUIDs in batches
  3. Fetch element details (story, bounding box) and property values
  4. Map tokens via token_mapper
  5. Post mapped elements to Planscape via /api/tagsync/sync
  6. Write STING tokens back to ArchiCAD as User-Defined properties
"""
from __future__ import annotations

import logging
import time
from dataclasses import dataclass, field
from typing import Any

from ..archicad.client import ArchiCadClient, ArchiCadError
from ..archicad.element_types import SYNCABLE_TYPES
from ..planscape.client import PlanscapeClient, PlanscapeError
from .token_mapper import map_element_to_tokens
from .property_writer import PropertyWriter

log = logging.getLogger(__name__)

# Properties to fetch from ArchiCAD for each element
_PROPS_TO_READ = [
    {"type": "UserDefined", "localizedName": ["STING", "ASS_DISCIPLINE_COD_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_LOC_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_ZONE_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_LVL_COD_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_SYSTEM_TYPE_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_FUNC_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_PRODCT_COD_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_SEQ_NUM_TXT"]},
    {"type": "UserDefined", "localizedName": ["STING", "ASS_TAG_1"]},
    {"type": "BuiltIn",     "nonLocalizedName": "AC_Pset_RenovationInfo.RenovationStatus"},
    {"type": "BuiltIn",     "nonLocalizedName": "General_ElementID"},
]


@dataclass
class SyncResult:
    elements_found: int = 0
    elements_mapped: int = 0
    planscape_synced: int = 0
    archicad_written: int = 0
    errors: list[str] = field(default_factory=list)
    duration_s: float = 0.0

    def summary(self) -> str:
        return (
            f"Found: {self.elements_found}  Mapped: {self.elements_mapped}  "
            f"→ Planscape: {self.planscape_synced}  "
            f"→ ArchiCAD: {self.archicad_written}  "
            f"Errors: {len(self.errors)}  ({self.duration_s:.1f}s)"
        )


class SyncEngine:
    """Orchestrates a full sync pass between ArchiCAD and Planscape."""

    def __init__(
        self,
        ac_client: ArchiCadClient,
        planscape_client: PlanscapeClient,
        write_back: bool = True,
        batch_size: int = 100,
    ):
        self._ac = ac_client
        self._ps = planscape_client
        self._write_back = write_back
        self._batch_size = batch_size
        self._writer = PropertyWriter(ac_client) if write_back else None

    # ── public API ────────────────────────────────────────────────────────────

    def run(self) -> SyncResult:
        t0 = time.monotonic()
        result = SyncResult()

        try:
            self._run_inner(result)
        except Exception as exc:
            result.errors.append(f"Fatal: {exc}")
            log.exception("Sync engine fatal error")

        result.duration_s = time.monotonic() - t0
        return result

    # ── internals ─────────────────────────────────────────────────────────────

    def _run_inner(self, result: SyncResult) -> None:
        # Ensure STING properties exist in AC before writing
        if self._write_back and self._writer:
            self._writer.ensure_properties()

        # Collect story info once for level derivation
        stories: dict[int, dict] = {}
        try:
            for story in self._ac.get_story_info():
                idx = story.get("index", -1)
                stories[idx] = story
        except ArchiCadError as e:
            log.warning("Could not fetch story info: %s", e)

        all_sync_elements: list[dict] = []
        write_pairs: list[tuple[str, dict[str, str]]] = []

        for element_type in SYNCABLE_TYPES:
            try:
                guids = self._ac.get_elements_by_type(element_type)
            except ArchiCadError as e:
                log.warning("get_elements_by_type(%s) failed: %s", element_type, e)
                continue

            if not guids:
                continue

            result.elements_found += len(guids)
            log.info("Processing %d %s elements", len(guids), element_type)

            for chunk in self._ac.batch_guids(guids, self._batch_size):
                self._process_chunk(
                    chunk=chunk,
                    element_type=element_type,
                    stories=stories,
                    all_sync_elements=all_sync_elements,
                    write_pairs=write_pairs,
                    result=result,
                )

        # Post to Planscape in batches
        if all_sync_elements:
            for i in range(0, len(all_sync_elements), self._batch_size):
                batch = all_sync_elements[i : i + self._batch_size]
                try:
                    self._ps.sync_elements(batch, source="archicad")
                    result.planscape_synced += len(batch)
                except (PlanscapeError, Exception) as e:
                    msg = f"Planscape sync failed for batch {i//self._batch_size}: {e}"
                    result.errors.append(msg)
                    log.error(msg)

        # Write tokens back to ArchiCAD
        if self._write_back and self._writer and write_pairs:
            for i in range(0, len(write_pairs), self._batch_size):
                batch = write_pairs[i : i + self._batch_size]
                try:
                    stats = self._writer.write_tokens(batch)
                    result.archicad_written += stats["written"]
                except ArchiCadError as e:
                    msg = f"ArchiCAD write-back failed: {e}"
                    result.errors.append(msg)
                    log.error(msg)

    def _process_chunk(
        self,
        chunk: list[str],
        element_type: str,
        stories: dict[int, dict],
        all_sync_elements: list[dict],
        write_pairs: list[tuple[str, dict[str, str]]],
        result: SyncResult,
    ) -> None:
        # Fetch element details (story index, bounding box, layer)
        details_map: dict[str, dict] = {}
        try:
            details_list = self._ac.get_details_of_elements(chunk)
            for item in details_list:
                eid = item.get("elementId", {}).get("guid", "")
                if eid:
                    details_map[eid] = item.get("details", item)
        except ArchiCadError as e:
            log.warning("get_details_of_elements failed: %s", e)

        # Fetch property values
        props_map: dict[str, dict] = {}
        try:
            prop_values = self._ac.get_property_values(chunk, _PROPS_TO_READ)
            for epv in prop_values:
                eid = epv.get("elementId", {}).get("guid", "")
                if not eid:
                    continue
                bag: dict[str, str] = {}
                for pv in epv.get("propertyValues", []):
                    pid = pv.get("propertyId", {})
                    name = _prop_key(pid)
                    val  = pv.get("propertyValue", {})
                    if val.get("type") == "normal":
                        bag[name] = str(val.get("value", ""))
                props_map[eid] = bag
        except ArchiCadError as e:
            log.warning("get_property_values failed: %s", e)

        for guid in chunk:
            details = details_map.get(guid, {})
            props   = props_map.get(guid, {})

            # Already has all STING tokens? Skip re-derivation
            if _already_tagged(props):
                tokens = {
                    "disc": props.get("STING.ASS_DISCIPLINE_COD_TXT", ""),
                    "loc":  props.get("STING.ASS_LOC_TXT", ""),
                    "zone": props.get("STING.ASS_ZONE_TXT", ""),
                    "lvl":  props.get("STING.ASS_LVL_COD_TXT", ""),
                    "sys":  props.get("STING.ASS_SYSTEM_TYPE_TXT", ""),
                    "func": props.get("STING.ASS_FUNC_TXT", ""),
                    "prod": props.get("STING.ASS_PRODCT_COD_TXT", ""),
                    "seq":  props.get("STING.ASS_SEQ_NUM_TXT", ""),
                    "tag1": props.get("STING.ASS_TAG_1", ""),
                    "status": None,
                }
            else:
                storey_name = ""
                storey_elev = None
                story_idx = details.get("floor", details.get("storyIndex", -1))
                if story_idx >= 0 and story_idx in stories:
                    story = stories[story_idx]
                    storey_name = story.get("name", "")
                    storey_elev = story.get("level")

                tokens = map_element_to_tokens(
                    element_type=element_type,
                    props=props,
                    storey_name=storey_name,
                    storey_elevation_m=storey_elev,
                    library_part_name=details.get("libPartName", ""),
                )

            # Build Planscape sync element
            is_complete = bool(
                tokens.get("disc") and tokens.get("lvl") and
                tokens.get("sys") and tokens.get("prod")
            )
            sync_el = PlanscapeClient.build_element_sync(
                guid=guid,
                disc=tokens.get("disc", ""),
                loc=tokens.get("loc", ""),
                zone=tokens.get("zone", ""),
                lvl=tokens.get("lvl", ""),
                sys=tokens.get("sys", ""),
                func=tokens.get("func", ""),
                prod=tokens.get("prod", ""),
                seq=tokens.get("seq", ""),
                category_name=element_type,
                family_name=details.get("libPartName", ""),
                status=tokens.get("status"),
                is_complete=is_complete,
            )
            all_sync_elements.append(sync_el)
            result.elements_mapped += 1

            if self._write_back:
                write_pairs.append((guid, tokens))


def _prop_key(pid: dict) -> str:
    """Build a consistent property key from a propertyId dict."""
    if pid.get("type") == "UserDefined":
        parts = pid.get("localizedName", [])
        return ".".join(parts)
    return pid.get("nonLocalizedName", "")


def _already_tagged(props: dict) -> bool:
    """Return True if the element already has all core STING tokens filled."""
    required = [
        "STING.ASS_DISCIPLINE_COD_TXT",
        "STING.ASS_LVL_COD_TXT",
        "STING.ASS_SYSTEM_TYPE_TXT",
    ]
    return all(bool(props.get(k)) for k in required)
