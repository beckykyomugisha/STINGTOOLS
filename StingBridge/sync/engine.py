"""
Main sync orchestrator for the STING ArchiCAD bridge.

Flow:
  1. Discover ArchiCAD on port 19723-19726
  2. For each syncable element type, fetch GUIDs in batches
  3. Fetch element details (story, bounding box) and property values
  4. Conflict detection: compare AC vs Planscape lastModifiedUtc — winner writes
  5. Map tokens via token_mapper
  6. Post mapped elements to Planscape via /api/tagsync/sync
  7. Write STING tokens back to ArchiCAD as User-Defined properties
  8. Verify write-back: re-read AC properties and compare — mismatches → sync_errors.json
"""
from __future__ import annotations

import json
import logging
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
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
    conflicts_ac_wins: int = 0       # AC timestamp newer → kept AC value
    conflicts_ps_wins: int = 0       # Planscape timestamp newer → overwrote AC
    write_verify_mismatches: int = 0  # tokens written but re-read differently
    errors: list[str] = field(default_factory=list)
    duration_s: float = 0.0

    def summary(self) -> str:
        conflict_str = (
            f"  Conflicts(AC={self.conflicts_ac_wins}/PS={self.conflicts_ps_wins})"
            if self.conflicts_ac_wins or self.conflicts_ps_wins else ""
        )
        mismatch_str = (
            f"  Verify-mismatches: {self.write_verify_mismatches}"
            if self.write_verify_mismatches else ""
        )
        return (
            f"Found: {self.elements_found}  Mapped: {self.elements_mapped}  "
            f"→ Planscape: {self.planscape_synced}  "
            f"→ ArchiCAD: {self.archicad_written}  "
            f"Errors: {len(self.errors)}{conflict_str}{mismatch_str}  "
            f"({self.duration_s:.1f}s)"
        )


class SyncEngine:
    """Orchestrates a full sync pass between ArchiCAD and Planscape."""

    def __init__(
        self,
        ac_client: ArchiCadClient,
        planscape_client: PlanscapeClient,
        write_back: bool = True,
        batch_size: int = 100,
        verify_write_back: bool = True,
        sync_errors_path: str | None = None,
    ):
        self._ac = ac_client
        self._ps = planscape_client
        self._write_back = write_back
        self._batch_size = batch_size
        self._verify = verify_write_back
        self._sync_errors_path = sync_errors_path or "sync_errors.json"
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

        # ── Conflict detection: fetch Planscape timestamps before syncing ────────
        ps_timestamps: dict[str, datetime] = {}
        if all_sync_elements:
            try:
                ps_timestamps = self._ps.get_element_timestamps(
                    [e["uniqueId"] for e in all_sync_elements]
                )
            except Exception as e:
                log.debug("Could not fetch Planscape timestamps (non-fatal): %s", e)

        # Resolve conflicts: for each element that exists in Planscape already,
        # compare lastModifiedUtc. If Planscape is newer, overwrite AC tokens
        # with the Planscape values instead of the AC-derived ones.
        if ps_timestamps:
            _apply_conflict_resolution(
                all_sync_elements, write_pairs, ps_timestamps, result
            )

        # ── Post to Planscape in batches ──────────────────────────────────────
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

        # ── Write tokens back to ArchiCAD ─────────────────────────────────────
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

        # ── Verify write-back: re-read and compare ────────────────────────────
        if self._write_back and self._verify and write_pairs:
            mismatches = self._verify_write_back(write_pairs)
            result.write_verify_mismatches = len(mismatches)
            if mismatches:
                self._save_sync_errors(mismatches)

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


    def _verify_write_back(
        self, write_pairs: list[tuple[str, dict]]
    ) -> list[dict]:
        """
        Re-read STING properties from ArchiCAD after write-back and compare.
        Returns list of mismatch dicts for the sync_errors.json sidecar.
        """
        guids = [guid for guid, _ in write_pairs]
        token_by_guid = {guid: tokens for guid, tokens in write_pairs}

        _TOKEN_PROP_MAP = {
            "disc": "STING.ASS_DISCIPLINE_COD_TXT",
            "loc":  "STING.ASS_LOC_TXT",
            "zone": "STING.ASS_ZONE_TXT",
            "lvl":  "STING.ASS_LVL_COD_TXT",
            "sys":  "STING.ASS_SYSTEM_TYPE_TXT",
            "func": "STING.ASS_FUNC_TXT",
            "prod": "STING.ASS_PRODCT_COD_TXT",
            "seq":  "STING.ASS_SEQ_NUM_TXT",
        }

        mismatches: list[dict] = []
        try:
            prop_values = self._ac.get_property_values(guids, _PROPS_TO_READ)
        except ArchiCadError as e:
            log.warning("Verify: could not re-read AC properties: %s", e)
            return mismatches

        for epv in prop_values:
            guid = epv.get("elementId", {}).get("guid", "")
            if not guid:
                continue
            written = token_by_guid.get(guid, {})
            # Rebuild property bag from re-read
            re_read: dict[str, str] = {}
            for pv in epv.get("propertyValues", []):
                pid = pv.get("propertyId", {})
                name = _prop_key(pid)
                val = pv.get("propertyValue", {})
                if val.get("type") == "normal":
                    re_read[name] = str(val.get("value", ""))

            for token_key, prop_key in _TOKEN_PROP_MAP.items():
                expected = written.get(token_key, "")
                actual = re_read.get(prop_key, "")
                if expected and actual and expected != actual:
                    mismatches.append({
                        "guid": guid,
                        "token": token_key,
                        "expected": expected,
                        "actual": actual,
                        "ts": datetime.now(timezone.utc).isoformat(),
                    })
                    log.warning(
                        "Verify mismatch guid=%s %s: wrote %r, read back %r",
                        guid, token_key, expected, actual,
                    )

        return mismatches

    def _save_sync_errors(self, mismatches: list[dict]) -> None:
        """Persist write-back mismatches to sync_errors.json for the next run."""
        path = Path(self._sync_errors_path)
        existing: list[dict] = []
        if path.exists():
            try:
                with open(path, encoding="utf-8") as f:
                    existing = json.load(f)
            except Exception:
                existing = []
        # Keep last 500 entries
        combined = (existing + mismatches)[-500:]
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(combined, f, indent=2)
            log.info("Saved %d sync errors to %s", len(mismatches), path)
        except OSError as e:
            log.warning("Could not write sync_errors.json: %s", e)


