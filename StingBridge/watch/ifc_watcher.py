"""
IFC drop-folder watcher.

Watches a directory for new or modified .ifc files and processes them
via IfcOpenShell — no running ArchiCAD session required.

Usage (standalone):
    python -m StingBridge.watch.ifc_watcher --drop-dir /path/to/IFC_DROP

Usage (from bridge.py):
    python -m StingBridge.bridge watch-ifc --drop-dir /path/to/IFC_DROP
"""
from __future__ import annotations

import json
import logging
import os
import time
from pathlib import Path
from threading import Thread
from typing import Callable

log = logging.getLogger(__name__)


def _patch_ifcopenshell_del() -> None:
    """
    Patch ifcopenshell.file.__del__ to swallow the KeyError that fires when
    the C++ file handle is freed before Python GC runs __del__.
    This is a known upstream bug; the patch is safe because the only thing
    __del__ does is remove the entry from an internal dict — which we let it
    do, silently ignoring the case where the key is already gone.
    """
    try:
        import ifcopenshell.file as _ifc_file_mod  # type: ignore
        _orig_del = _ifc_file_mod.file.__del__

        def _safe_del(self):
            try:
                _orig_del(self)
            except (KeyError, Exception):
                pass

        _ifc_file_mod.file.__del__ = _safe_del
    except Exception:
        pass  # ifcopenshell not installed or API changed — ignore


