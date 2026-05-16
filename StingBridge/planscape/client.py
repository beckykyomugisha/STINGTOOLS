"""
Planscape REST API client.

Posts STING token data to /api/tagsync/sync using JWT authentication.
Matches the TagElementSync DTO in Planscape.Shared/Models/SyncModels.cs.
"""
from __future__ import annotations

import logging
import time
from datetime import datetime, timezone
from typing import Any

import requests

log = logging.getLogger(__name__)

_DEFAULT_BASE = "http://localhost:5000"
_TIMEOUT = 30


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

    # ── sync ─────────────────────────────────────────────────────────────────

    def sync_elements(
        self,
        elements: list[dict],
        source: str = "archicad",
    ) -> dict:
        """
        POST /api/tagsync/sync

        elements: list of TagElementSync-shaped dicts.
        Returns the server response dict.
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
        """Build a TagElementSync dict matching Planscape.Shared TagElementSync."""
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
