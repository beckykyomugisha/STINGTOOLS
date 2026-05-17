"""
Writes STING tokens back to ArchiCAD as User-Defined properties.

Ensures the STING property group and individual token properties exist
before writing, using create_property_group / create_property_definition
as needed.
"""
from __future__ import annotations

import logging
from typing import Any

from ..archicad.client import ArchiCadClient

log = logging.getLogger(__name__)

_STING_GROUP = "STING"

# Token name → AC property name
_TOKEN_PROPS: dict[str, str] = {
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


class PropertyWriter:
    """Ensures STING properties exist then writes token values to elements."""

    def __init__(self, client: ArchiCadClient):
        self._client = client
        self._group_id: dict | None = None
        self._prop_ids: dict[str, dict] = {}  # prop_name → propertyId dict

    # ── setup ─────────────────────────────────────────────────────────────────

    def ensure_properties(self) -> None:
        """Create the STING property group and all token properties if absent."""
        all_defs = self._client.get_all_property_definitions()

        # Find or create STING group
        group_id: dict | None = None
        for defn in all_defs:
            grp = defn.get("propertyDefinition", {}).get("group", {})
            if grp.get("name", "").upper() == _STING_GROUP:
                group_id = {"type": "UserDefined", "localizedName": [_STING_GROUP]}
                break

        if group_id is None:
            log.info("Creating STING property group in ArchiCAD")
            result = self._client.create_property_group(_STING_GROUP)
            group_id = result.get("propertyGroupId", {
                "type": "UserDefined",
                "localizedName": [_STING_GROUP],
            })

        self._group_id = group_id

        # Index existing props by name
        existing: dict[str, dict] = {}
        for defn in all_defs:
            pd = defn.get("propertyDefinition", {})
            grp = pd.get("group", {})
            if grp.get("name", "").upper() != _STING_GROUP:
                continue
            name = pd.get("name", "")
            pid = defn.get("propertyId", {})
            if name and pid:
                existing[name] = pid

        # Create any missing token properties
        for _token_key, prop_name in _TOKEN_PROPS.items():
            if prop_name in existing:
                self._prop_ids[prop_name] = existing[prop_name]
            else:
                log.info("Creating property %s/%s", _STING_GROUP, prop_name)
                result = self._client.create_property_definition(
                    group_id=self._group_id,
                    name=prop_name,
                    description=f"STING ISO 19650 token: {prop_name}",
                    default_value="",
                )
                pid = result.get("propertyId")
                if pid:
                    self._prop_ids[prop_name] = pid

    # ── write ─────────────────────────────────────────────────────────────────

    def write_tokens(
        self,
        guid_token_pairs: list[tuple[str, dict[str, str]]],
    ) -> dict[str, int]:
        """
        Write token dicts back to ArchiCAD elements.

        guid_token_pairs: [(guid, {disc, loc, zone, ...}), ...]
        Returns {"written": N, "skipped": N}
        """
        if not self._prop_ids:
            self.ensure_properties()

        payloads: list[dict] = []
        for guid, tokens in guid_token_pairs:
            # Build tag1 if not already present
            tag_tokens = tokens.copy()
            if "tag1" not in tag_tokens or not tag_tokens["tag1"]:
                parts = [
                    tag_tokens.get("disc", ""),
                    tag_tokens.get("loc",  ""),
                    tag_tokens.get("zone", ""),
                    tag_tokens.get("lvl",  ""),
                    tag_tokens.get("sys",  ""),
                    tag_tokens.get("func", ""),
                    tag_tokens.get("prod", ""),
                    tag_tokens.get("seq",  ""),
                ]
                tag_tokens["tag1"] = "-".join(p for p in parts if p)

            for token_key, prop_name in _TOKEN_PROPS.items():
                value = tag_tokens.get(token_key, "")
                if not value:
                    continue
                prop_id = self._prop_ids.get(prop_name)
                if not prop_id:
                    continue
                payloads.append({
                    "elementId": {"guid": guid},
                    "propertyId": prop_id,
                    "propertyValue": {"type": "normal", "value": value},
                })

        if not payloads:
            return {"written": 0, "skipped": len(guid_token_pairs)}

        self._client.set_property_values(payloads)
        written = len({p["elementId"]["guid"] for p in payloads})
        skipped = len(guid_token_pairs) - written
        return {"written": written, "skipped": skipped}