class IFCDropHandler:
    """
    Handles a single IFC file: parses it with IfcOpenShell, maps STING
    tokens, syncs to Planscape, writes tokens back into the IFC as a
    STING_TOKENS IfcPropertySet, and converts to GLB for the viewer.
    """

    def __init__(
        self,
        planscape_client,
        config,
        on_progress: Callable[[str], None] | None = None,
    ):
        self._ps = planscape_client
        self._cfg = config
        self._on_progress = on_progress or (lambda msg: None)

    # ── public ────────────────────────────────────────────────────────────────

    def process(self, ifc_path: str) -> dict:
        """
        Full pipeline for one IFC file.
        Returns a result dict: {elements, synced, written_back, glb_path, errors}.
        """
        try:
            import ifcopenshell  # type: ignore
        except ImportError:
            raise RuntimeError(
                "ifcopenshell is not installed. Run: pip install ifcopenshell"
            )

        path = Path(ifc_path)
        result: dict = {
            "ifc_path": str(path),
            "elements": 0,
            "synced": 0,
            "written_back": False,
            "glb_path": None,
            "errors": [],
        }

        self._emit(f"Opening {path.name}…")
        try:
            model = ifcopenshell.open(str(path))
        except Exception as exc:
            result["errors"].append(f"IFC open failed: {exc}")
            return result

        result = self._process_model(model, path, result)
        return result

    def _process_model(self, model, path: Path, result: dict) -> dict:
        """Inner pipeline — called with model already open."""
        # ── Extract elements ──────────────────────────────────────────────────
        self._emit("Extracting elements…")
        elements = _extract_elements(model)
        result["elements"] = len(elements)
        self._emit(f"Found {len(elements)} elements")

        if not elements:
            self._emit(
                "No recognised IFC elements found — check IFC schema or element types. "
                f"Supported: {', '.join(_IFC_TO_STING_TYPE)}"
            )
            return result

        # ── Map STING tokens ──────────────────────────────────────────────────
        self._emit("Mapping STING tokens…")
        from ..sync.token_mapper import map_element_to_tokens
        sync_payloads = []
        token_map: dict[str, dict] = {}  # guid → tokens (for write-back)

        for el in elements:
            tokens = map_element_to_tokens(
                element_type=el["ifc_type"],
                props=el["props"],
                storey_name=el["storey_name"],
                storey_elevation_m=el["storey_elevation_m"],
                library_part_name=el.get("type_name", ""),
            )
            is_complete = bool(
                tokens.get("disc") and tokens.get("lvl") and
                tokens.get("sys") and tokens.get("prod")
            )
            from ..planscape.client import PlanscapeClient
            sync_el = PlanscapeClient.build_element_sync(
                guid=el["guid"],
                disc=tokens.get("disc", ""),
                loc=tokens.get("loc", ""),
                zone=tokens.get("zone", ""),
                lvl=tokens.get("lvl", ""),
                sys=tokens.get("sys", ""),
                func=tokens.get("func", ""),
                prod=tokens.get("prod", ""),
                seq=tokens.get("seq", ""),
                category_name=el["ifc_type"],
                family_name=el.get("type_name", ""),
                status=tokens.get("status"),
                is_complete=is_complete,
            )
            sync_payloads.append(sync_el)
            token_map[el["guid"]] = tokens

        # ── Sync to Planscape ─────────────────────────────────────────────────
        self._emit(f"Syncing {len(sync_payloads)} elements to Planscape…")
        from ..planscape.client import PlanscapeError
        batch_size = 100
        for i in range(0, len(sync_payloads), batch_size):
            batch = sync_payloads[i: i + batch_size]
            try:
                self._ps.sync_elements(batch, source="archicad-ifc")
                result["synced"] += len(batch)
            except (PlanscapeError, Exception) as exc:
                msg = f"Planscape sync batch {i // batch_size} failed: {exc}"
                result["errors"].append(msg)
                log.error(msg)

        self._emit(f"Synced {result['synced']} elements")

        # ── Write tokens back into the IFC as IfcPropertySet ─────────────────
        self._emit("Writing STING tokens back to IFC…")
        try:
            tagged_path = _write_tokens_to_ifc(model, token_map, path)
            result["written_back"] = True
            self._emit(f"Saved tagged IFC → {tagged_path.name}")
        except Exception as exc:
            msg = f"IFC token write-back failed: {exc}"
            result["errors"].append(msg)
            log.warning(msg)

        # ── Convert to GLB for Planscape viewer ───────────────────────────────
        glb_path = self._convert_to_glb(path, result)
        if glb_path:
            result["glb_path"] = str(glb_path)
            self._upload_glb(glb_path, result)

        return result

    # ── private helpers ───────────────────────────────────────────────────────

    def _emit(self, msg: str) -> None:
        log.info(msg)
        self._on_progress(msg)

    def _convert_to_glb(self, ifc_path: Path, result: dict) -> Path | None:
        """Try IfcConvert → GLB. Returns GLB path or None."""
        glb_path = ifc_path.with_suffix(".glb")
        ifc_convert = _find_ifc_convert()
        if not ifc_convert:
            log.info("IfcConvert not found — skipping GLB conversion")
            return None

        import subprocess
        self._emit(f"Converting to GLB ({glb_path.name})…")
        try:
            subprocess.run(
                [ifc_convert, str(ifc_path), str(glb_path),
                 "--use-element-guids", "--threads=4"],
                check=True,
                capture_output=True,
                timeout=300,
            )
            self._emit(f"GLB ready: {glb_path.name}")
            return glb_path
        except Exception as exc:
            msg = f"GLB conversion failed: {exc}"
            result["errors"].append(msg)
            log.warning(msg)
            return None

    def _upload_glb(self, glb_path: Path, result: dict) -> None:
        """Upload the GLB to Planscape /api/projects/{id}/models if configured."""
        project_id = self._cfg.planscape_project_id
        if not project_id:
            log.info("No project_id configured — skipping GLB upload")
            return

        self._emit(f"Uploading GLB to Planscape…")
        try:
            self._ps.upload_model(project_id, glb_path)
            self._emit("GLB uploaded ✓")
        except Exception as exc:
            msg = f"GLB upload failed: {exc}"
            result["errors"].append(msg)
            log.warning(msg)


# ── IFC parsing helpers ────────────────────────────────────────────────────────

# Map IFC class names → STING element type names used by token_mapper
_IFC_TO_STING_TYPE: dict[str, str] = {
    "IfcWall": "Wall", "IfcWallStandardCase": "Wall",
    "IfcSlab": "Slab", "IfcRoof": "Roof",
    "IfcColumn": "Column", "IfcBeam": "Beam",
    "IfcDoor": "Door", "IfcWindow": "Window",
    "IfcStair": "Stair", "IfcRailing": "Railing",
    "IfcCurtainWall": "CurtainWall",
    "IfcAirTerminal": "AirTerminal",
    "IfcDuctSegment": "Duct", "IfcDuctFitting": "Duct",
    "IfcPipeSegment": "Pipe", "IfcPipeFitting": "Pipe",
    "IfcCableSegment": "CableTray", "IfcCableFitting": "CableTray",
    "IfcLightFixture": "LightFixture",
    "IfcElectricDistributionBoard": "ElectricalEquipment",
    "IfcSanitaryTerminal": "Plumbing",
    "IfcFireSuppressionTerminal": "Sprinkler",
    "IfcZone": "Zone",
    "IfcSpace": "Room",
    "IfcFurnishingElement": "Furniture",
    "IfcMember": "StructuralMember",
    "IfcFooting": "Foundation",
}


