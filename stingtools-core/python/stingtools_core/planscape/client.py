"""HTTP client for Planscape Server. Uses httpx when available;
falls back to urllib for the package-loads-without-deps story."""

from __future__ import annotations

import json
import logging
from typing import Any, Optional
from urllib import request as _urlreq, error as _urlerr

from ..exceptions import PlanscapeAuthError, PlanscapeNetworkError, PlanscapeError

logger = logging.getLogger("stingtools_core.planscape")

# PlanscapeAuthError is re-exported from .exceptions for backwards
# compatibility — existing callers importing it from this module keep
# working.


class PlanscapeClient:
    """Minimal Planscape REST client.

    Endpoints used by the MVP:
      POST  /api/auth/login                    → access + refresh tokens
      POST  /api/auth/refresh                  → new access token
      GET   /api/projects                      → user's projects
      POST  /api/projects/{id}/ifc/data        → IFC-data ingest (new MVP endpoint)
      POST  /api/tagsync/sync                  → bulk tag upsert (existing)
      POST  /api/projects/{id}/issues          → raise issue
      POST  /api/projects/{id}/compliance      → push compliance snapshot

    All requests carry X-Client-Type: blender so the server can
    attribute writes to the host.
    """

    DEFAULT_TIMEOUT_S = 15.0

    def __init__(
        self,
        base_url: str,
        access_token: Optional[str] = None,
        refresh_token: Optional[str] = None,
        client_type: str = "blender",
        timeout_s: float = DEFAULT_TIMEOUT_S,
    ):
        self.base_url = base_url.rstrip("/")
        self._access = access_token
        self._refresh = refresh_token
        self._client_type = client_type
        self._timeout = timeout_s

    # ------------------------------------------------------------------
    # auth
    # ------------------------------------------------------------------

    def login(self, email: str, password: str) -> tuple[str, str]:
        body = {"email": email, "password": password}
        data = self._request("POST", "/api/auth/login", body=body, with_auth=False)
        self._access = data.get("accessToken") or data.get("token")
        self._refresh = data.get("refreshToken")
        if not self._access:
            raise PlanscapeAuthError("Server returned no access token")
        return self._access, self._refresh or ""

    def refresh(self) -> str:
        if not self._refresh:
            raise PlanscapeAuthError("No refresh token available")
        data = self._request("POST", "/api/auth/refresh", body={"refreshToken": self._refresh}, with_auth=False)
        self._access = data.get("accessToken") or data.get("token")
        if not self._access:
            raise PlanscapeAuthError("Refresh did not return a new access token")
        return self._access

    # ------------------------------------------------------------------
    # projects
    # ------------------------------------------------------------------

    def list_projects(self) -> list[dict]:
        data = self._request("GET", "/api/projects")
        return data if isinstance(data, list) else (data.get("items") or [])

    # ------------------------------------------------------------------
    # IFC ingest (new MVP endpoint)
    # ------------------------------------------------------------------

    def ingest_ifc_data(
        self,
        project_id: str,
        elements: list[dict],
        host_document_guid: Optional[str] = None,
    ) -> dict:
        """Send per-element STING data + host element ids to Planscape.

        elements is a list of dicts shaped like:
            {
                "ifc_guid": "1Abc...",
                "host_element_id": "blender:Wall.042",
                "tag": "M-BLD1-Z01-L02-HVAC-SUP-AHU-0042",
                "discipline": "M", "location": "BLD1", ...,
                "compliance_pct": 0.92,
            }
        """
        body = {
            "host": self._client_type,
            "host_document_guid": host_document_guid,
            "elements": elements,
        }
        return self._request("POST", f"/api/projects/{project_id}/ifc/data", body=body)

    # ------------------------------------------------------------------
    # tag sync (existing endpoint)
    # ------------------------------------------------------------------

    def sync_tags(self, project_id: str, elements: list[dict]) -> dict:
        body = {"projectId": project_id, "elements": elements}
        return self._request("POST", "/api/tagsync/sync", body=body)

    # ------------------------------------------------------------------
    # issues
    # ------------------------------------------------------------------

    def raise_issue(self, project_id: str, issue: dict) -> dict:
        return self._request("POST", f"/api/projects/{project_id}/issues", body=issue)

    # ------------------------------------------------------------------
    # compliance
    # ------------------------------------------------------------------

    def push_compliance(self, project_id: str, snapshot: dict) -> dict:
        return self._request("POST", f"/api/projects/{project_id}/compliance", body=snapshot)

    # ------------------------------------------------------------------
    # transport
    # ------------------------------------------------------------------

    def _request(self, method: str, path: str, body: Optional[dict] = None, with_auth: bool = True) -> Any:
        url = self.base_url + path
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json",
            "X-Client-Type": self._client_type,
        }
        if with_auth and self._access:
            headers["Authorization"] = f"Bearer {self._access}"

        payload = json.dumps(body or {}).encode("utf-8") if body is not None else None

        # prefer httpx when available
        try:
            import httpx  # type: ignore
            with httpx.Client(timeout=self._timeout) as client:
                resp = client.request(method, url, headers=headers, content=payload)
                if resp.status_code in (401, 403):
                    raise PlanscapeAuthError(f"{resp.status_code} from {path}")
                resp.raise_for_status()
                return resp.json() if resp.content else {}
        except ImportError:
            # stdlib fallback
            req = _urlreq.Request(url, data=payload, method=method, headers=headers)
            try:
                with _urlreq.urlopen(req, timeout=self._timeout) as resp:
                    raw = resp.read()
                    return json.loads(raw.decode("utf-8")) if raw else {}
            except _urlerr.HTTPError as e:
                if e.code in (401, 403):
                    raise PlanscapeAuthError(f"{e.code} from {path}") from e
                raise PlanscapeNetworkError(f"HTTP {e.code} from {path}: {e.reason}") from e
            except _urlerr.URLError as e:
                raise PlanscapeNetworkError(f"network error contacting {path}: {e.reason}") from e
