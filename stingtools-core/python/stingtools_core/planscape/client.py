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
        plugin_version: str = "",
        user_name: str = "",
    ) -> dict:
        """Send per-element STING data + host element ids to Planscape.

        Callers pass Pythonic snake_case element dicts; this method maps
        them onto the server's ``IfcElementDto`` field names (camelCase)
        before serialising. The server
        (``Planscape.Core.DTOs.IfcIngestDtos``) deserialises with the
        ASP.NET Core "Web" defaults — case-INsensitive but NOT
        snake_case-aware — so the wire payload must already use the DTO's
        member names. The fix here is that mapping; see ``_element_to_wire``.

        Each element dict may carry any of (snake_case keys; aliases in
        parens are also accepted):

            ifc_global_id   (ifc_guid, global_id)  -> ifcGlobalId   [required]
            host_element_id                        -> hostElementId
            host_display_label (display_label)     -> hostDisplayLabel
            discipline / location / zone / level / system / function /
                product / sequence                 -> same (camelCase identical)
            full_tag        (tag)                  -> fullTag
            ifc_class                              -> ifcClass
            category_name   (category)             -> categoryName
            family_name     (family)               -> familyName
            type_name       (type)                 -> typeName
            status / rev                           -> status / rev
            room_name                              -> roomName
            level_name                             -> levelName
            is_complete / is_fully_resolved /
                is_stale                           -> isComplete / ...
            validation_errors                      -> validationErrors (JSON str)
            last_modified_utc                      -> lastModifiedUtc (ISO-8601)
        """
        body = {
            "host": self._client_type,
            "hostDocumentGuid": host_document_guid,
            "pluginVersion": plugin_version,
            "userName": user_name,
            "elements": [self._element_to_wire(e) for e in elements],
        }
        return self._request("POST", f"/api/projects/{project_id}/ifc/data", body=body)

    # Maps Pythonic snake_case element keys to the server IfcElementDto
    # member names. Each entry is (wireName, (acceptedSourceKeys...)).
    # First source key present in the input dict wins. Booleans are
    # emitted even when False; other keys are emitted only when present.
    _ELEMENT_FIELD_MAP: tuple[tuple[str, tuple[str, ...]], ...] = (
        ("ifcGlobalId",      ("ifc_global_id", "ifc_guid", "global_id")),
        ("hostElementId",    ("host_element_id",)),
        ("hostDisplayLabel", ("host_display_label", "display_label")),
        ("discipline",       ("discipline",)),
        ("location",         ("location",)),
        ("zone",             ("zone",)),
        ("level",            ("level",)),
        ("system",           ("system",)),
        ("function",         ("function",)),
        ("product",          ("product",)),
        ("sequence",         ("sequence",)),
        ("fullTag",          ("full_tag", "tag")),
        ("ifcClass",         ("ifc_class",)),
        ("categoryName",     ("category_name", "category")),
        ("familyName",       ("family_name", "family")),
        ("typeName",         ("type_name", "type")),
        ("status",           ("status",)),
        ("rev",              ("rev",)),
        ("roomName",         ("room_name",)),
        ("levelName",        ("level_name",)),
        ("isComplete",       ("is_complete",)),
        ("isFullyResolved",  ("is_fully_resolved",)),
        ("isStale",          ("is_stale",)),
        ("validationErrors", ("validation_errors",)),
        ("lastModifiedUtc",  ("last_modified_utc",)),
    )

    @classmethod
    def _element_to_wire(cls, el: dict) -> dict:
        """Translate one snake_case element dict to the server DTO shape.

        Pass-through for keys already in camelCase (so a caller that
        hand-builds the wire shape isn't double-mangled): if a wire key
        is already present verbatim in the input it is preserved.
        """
        _MISSING = object()
        out: dict[str, Any] = {}
        for wire_key, source_keys in cls._ELEMENT_FIELD_MAP:
            value = _MISSING
            if wire_key in el:                      # already-camelCase input
                value = el[wire_key]
            else:
                for sk in source_keys:
                    if sk in el:
                        value = el[sk]
                        break
            if value is not _MISSING:
                out[wire_key] = value
        return out

    # ------------------------------------------------------------------
    # substrate drift-check (Phase A4)
    # ------------------------------------------------------------------

    def get_substrate_manifest(self) -> dict:
        """GET /api/substrate/manifest — the server's substrate hash.

        Returns the server's ``{ "sha256", "schemaVersion", "totalEnums" }``.
        The substrate is global (not project-scoped), so no project id.
        """
        return self._request("GET", "/api/substrate/manifest")

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


# ----------------------------------------------------------------------
# substrate drift-check helper (Phase A4)
# ----------------------------------------------------------------------

def check_substrate_drift(client: Any, shared_ifc: Optional[Any] = None) -> tuple[bool, str]:
    """Compare this host's substrate hash against the server's.

    Returns ``(ok, message)``:
      - ``(True, …)``  — in sync, or the server hasn't pinned a hash, or the
        check could not be performed (network/transport error) — drift-check
        never blocks login.
      - ``(False, …)`` — the host reads a different (stale/forked) substrate
        than the federation; ``message`` is suitable for a host warning.

    ``client`` is duck-typed: any object exposing ``get_substrate_manifest()``
    returning ``{"sha256": …}`` works (the core :class:`PlanscapeClient`, the
    Bonsai add-on client, or the StingBridge client).
    """
    from .. import substrate

    try:
        local = substrate.substrate_manifest_sha256(shared_ifc)
    except FileNotFoundError as e:
        return True, f"substrate check skipped (no local manifest: {e})"

    try:
        remote = client.get_substrate_manifest()
    except Exception as e:  # noqa: BLE001 — never block login on a transport hiccup
        return True, f"substrate check skipped (server unreachable: {e})"

    remote_hash = remote.get("sha256") if isinstance(remote, dict) else None
    if substrate.compare(local, remote_hash):
        return True, "substrate in sync with server"
    return (
        False,
        "SUBSTRATE DRIFT — this host reads a different shared/ifc vocabulary "
        f"than the server (local {local[:12]}… vs server {(remote_hash or '')[:12]}…). "
        "Re-sync shared/ifc before coordinating.",
    )