def _extract_elements(model) -> list[dict]:
    """
    Extract elements from an IfcOpenShell model.
    Returns list of dicts with guid, ifc_type, storey_name,
    storey_elevation_m, props, type_name.
    """
    import ifcopenshell.util.element as ifc_util  # type: ignore

    # Build storey lookup: element → storey
    storey_cache: dict[str, tuple[str, float | None]] = {}

    def _storey_for(el) -> tuple[str, float | None]:
        eid = el.GlobalId
        if eid in storey_cache:
            return storey_cache[eid]
        name, elev = "", None
        try:
            storey = ifc_util.get_container(el)
            while storey and storey.is_a() not in ("IfcBuildingStorey", "IfcSite"):
                storey = ifc_util.get_container(storey)
            if storey and storey.is_a("IfcBuildingStorey"):
                name = storey.Name or ""
                elev = getattr(storey, "Elevation", None)
        except Exception:
            pass
        storey_cache[eid] = (name, elev)
        return name, elev

    elements: list[dict] = []

    for ifc_class, sting_type in _IFC_TO_STING_TYPE.items():
        for el in model.by_type(ifc_class):
            if not el.GlobalId:
                continue

            # Collect property sets into a flat bag
            props: dict[str, str] = {}
            try:
                for pset_name, pset in ifc_util.get_psets(el).items():
                    for k, v in pset.items():
                        props[f"{pset_name}.{k}"] = str(v) if v is not None else ""
                        # Also expose without pset prefix for simpler lookup
                        props[k] = str(v) if v is not None else ""
            except Exception:
                pass

            # Existing STING tokens (from a previous write-back)
            for token_key in [
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT", "ASS_TAG_1",
            ]:
                v = props.get(f"STING_TOKENS.{token_key}", "")
                if v:
                    props[f"STING.{token_key}"] = v

            storey_name, storey_elev = _storey_for(el)

            type_name = ""
            try:
                el_type = ifc_util.get_type(el)
                if el_type:
                    type_name = el_type.Name or ""
            except Exception:
                pass

            elements.append({
                "guid": el.GlobalId,
                "ifc_type": sting_type,
                "storey_name": storey_name,
                "storey_elevation_m": storey_elev,
                "props": props,
                "type_name": type_name,
            })

    return elements


def _write_tokens_to_ifc(model, token_map: dict[str, dict], src_path: Path) -> Path:
    """
    Write STING tokens back into the IFC model as an IfcPropertySet
    named "STING_TOKENS" on each element, then save to <name>_sting.ifc.
    """
    import ifcopenshell.api  # type: ignore
    import ifcopenshell.util.element as ifc_util  # type: ignore

    owner_history = model.by_type("IfcOwnerHistory")[0] if model.by_type("IfcOwnerHistory") else None

    _TOKEN_LABELS: dict[str, str] = {
        "disc": "ASS_DISCIPLINE_COD_TXT",
        "loc":  "ASS_LOC_TXT",
        "zone": "ASS_ZONE_TXT",
        "lvl":  "ASS_LVL_COD_TXT",
        "sys":  "ASS_SYSTEM_TYPE_TXT",
        "func": "ASS_FUNC_TXT",
        "prod": "ASS_PRODCT_COD_TXT",
        "seq":  "ASS_SEQ_NUM_TXT",
        "tag1": "ASS_TAG_1",
        "status": "ASS_STATUS_TXT",
    }

    for el in model.by_type("IfcProduct"):
        guid = el.GlobalId
        tokens = token_map.get(guid)
        if not tokens:
            continue

        # Find or create STING_TOKENS pset
        existing_psets = ifc_util.get_psets(el)
        if "STING_TOKENS" in existing_psets:
            # Update existing
            for pset in el.IsDefinedBy:
                if hasattr(pset, "RelatingPropertyDefinition"):
                    pdef = pset.RelatingPropertyDefinition
                    if hasattr(pdef, "Name") and pdef.Name == "STING_TOKENS":
                        _update_pset_properties(model, pdef, tokens, _TOKEN_LABELS)
                        break
        else:
            # Create new pset
            ifcopenshell.api.run(
                "pset.add_pset",
                model,
                product=el,
                name="STING_TOKENS",
            )
            # Re-fetch and populate
            for pset in el.IsDefinedBy:
                if hasattr(pset, "RelatingPropertyDefinition"):
                    pdef = pset.RelatingPropertyDefinition
                    if hasattr(pdef, "Name") and pdef.Name == "STING_TOKENS":
                        _update_pset_properties(model, pdef, tokens, _TOKEN_LABELS)
                        break

    out_path = src_path.with_name(src_path.stem + "_sting.ifc")
    model.write(str(out_path))
    return out_path


