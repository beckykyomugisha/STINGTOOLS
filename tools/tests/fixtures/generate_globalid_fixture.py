"""Generate a stable GlobalId fixture file used by test_globalid_stability.py.

Run once to seed fixtures/globalid_corpus.json; commit the output.
Re-run only when the UUID5 seed namespace changes (which must never happen
in production — changing it invalidates every cross-host element identity).

Usage:
    python generate_globalid_fixture.py
"""
from __future__ import annotations

import hashlib
import json
import os
import uuid

# Stable namespace UUID for STING GlobalId derivation.
# MUST match the constant used by every host adapter.
STING_GLOBALID_NAMESPACE = uuid.UUID("a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a")

# Mapping format mirrors ExternalElementMapping on the server:
# keyed on (host, host_element_id) → expected 22-char IFC GlobalId.
_SAMPLE_INPUTS: list[dict] = [
    {"host": "revit",     "host_element_id": "1234567"},
    {"host": "revit",     "host_element_id": "9999999"},
    {"host": "bonsai",    "host_element_id": "#42"},
    {"host": "bonsai",    "host_element_id": "BlenderObj.001"},
    {"host": "archicad",  "host_element_id": "AC-wall-0001"},
    {"host": "archicad",  "host_element_id": "AC-slab-0002"},
]


def _uuid5_to_ifc_globalid(u: uuid.UUID) -> str:
    """Encode a UUID as a 22-character IFC GlobalId string (base64-variant).

    The IFC spec uses a modified base64 alphabet:
    0-9 A-Z a-z _ $  (64 chars, big-endian groups of 6 bits).
    """
    CHARS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$"
    int_val = u.int
    result = []
    for _ in range(22):
        result.append(CHARS[int_val & 0x3F])
        int_val >>= 6
    return "".join(reversed(result))


def derive_globalid(host: str, host_element_id: str) -> str:
    seed = f"{host}:{host_element_id}"
    u = uuid.uuid5(STING_GLOBALID_NAMESPACE, seed)
    return _uuid5_to_ifc_globalid(u)


def main() -> None:
    corpus = []
    for item in _SAMPLE_INPUTS:
        gid = derive_globalid(item["host"], item["host_element_id"])
        corpus.append({
            "host": item["host"],
            "host_element_id": item["host_element_id"],
            "expected_global_id": gid,
        })

    out_path = os.path.join(os.path.dirname(__file__), "globalid_corpus.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(corpus, f, indent=2)
    print(f"Wrote {len(corpus)} fixtures to {out_path}")


if __name__ == "__main__":
    main()
