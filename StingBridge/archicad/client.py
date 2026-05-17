"""
ArchiCAD JSON HTTP API client.

Connects to the local ArchiCAD process on port 19723 (default).
All requests are POST to http://localhost:{port} with a JSON body:
  {"command": "API.*", "parameters": {...}}

AC28 / AC29 compatible.  No dependencies beyond `requests`.
"""
from __future__ import annotations

import time
import socket
import logging
from typing import Any

import requests

log = logging.getLogger(__name__)

_DEFAULT_PORT = 19723
_DISCOVER_PORTS = [19723, 19724, 19725, 19726]   # ArchiCAD tries next port if 19723 is taken
_TIMEOUT = 30   # seconds for individual requests


class ArchiCadError(Exception):
    pass


class ArchiCadClient:
    """Thin wrapper around the ArchiCAD JSON API."""

    def __init__(self, port: int = _DEFAULT_PORT):
        self.port = port
        self._base = f"http://localhost:{port}"
        self._session = requests.Session()
        self._session.headers.update({"Content-Type": "application/json"})

    # ── discovery ────────────────────────────────────────────────────────────

    @classmethod
    def discover(cls, timeout: float = 3.0) -> "ArchiCadClient":
        """
        Try each port in _DISCOVER_PORTS and return a client connected to the
        first live ArchiCAD instance.  Raises ArchiCadError if none found.
        """
        for port in _DISCOVER_PORTS:
            if _port_open("localhost", port, timeout):
                client = cls(port)
                try:
                    client.get_product_info()
                    log.info("ArchiCAD found on port %d", port)
                    return client
                except Exception:
                    continue
        raise ArchiCadError(
            f"ArchiCAD not found on ports {_DISCOVER_PORTS}. "
            "Make sure ArchiCAD is running and a project is open."
        )

    # ── raw command dispatch ─────────────────────────────────────────────────

    def call(self, command: str, parameters: dict | None = None) -> dict:
        body = {"command": command, "parameters": parameters or {}}
        try:
            resp = self._session.post(self._base, json=body, timeout=_TIMEOUT)
            resp.raise_for_status()
        except requests.ConnectionError as e:
            raise ArchiCadError(f"Cannot reach ArchiCAD on port {self.port}: {e}") from e
        except requests.HTTPError as e:
            raise ArchiCadError(f"ArchiCAD HTTP error: {e}") from e

        data = resp.json()
        if not data.get("succeeded", False):
            error = data.get("error", {})
            raise ArchiCadError(
                f"ArchiCAD command '{command}' failed: "
                f"[{error.get('code', '?')}] {error.get('message', 'unknown error')}"
            )
        return data.get("result", {})

    # ── product / version ────────────────────────────────────────────────────

    def get_product_info(self) -> dict:
        return self.call("API.GetProductInfo")

    # ── elements ─────────────────────────────────────────────────────────────

    def get_all_elements(self) -> list[str]:
        """Return list of element GUIDs for the entire project."""
        result = self.call("API.GetAllElements")
        return [e["elementId"]["guid"] for e in result.get("elements", [])]

    def get_elements_by_type(self, element_type: str) -> list[str]:
        """
        element_type: ArchiCAD type string, e.g. 'Wall', 'Slab', 'Roof',
        'Column', 'Beam', 'Door', 'Window', 'Object', 'Zone', 'Stair', etc.
        """
        result = self.call("API.GetElementsByType", {"elementType": element_type})
        return [e["elementId"]["guid"] for e in result.get("elements", [])]

    def get_details_of_elements(self, guids: list[str]) -> list[dict]:
        """Return element details (type, layer, story, boundingBox) for guids."""
        elements = [{"elementId": {"guid": g}} for g in guids]
        result = self.call("API.GetDetailsOfElements", {"elements": elements})
        return result.get("detailsOfElements", [])

    # ── properties ───────────────────────────────────────────────────────────

    def get_all_property_definitions(self) -> list[dict]:
        """Return every property definition visible in this project."""
        result = self.call("API.GetAllPropertyDefinitions")
        return result.get("propertyDefinitions", [])

    def get_property_values(
        self, guids: list[str], property_ids: list[dict]
    ) -> list[dict]:
        """
        property_ids: list of propertyId dicts, e.g.:
          {"type": "BuiltIn", "nonLocalizedName": "General_ElementID"}
          {"type": "UserDefined", "localizedName": ["STING", "ASS_DISCIPLINE_COD_TXT"]}
        Returns elementPropertyValues list.
        """
        elements = [{"elementId": {"guid": g}} for g in guids]
        result = self.call(
            "API.GetPropertyValuesOfElements",
            {"elements": elements, "properties": [{"propertyId": p} for p in property_ids]},
        )
        return result.get("elementPropertyValues", [])

    def set_property_values(self, element_property_values: list[dict]) -> dict:
        """
        element_property_values: list of
          {
            "elementId": {"guid": "..."},
            "propertyId": {"type": "UserDefined", "localizedName": ["STING", "ASS_DISC_TXT"]},
            "propertyValue": {"type": "normal", "value": "A"}
          }
        """
        return self.call(
            "API.SetPropertyValuesOfElements",
            {"elementPropertyValues": element_property_values},
        )

    # ── classifications ──────────────────────────────────────────────────────

    def get_classification_systems(self) -> list[dict]:
        result = self.call("API.GetAllClassificationSystems")
        return result.get("classificationSystems", [])

    def get_classifications_of_elements(
        self, guids: list[str], system_ids: list[dict]
    ) -> list[dict]:
        elements = [{"elementId": {"guid": g}} for g in guids]
        result = self.call(
            "API.GetClassificationsOfElements",
            {"elements": elements, "classificationSystemIds": system_ids},
        )
        return result.get("elementClassifications", [])

    # ── zones / stories ───────────────────────────────────────────────────────

    def get_elements_related_to_zones(self) -> list[dict]:
        result = self.call("API.GetElementsRelatedToZones")
        return result.get("zoneElementRelations", [])

    def get_story_info(self) -> list[dict]:
        result = self.call("API.GetStoryInfo")
        return result.get("stories", [])

    # ── property group / definition creation ──────────────────────────────────

    def create_property_group(self, name: str) -> dict:
        result = self.call(
            "API.CreatePropertyGroups",
            {"propertyGroups": [{"propertyGroup": {"name": name}}]},
        )
        groups = result.get("propertyGroups", [])
        return groups[0] if groups else {}

    def create_property_definition(
        self,
        group_id: dict,
        name: str,
        description: str = "",
        default_value: str = "",
    ) -> dict:
        """Create a single-value string property in the given group."""
        defn = {
            "propertyDefinition": {
                "name": name,
                "description": description,
                "group": group_id,
                "canValueBeEmpty": True,
                "defaultValue": {
                    "type": "normal",
                    "value": default_value,
                },
                "type": "string",
                "availability": {"type": "allElements"},
            }
        }
        result = self.call(
            "API.CreatePropertyDefinitions",
            {"propertyDefinitions": [defn]},
        )
        defs = result.get("propertyDefinitions", [])
        return defs[0] if defs else {}

    # ── tapir extended commands (optional) ────────────────────────────────────

    def tapir_available(self) -> bool:
        """Return True if the Tapir add-on is installed and responds."""
        try:
            self.call("TapirCommand.GetAddOnVersion")
            return True
        except ArchiCadError:
            return False

    def tapir_get_element_details(self, guids: list[str]) -> list[dict]:
        elements = [{"elementId": {"guid": g}} for g in guids]
        result = self.call(
            "TapirCommand.GetElementsDetails",
            {"elements": elements},
        )
        return result.get("detailsOfElements", [])

    # ── batch helper ─────────────────────────────────────────────────────────

    def batch_guids(self, guids: list[str], size: int = 100):
        """Yield successive chunks of `size` from guids."""
        for i in range(0, len(guids), size):
            yield guids[i : i + size]


def _port_open(host: str, port: int, timeout: float) -> bool:
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False