def _update_pset_properties(model, pset, tokens: dict, labels: dict[str, str]) -> None:
    """Add or overwrite string properties on an IfcPropertySet."""
    import ifcopenshell.api  # type: ignore

    existing = {p.Name: p for p in getattr(pset, "HasProperties", [])}
    props_to_set = {
        labels[k]: str(v)
        for k, v in tokens.items()
        if k in labels and v
    }
    ifcopenshell.api.run(
        "pset.edit_pset",
        model,
        pset=pset,
        properties=props_to_set,
    )


def _find_ifc_convert() -> str | None:
    """Search common paths for the IfcConvert binary."""
    candidates = [
        os.getenv("IFC_CONVERT_PATH", ""),
        "/usr/local/bin/IfcConvert",
        "/usr/bin/IfcConvert",
        "IfcConvert",  # on PATH
        r"C:\Program Files\IfcOpenShell\IfcConvert.exe",
    ]
    import shutil
    for c in candidates:
        if c and (shutil.which(c) or Path(c).is_file()):
            return c
    return None


# ── Watchdog integration ───────────────────────────────────────────────────────

class _IFCEventHandler:
    """watchdog FileSystemEventHandler that debounces IFC file events."""

    def __init__(self, handler: IFCDropHandler, debounce_s: float = 3.0):
        self._handler = handler
        self._debounce = debounce_s
        self._pending: dict[str, float] = {}
        self._thread: Thread | None = None
        self._running = False

    # Called by watchdog
    def dispatch(self, event) -> None:
        if getattr(event, "is_directory", False):
            return
        src = getattr(event, "src_path", "")
        if src.lower().endswith(".ifc"):
            self._pending[src] = time.monotonic()

    def start_drainer(self) -> None:
        self._running = True
        self._thread = Thread(target=self._drain_loop, daemon=True)
        self._thread.start()

    def stop_drainer(self) -> None:
        self._running = False

    def _drain_loop(self) -> None:
        while self._running:
            now = time.monotonic()
            ready = [p for p, t in list(self._pending.items()) if now - t >= self._debounce]
            for path in ready:
                del self._pending[path]
                try:
                    result = self._handler.process(path)
                    _save_result(path, result)
                except Exception as exc:
                    log.exception("IFC processing failed for %s: %s", path, exc)
            time.sleep(0.5)


def _save_result(ifc_path: str, result: dict) -> None:
    """Write sync result to a sidecar JSON file next to the IFC."""
    out = Path(ifc_path).with_suffix(".sync_result.json")
    try:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(result, f, indent=2, default=str)
    except OSError:
        pass


def watch_drop_folder(
    drop_dir: str,
    planscape_client,
    config,
    on_progress: Callable[[str], None] | None = None,
) -> None:
    """
    Block forever watching drop_dir for IFC files.
    Press Ctrl-C to stop.
    """
    try:
        from watchdog.observers import Observer  # type: ignore
        from watchdog.events import FileSystemEventHandler  # type: ignore
    except ImportError:
        raise RuntimeError("watchdog is not installed. Run: pip install watchdog")

    _patch_ifcopenshell_del()  # suppress upstream __del__ KeyError before any model opens
    handler = IFCDropHandler(planscape_client, config, on_progress)
    ev_handler = _IFCEventHandler(handler)

    # Make watchdog call our dispatch method
    class _Adapter(FileSystemEventHandler):
        def dispatch(self, event):
            ev_handler.dispatch(event)

    drop_path = Path(drop_dir)
    drop_path.mkdir(parents=True, exist_ok=True)

    observer = Observer()
    observer.schedule(_Adapter(), str(drop_path), recursive=False)
    observer.start()
    ev_handler.start_drainer()

    log.info("Watching for IFC files in: %s", drop_path)
    if on_progress:
        on_progress(f"Watching {drop_path} …")

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        pass
    finally:
        observer.stop()
        observer.join()
        ev_handler.stop_drainer()
