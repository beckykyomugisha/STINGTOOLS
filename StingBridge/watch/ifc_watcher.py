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

import hashlib
import json
import logging
import os
import time
from . import hot_folder
from pathlib import Path
from threading import Thread
from typing import Callable

log = logging.getLogger(__name__)


def _patch_ifcopenshell_del() -> None:
    """
    Silence the KeyError in ifcopenshell.file.__del__ that fires when the
    C++ file handle is freed before Python GC runs __del__.

    Root cause: temporary file objects created inside ifcopenshell.open()
    are freed on the C++ side before Python GC calls __del__, leaving a
    stale key in the internal file_dict.  The fix must happen BEFORE open()
    is called so the patch covers those temporaries.

    ifcopenshell/__init__.py does `from .file import file`, so
    `ifcopenshell.file` IS the class, not the submodule.  We patch __del__
    on that class directly.
    """
    try:
        import ifcopenshell as _ifc  # type: ignore
        # ifcopenshell.file is the class (re-exported from ifcopenshell/file.py)
        file_cls = _ifc.file
        if not isinstance(file_cls, type):
            # Unexpected — fall through to submodule path
            raise AttributeError("ifcopenshell.file is not a class")
    except Exception:
        try:
            import importlib
            _mod = importlib.import_module("ifcopenshell.file")
            file_cls = getattr(_mod, "file", None)
            if file_cls is None or not isinstance(file_cls, type):
                log.warning("ifcopenshell.file class not found — __del__ patch skipped")
                return
        except Exception as e:
            log.warning("ifcopenshell __del__ patch failed: %s", e)
            return

    if getattr(file_cls, "_sting_del_patched", False):
        return  # already patched

    _orig_del = getattr(file_cls, "__del__", None)
    if _orig_del is None:
        return

    def _safe_del(self, _orig=_orig_del):
        try:
            _orig(self)
        except Exception:
            pass  # swallow KeyError from stale C++ pointer

    try:
        file_cls.__del__ = _safe_del
        file_cls._sting_del_patched = True
        log.debug("ifcopenshell %s.__del__ patched OK", file_cls.__name__)
    except (TypeError, AttributeError) as e:
        log.debug("ifcopenshell __del__ patch not applicable: %s", e)


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

        _patch_ifcopenshell_del()  # must run before open() to cover internal temporaries
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

        rows: list[tuple[dict, dict]] = []
        for el in elements:
            tokens = map_element_to_tokens(
                element_type=el["ifc_type"],
                props=el["props"],
                storey_name=el["storey_name"],
                storey_elevation_m=el["storey_elevation_m"],
                library_part_name=el.get("type_name", ""),
            )
            # Adopt a SEQ the element already carries from an earlier write-back.
            # The extractor reads STING_TOKENS.ASS_SEQ_NUM_TXT back into
            # props["STING.ASS_SEQ_NUM_TXT"] (see _extract), but
            # map_element_to_tokens does not derive `seq` — it is minted, not
            # mapped. Without this seed assign_sequences() sees an unnumbered
            # element on every re-drop and mints a NEW number, burning counter
            # values and renumbering stable elements. The live ArchiCAD path
            # already adopts it this way (sync/engine.py:401-413); this keeps the
            # IFC path honest to the same contract.
            existing_seq = str(el["props"].get("STING.ASS_SEQ_NUM_TXT", "") or "").strip()
            if existing_seq:
                tokens["seq"] = existing_seq
            rows.append((el, tokens))

        # SB-2 — mint the 8th tag segment from the server's per-key counters,
        # in one batched call for the whole file. Elements that already carry a
        # SEQ are skipped, so re-dropping the same IFC neither renumbers them
        # nor consumes counter values. If the server cannot reserve, tags stay
        # 7-segment rather than the file failing to process.
        from ..sync.seq_minter import assign_sequences
        try:
            minted = assign_sequences(self._ps, [t for _, t in rows])
            if minted:
                self._emit(f"Minted {minted} SEQ number(s)")
        except Exception as e:  # noqa: BLE001 — numbering must never fail an ingest
            self._emit(f"SEQ minting skipped: {e}")

        for el, tokens in rows:
            is_complete = bool(
                tokens.get("disc") and tokens.get("lvl") and
                tokens.get("sys") and tokens.get("prod")
            )
            from ..planscape.client import PlanscapeClient
            # The watcher parses an exported IFC, so el["guid"] is the true IFC
            # GlobalId and is also the only host-side id available — it serves
            # as both the cross-host key and HostElementId.
            sync_el = PlanscapeClient.build_ifc_element(
                ifc_global_id=el["guid"],
                host_element_id=el["guid"],
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
        # H-3/identity — stable per-document id so the same IfcGlobalId in
        # different source documents (federated / hot-linked models) keeps
        # distinct ExternalElementMapping rows instead of overwriting one. The
        # composite key is (ProjectId, IfcGlobalId, Host, HostDocumentGuid);
        # leaving the doc guid null collapsed every federated doc together.
        # Derived from the resolved file path (≤64 chars to match the column).
        doc_guid = hashlib.sha1(str(path.resolve()).lower().encode("utf-8")).hexdigest()
        batch_size = 100
        for i in range(0, len(sync_payloads), batch_size):
            batch = sync_payloads[i: i + batch_size]
            try:
                self._ps.ingest_ifc_data(batch, host="archicad", host_document_guid=doc_guid)
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
        try:
            instances = model.by_type(ifc_class)
        except RuntimeError:
            continue  # class not present in this IFC schema version (e.g. IFC2X3 vs IFC4)
        for el in instances:
            if not el.GlobalId:
                continue

            # Collect property sets into a flat bag
            props: dict[str, str] = {}
            try:
                for pset_name, pset in ifc_util.get_psets(el).items():
                    for k, v in pset.items():
                        str_v = str(v) if v is not None else ""
                        # Qualified key always wins; bare key only if not already set
                        # (first pset that defines a bare key keeps it — avoids collision)
                        qualified = f"{pset_name}.{k}"
                        props[qualified] = str_v
                        if k not in props:
                            props[k] = str_v
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
    Uses direct entity creation to avoid the owner history requirement of
    the ifcopenshell.api pset helpers.
    """
    import ifcopenshell  # type: ignore
    import ifcopenshell.guid  # type: ignore
    import ifcopenshell.util.element as ifc_util  # type: ignore

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
            # Create pset directly without owner history requirement
            props_to_add = [
                model.create_entity(
                    "IfcPropertySingleValue",
                    Name=_TOKEN_LABELS[k],
                    NominalValue=model.create_entity("IfcLabel", wrappedValue=str(v)),
                )
                for k, v in tokens.items()
                if k in _TOKEN_LABELS and v
            ]
            if props_to_add:
                pset = model.create_entity(
                    "IfcPropertySet",
                    GlobalId=ifcopenshell.guid.new(),
                    Name="STING_TOKENS",
                    HasProperties=props_to_add,
                )
                model.create_entity(
                    "IfcRelDefinesByProperties",
                    GlobalId=ifcopenshell.guid.new(),
                    RelatedObjects=[el],
                    RelatingPropertyDefinition=pset,
                )

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

    def __init__(self, handler: IFCDropHandler, debounce_s: float = 3.0,
                 drop_root: Path | None = None):
        self._handler = handler
        self._debounce = debounce_s
        # When set, files move through processing/ -> done/|failed/ (SB-4).
        # None keeps the legacy in-place behaviour for one-off processing.
        self._drop_root = drop_root
        self._pending: dict[str, float] = {}
        self._lock = __import__("threading").Lock()
        self._thread: Thread | None = None
        self._running = False

    # Called by watchdog (may be on a different thread from _drain_loop)
    def dispatch(self, event) -> None:
        if getattr(event, "is_directory", False):
            return
        src = getattr(event, "src_path", "")
        if not src.lower().endswith(".ifc"):
            return
        if "_sting" in Path(src).stem:
            return  # our own output
        if self._drop_root is not None and hot_folder.is_in_managed_subfolder(
                Path(src), self._drop_root):
            return  # already claimed, archived, or failed
        with self._lock:
            self._pending[src] = time.monotonic()

    def enqueue(self, path: str) -> None:
        """Queue a path directly (start-up sweep), bypassing watchdog."""
        with self._lock:
            self._pending.setdefault(path, time.monotonic())

    def start_drainer(self) -> None:
        self._running = True
        self._thread = Thread(target=self._drain_loop, daemon=True)
        self._thread.start()

    def stop_drainer(self) -> None:
        self._running = False

    def _drain_loop(self) -> None:
        while self._running:
            now = time.monotonic()
            with self._lock:
                ready = [p for p, t in self._pending.items() if now - t >= self._debounce]
                for path in ready:
                    del self._pending[path]
            for path in ready:
                self._process_one(Path(path))
            time.sleep(0.5)

    def _process_one(self, src: Path) -> None:
        """Run one file through the processing/ -> done/|failed/ lifecycle.

        SB-4 — this mirrors StingBridge/src/IFC/IfcDropWatcher.cs so both
        watchers leave the same folder in the same state. Claiming into
        processing/ first is what makes "still outstanding" answerable and stops
        a re-run reprocessing everything.
        """
        root = self._drop_root
        if root is None:
            # No managed root (single-file `process-ifc`): keep the old
            # in-place behaviour, sidecar included.
            try:
                result = self._handler.process(str(src))
                _save_result(str(src), result)
            except Exception as exc:
                log.exception("IFC processing failed for %s: %s", src, exc)
            return

        claimed = hot_folder.claim(src, root)
        if claimed is None:
            return  # someone else owns it, or it vanished

        try:
            result = self._handler.process(str(claimed))
            _save_result(str(claimed), result)
            if _is_failure(result):
                # process() reports parse/sync failures in result["errors"]
                # rather than raising, so routing on exceptions alone would
                # archive an unopenable file as a success.
                hot_folder.fail(claimed, root, "; ".join(result.get("errors") or []))
            else:
                hot_folder.complete(claimed, root)
        except Exception as exc:
            log.exception("IFC processing failed for %s: %s", claimed.name, exc)
            try:
                hot_folder.fail(claimed, root, f"{type(exc).__name__}: {exc}")
            except OSError as move_exc:
                # Leave it in processing/ — recover_orphans will return it to the
                # root on the next start rather than the file being lost.
                log.error("Could not move %s to failed/: %s", claimed.name, move_exc)


def _is_failure(result: dict) -> bool:
    """True when a drop produced nothing usable and should land in failed/.

    The bar is "errors AND nothing synced": a file that yielded elements is a
    successful drop even if something secondary (write-back, GLB conversion)
    complained — the sidecar records those. A file that parsed to nothing, or
    whose elements were all rejected, is a failure the operator needs to see
    in failed/ rather than buried in a done/ sidecar.
    """
    if not (result.get("errors") or []):
        return False
    return int(result.get("synced") or 0) == 0


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
    drop_path = Path(drop_dir)
    hot_folder.ensure_layout(drop_path)
    # A run killed mid-file leaves that file invisible to every future run:
    # gone from the root, and nothing moves it out of processing/.
    recovered = hot_folder.recover_orphans(drop_path)
    if recovered and on_progress:
        on_progress(f"Recovered {recovered} file(s) left in processing/ by a previous run")

    handler = IFCDropHandler(planscape_client, config, on_progress)
    ev_handler = _IFCEventHandler(handler, drop_root=drop_path)

    # Make watchdog call our dispatch method
    class _Adapter(FileSystemEventHandler):
        def dispatch(self, event):
            ev_handler.dispatch(event)

    observer = Observer()
    observer.schedule(_Adapter(), str(drop_path), recursive=False)
    observer.start()
    ev_handler.start_drainer()

    # Sweep files that were already sitting in the root — dropped while the
    # watcher was down, or present before it started. watchdog only reports
    # *events*, so without this they would wait for someone to touch them again.
    # Safe now that processed files move out of the root (SB-4): before that,
    # a start-up sweep would have reprocessed the entire folder every run.
    for existing in sorted(drop_path.glob("*.ifc")):
        if existing.is_file() and "_sting" not in existing.stem:
            ev_handler.enqueue(str(existing))

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
