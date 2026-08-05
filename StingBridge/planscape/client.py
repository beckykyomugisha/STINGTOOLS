"""
Planscape REST API client (ArchiCAD bridge).

Primary path: POST /api/projects/{id}/ifc/data with Host="archicad", the
true IFC GlobalId as the cross-host key, and the ArchiCAD element GUID as
HostElementId — so ArchiCAD elements produce real ExternalElementMapping
rows and resolve across hosts (Drift 5 fix).

The element→wire (snake_case→camelCase) shaping is single-sourced from
``stingtools_core.planscape.PlanscapeClient._element_to_wire`` rather than
re-implemented here — two divergent copies of the same wire contract is
exactly how Drift 1 and Drift 5 happened independently. The legacy
``sync_elements`` → /api/tagsync/sync path (which fabricated a Revit id
from md5(guid)) is retained but DEPRECATED for backwards-compat.
"""
from __future__ import annotations

import logging
import time
from datetime import datetime, timezone
from pathlib import Path as _Path
from typing import Any, Callable

import requests

log = logging.getLogger(__name__)

_DEFAULT_BASE = "http://localhost:5000"
_TIMEOUT = 30


def _load_element_to_wire() -> Callable[[dict], dict]:
    """Return the core client's ``_element_to_wire`` staticmethod, importing
    ``stingtools_core`` from the installed package or — in the monorepo dev
    checkout where it isn't pip-installed — from the sibling
    ``stingtools-core/python`` source tree. Reusing it (rather than copying
    the field map) keeps the ArchiCAD bridge and every other host on ONE
    wire contract."""
    try:
        from stingtools_core.planscape.client import PlanscapeClient as _Core
    except ImportError:
        # repo root = StingBridge/planscape/client.py → parents[2]
        core_src = _Path(__file__).resolve().parents[2] / "stingtools-core" / "python"
        import sys
        if core_src.is_dir() and str(core_src) not in sys.path:
            sys.path.insert(0, str(core_src))
        from stingtools_core.planscape.client import PlanscapeClient as _Core
    return _Core._element_to_wire


class PlanscapeAuthError(Exception):
    pass


class PlanscapeError(Exception):
    pass