def _apply_conflict_resolution(
    sync_elements: list[dict],
    write_pairs: list[tuple[str, dict]],
    ps_timestamps: dict[str, datetime],
    result: SyncResult,
) -> None:
    """
    For elements that exist in both AC and Planscape, compare timestamps.
    If Planscape record is newer than AC-derived tokens, overwrite the
    write_pair tokens with the Planscape values so AC stays consistent.
    AC's modification time is approximated as now (conservative — AC was
    just read, so its data is current as of this sync run).
    """
    now = datetime.now(timezone.utc)
    token_by_guid = {guid: tokens for guid, tokens in write_pairs}

    for el in sync_elements:
        guid = el.get("uniqueId", "")
        ps_ts = ps_timestamps.get(guid)
        if not ps_ts:
            continue  # not in Planscape yet — AC wins by default

        # AC data is current (just read), so compare against now
        # If Planscape was modified more recently than 60s ago it wins
        # (the 60s grace prevents flip-flopping on same-second writes)
        age_s = (now - ps_ts).total_seconds()
        if age_s < 60:
            # Planscape is newer — overwrite the tokens we're about to write
            # back to AC with the Planscape values
            ps_tokens = {
                "disc": el.get("disc", ""),
                "loc":  el.get("loc", ""),
                "zone": el.get("zone", ""),
                "lvl":  el.get("lvl", ""),
                "sys":  el.get("sys", ""),
                "func": el.get("func", ""),
                "prod": el.get("prod", ""),
                "seq":  el.get("seq", ""),
            }
            if guid in token_by_guid:
                token_by_guid[guid].update(ps_tokens)
            result.conflicts_ps_wins += 1
            log.info(
                "Conflict guid=%s: Planscape newer (%.0fs ago) — PS wins",
                guid, age_s,
            )
        else:
            result.conflicts_ac_wins += 1


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
