"""Planscape Server HTTP client — Python standard library only.

No `requests`, no `_vendor`, no `stingtools_core`. Uses `urllib.request`
so the extension runs on a stock Blender 4.2+ install with nothing to
pip-install. Imports no `bpy`, so it is importable and testable headless.

Endpoints used:
  - POST /api/auth/login                         → { accessToken, ... }
  - POST /api/projects/{id}/ifc/data             → IfcIngestResponse
  - GET  /api/projects/{id}/ifc/mappings?ifcGuid=… → MappingsPage

The server (ASP.NET Core, System.Text.Json) emits camelCase JSON and
binds request bodies case-insensitively, so camelCase keys
(`ifcGlobalId`, `hostElementId`, …) match the IfcIngestRequest /
IfcElementDto contract.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Optional


class PlanscapeError(Exception):
    """Raised for any HTTP / transport / decode failure. Carries a readable message."""

    def __init__(self, message: str, status: Optional[int] = None, body: str = ""):
        super().__init__(message)
        self.status = status
        self.body = body


class PlanscapeClient:
    """Thin REST client for the Planscape Server.

    Use http://… for localhost (no TLS). ``token`` is the JWT access token
    returned by :meth:`login`; pass it in to reuse a stored token without
    logging in again.
    """

    def __init__(self, base_url: str, token: Optional[str] = None, timeout: int = 30):
        self.base_url = (base_url or "").rstrip("/")
        self.token = token or None
        self.timeout = timeout

    # ------------------------------------------------------------------
    # core request
    # ------------------------------------------------------------------
    def _request(self, method: str, path: str, body: Optional[dict] = None,
                 token: Optional[str] = None) -> Any:
        if not self.base_url:
            raise PlanscapeError("server URL is empty — set it in STING preferences")
        url = self.base_url + path
        data = json.dumps(body).encode("utf-8") if body is not None else None

        req = urllib.request.Request(url, data=data, method=method.upper())
        req.add_header("Accept", "application/json")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        tok = token if token is not None else self.token
        if tok:
            req.add_header("Authorization", "Bearer " + tok)

        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode("utf-8", "replace")
                return json.loads(raw) if raw.strip() else {}
        except urllib.error.HTTPError as e:
            detail = ""
            try:
                detail = e.read().decode("utf-8", "replace")
            except Exception:  # noqa: BLE001
                pass
            raise PlanscapeError(
                f"HTTP {e.code} on {method} {path}: {detail[:500] or e.reason}",
                status=e.code, body=detail,
            ) from None
        except urllib.error.URLError as e:
            raise PlanscapeError(
                f"connection failed on {method} {url}: {getattr(e, 'reason', e)} "
                f"(is the Planscape server running and the URL correct?)"
            ) from None
        except (TimeoutError, OSError) as e:  # socket timeout, etc.
            raise PlanscapeError(f"network error on {method} {url}: {e}") from None
        except json.JSONDecodeError as e:
            raise PlanscapeError(f"bad JSON from {method} {path}: {e}") from None

    # ------------------------------------------------------------------
    # auth
    # ------------------------------------------------------------------
    def login(self, email: str, password: str) -> tuple[str, dict]:
        """POST /api/auth/login. Returns (access_token, full_response).

        Stores the token on the client for subsequent calls.
        """
        resp = self._request("POST", "/api/auth/login",
                             {"email": email, "password": password})
        token = (resp.get("accessToken") or resp.get("access_token") or "")
        if not token:
            raise PlanscapeError("login succeeded but no accessToken in response")
        self.token = token
        return token, resp

    # ------------------------------------------------------------------
    # IFC ingest
    # ------------------------------------------------------------------
    def ingest_ifc(self, project_id: str, host: str, elements: list[dict],
                   host_document_guid: Optional[str] = None,
                   plugin_version: str = "", user_name: str = "") -> dict:
        """POST /api/projects/{id}/ifc/data — the cross-host IFC ingest path.

        ``elements`` items are IfcElementDto-shaped dicts (camelCase keys:
        ``ifcGlobalId``, ``hostElementId``, ``ifcClass``, tag segments…).
        """
        body = {
            "host": host,
            "hostDocumentGuid": host_document_guid,
            "pluginVersion": plugin_version,
            "userName": user_name,
            "elements": elements,
        }
        return self._request("POST", f"/api/projects/{project_id}/ifc/data", body)

    # ------------------------------------------------------------------
    # cross-host lookup
    # ------------------------------------------------------------------
    def get_mappings(self, project_id: str, ifc_guid: Optional[str] = None,
                     host: Optional[str] = None) -> dict:
        """GET /api/projects/{id}/ifc/mappings?ifcGuid=…&host=… — cross-host resolve."""
        params = {}
        if ifc_guid:
            params["ifcGuid"] = ifc_guid
        if host:
            params["host"] = host
        qs = ("?" + urllib.parse.urlencode(params)) if params else ""
        return self._request("GET", f"/api/projects/{project_id}/ifc/mappings{qs}")

    def list_projects(self) -> Any:
        """GET /api/projects — used to help the user find their project id."""
        return self._request("GET", "/api/projects")

    # ------------------------------------------------------------------
    # substrate drift-check (Phase A4)
    # ------------------------------------------------------------------
    def get_substrate_manifest(self) -> dict:
        """GET /api/substrate/manifest — the server's substrate hash.

        Returns ``{ "sha256", "schemaVersion", "totalEnums" }``. The substrate
        is global (not project-scoped). Used by the post-login drift-check so a
        host reading a stale/forked shared/ifc vocabulary is warned, not left to
        silently coordinate on divergent enums.
        """
        return self._request("GET", "/api/substrate/manifest")