class PlanscapeClient:
    """Client for Planscape Server REST API."""

    def __init__(self, base_url: str = _DEFAULT_BASE, project_id: str = ""):
        self.base_url = base_url.rstrip("/")
        self.project_id = project_id
        self._session = requests.Session()
        self._session.headers.update({"Content-Type": "application/json"})
        self._token: str | None = None
        self._token_expiry: float = 0.0

    # ── auth ─────────────────────────────────────────────────────────────────

    def login(self, email: str, password: str) -> None:
        resp = self._session.post(
            f"{self.base_url}/api/auth/login",
            json={"email": email, "password": password},
            timeout=_TIMEOUT,
        )
        if resp.status_code == 401:
            raise PlanscapeAuthError("Invalid credentials")
        resp.raise_for_status()
        data = resp.json()
        self._token = data.get("token") or data.get("accessToken")
        if not self._token:
            raise PlanscapeAuthError("No token in login response")
        # Assume 60-minute expiry; refresh 5 minutes early
        self._token_expiry = time.time() + 55 * 60
        self._session.headers.update({"Authorization": f"Bearer {self._token}"})
        log.info("Planscape login successful")

    def _ensure_auth(self, email: str, password: str) -> None:
        if not self._token or time.time() >= self._token_expiry:
            self.login(email, password)

    # ── substrate drift-check (Phase A4) ─────────────────────────────────────

    def get_substrate_manifest(self) -> dict:
        """GET /api/substrate/manifest — the server's global substrate hash.

        Returns ``{ "sha256", "schemaVersion", "totalEnums" }``. Used by the
        worker-startup drift-check so a bridge running on a stale/forked
        shared/ifc copy is warned before it ingests against divergent enums.
        """
        resp = self._session.get(
            f"{self.base_url}/api/substrate/manifest", timeout=_TIMEOUT
        )
        if resp.status_code == 401:
            raise PlanscapeAuthError("Token expired or invalid")
        resp.raise_for_status()
        return resp.json()

    # ── sync ─────────────────────────────────────────────────────────────────

    def ingest_ifc_data(
        self,
        elements: list[dict],
        host: str = "archicad",
        host_document_guid: str | None = None,
        plugin_version: str = "stingbridge",
        user_name: str = "",
    ) -> dict:
        """POST /api/projects/{id}/ifc/data — the cross-host ingest path.

        ``elements`` are snake_case dicts (see ``build_ifc_element``) carrying
        the true ``ifc_global_id`` + ``host_element_id``. They are shaped to
        the server's ``IfcElementDto`` (camelCase) via the shared
        ``_element_to_wire`` — NOT a local copy. The server upserts an
        ``ExternalElementMapping`` (keyed on Host + IfcGlobalId +
        HostDocumentGuid) plus the ``TaggedElement`` projection, so ArchiCAD
        elements are visible to cross-host resolution and carry no fabricated
        Revit id.
        """
        if not self._token:
            raise PlanscapeAuthError("Not logged in — call login() first")
        if not self.project_id:
            raise PlanscapeError("project_id not set")

        to_wire = _load_element_to_wire()
        payload = {
            "host": host,
            "hostDocumentGuid": host_document_guid,
            "pluginVersion": plugin_version,
            "userName": user_name,
            "elements": [to_wire(e) for e in elements],
        }
        resp = self._session.post(
            f"{self.base_url}/api/projects/{self.project_id}/ifc/data",
            json=payload,
            timeout=_TIMEOUT,
        )
        if resp.status_code == 401:
            raise PlanscapeAuthError("Token expired or invalid")
        resp.raise_for_status()
        return resp.json()

    def sync_elements(
        self,
        elements: list[dict],
        source: str = "archicad",
    ) -> dict:
        """
        DEPRECATED — use :meth:`ingest_ifc_data`.

        POST /api/tagsync/sync. This path fabricated a ``revitElementId`` from
        md5(guid) (see the deprecated :meth:`build_element_sync`), so ArchiCAD
        elements landed as pseudo-Revit rows with no ExternalElementMapping
        and were invisible to cross-host resolution. Retained only so any
        out-of-tree caller keeps working; all in-tree callers now use
        :meth:`ingest_ifc_data`.
        """
        if not self._token:
            raise PlanscapeAuthError("Not logged in — call login() first")
        if not self.project_id:
            raise PlanscapeError("project_id not set")

        payload = {
            "projectId": self.project_id,
            "source": source,
            "lastSyncUtc": datetime.now(timezone.utc).isoformat(),
            "elements": elements,
        }
        resp = self._session.post(
            f"{self.base_url}/api/tagsync/sync",
            json=payload,
            timeout=_TIMEOUT,
        )
        if resp.status_code == 401:
            raise PlanscapeAuthError("Token expired or invalid")
        resp.raise_for_status()
        return resp.json()

    def get_element_timestamps(self, guids: list[str]) -> dict[str, datetime]:
        """
        Fetch lastModifiedUtc for a list of element GUIDs from Planscape.
        Returns {guid: datetime} for elements that exist in the project.
        Falls back gracefully — returns {} on any error.
        """
        if not self._token or not self.project_id or not guids:
            return {}
        try:
            resp = self._session.post(
                f"{self.base_url}/api/projects/{self.project_id}/tagsync/timestamps",
                json={"guids": guids},
                timeout=_TIMEOUT,
            )
            if resp.status_code == 404:
                # Endpoint not yet deployed — silently skip conflict detection
                return {}
            resp.raise_for_status()
            raw: dict[str, str] = resp.json()
            result: dict[str, datetime] = {}
            for guid, ts_str in raw.items():
                try:
                    result[guid] = datetime.fromisoformat(
                        ts_str.replace("Z", "+00:00")
                    )
                except (ValueError, TypeError):
                    pass
            return result
        except Exception as e:
            log.debug("get_element_timestamps failed (non-fatal): %s", e)
            return {}

    def upload_model(self, project_id: str, glb_path) -> dict:
        """Upload a GLB file to Planscape /api/projects/{id}/models."""
        import mimetypes
        from pathlib import Path
        path = Path(glb_path)
        mime = mimetypes.guess_type(str(path))[0] or "model/gltf-binary"
        with open(path, "rb") as f:
            resp = self._session.post(
                f"{self.base_url}/api/projects/{project_id}/models",
                files={"file": (path.name, f, mime)},
                timeout=120,
            )
        resp.raise_for_status()
        return resp.json()

    def get_compliance(self) -> dict:
        """GET latest compliance snapshot for the project."""
        resp = self._session.get(
            f"{self.base_url}/api/projects/{self.project_id}/compliance",
            timeout=_TIMEOUT,
        )
        resp.raise_for_status()
        return resp.json()

    # ── tag element sync helpers ──────────────────────────────────────────────

    @staticmethod
    def build_ifc_element(
        ifc_global_id: str,
        host_element_id: str | None = None,
        disc: str = "",
        loc: str = "",
        zone: str = "",
        lvl: str = "",
        sys: str = "",
        func: str = "",
        prod: str = "",
        seq: str = "",
        category_name: str = "",
        family_name: str = "",
        status: str | None = None,
        is_complete: bool = False,
        extra: dict | None = None,
    ) -> dict:
        """Build one snake_case element dict for :meth:`ingest_ifc_data`.

        Keys match the source names ``_element_to_wire`` maps onto the server
        ``IfcElementDto`` — no fabricated ``revitElementId``.

        ``host_element_id`` defaults to ``ifc_global_id`` when the caller has
        no distinct host-side id (the IFC-file watcher only ever sees the IFC
        GlobalId, so the GlobalId IS its host identifier).
        """
        segments = [disc, loc, zone, lvl, sys, func, prod, seq]
        full_tag = "-".join(s for s in segments if s)

        d: dict[str, Any] = {
            "ifc_global_id": ifc_global_id,
            "host_element_id": host_element_id or ifc_global_id,
            "discipline": disc,
            "location": loc,
            "zone": zone,
            "level": lvl,
            "system": sys,
            "function": func,
            "product": prod,
            "sequence": seq,
            "full_tag": full_tag,
            "category_name": category_name,
            "family_name": family_name,
            "is_complete": is_complete,
            "last_modified_utc": datetime.now(timezone.utc).isoformat(),
        }
        if status is not None:
            d["status"] = status
        if extra:
            d.update(extra)
        return d

    @staticmethod
    def build_element_sync(
        guid: str,
        disc: str = "",
        loc: str = "",
        zone: str = "",
        lvl: str = "",
        sys: str = "",
        func: str = "",
        prod: str = "",
        seq: str = "",
        category_name: str = "",
        family_name: str = "",
        status: str | None = None,
        is_complete: bool = False,
        extra: dict | None = None,
    ) -> dict:
        """DEPRECATED — use :meth:`build_ifc_element` + :meth:`ingest_ifc_data`.

        Built a TagElementSync dict for the legacy /tagsync/sync path and
        fabricated a ``revitElementId`` from md5(guid), forcing ArchiCAD
        elements into pseudo-Revit rows. Kept only for backwards-compat.
        """
        segments = [disc, loc, zone, lvl, sys, func, prod, seq]
        tag1 = "-".join(s for s in segments if s)

        # RevitElementId is a non-nullable long on the server. For IFC elements
        # that have no integer element ID, derive a stable int64 from the GUID.
        import hashlib as _hl
        revit_id = int(_hl.md5(guid.encode()).hexdigest()[:15], 16) & 0x7FFF_FFFF_FFFF_FFFF

        d: dict[str, Any] = {
            "revitElementId": revit_id,
            "uniqueId": guid,
            "disc": disc,
            "loc": loc,
            "zone": zone,
            "lvl": lvl,
            "sys": sys,
            "func": func,
            "prod": prod,
            "seq": seq,
            "tag1": tag1,
            "categoryName": category_name,
            "familyName": family_name,
            "isComplete": is_complete,
            "lastModifiedUtc": datetime.now(timezone.utc).isoformat(),
        }
        if status is not None:
            d["status"] = status
        if extra:
            d.update(extra)
        return d
