"""Regression guard — GlobalId derivation must be stable across all hosts.

Loads fixtures/globalid_corpus.json (generated once by generate_globalid_fixture.py
and committed to the repo) and asserts that the Python derivation function
produces exactly the same 22-char IFC GlobalId for every (host, host_element_id)
pair.

A test failure here means the UUID5 namespace or encoding changed — which would
break cross-host element identity for every existing project in production. Do NOT
update the fixture file without a migration plan.
"""
from __future__ import annotations

import json
import os
import uuid

import pytest

STING_GLOBALID_NAMESPACE = uuid.UUID("a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a")

_FIXTURE_PATH = os.path.join(
    os.path.dirname(__file__), "fixtures", "globalid_corpus.json"
)


def _uuid5_to_ifc_globalid(u: uuid.UUID) -> str:
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


def _load_corpus() -> list[dict]:
    with open(_FIXTURE_PATH, encoding="utf-8") as f:
        return json.load(f)


@pytest.mark.parametrize(
    "host,host_element_id,expected",
    [
        (row["host"], row["host_element_id"], row["expected_global_id"])
        for row in _load_corpus()
    ],
)
def test_globalid_is_stable(host: str, host_element_id: str, expected: str) -> None:
    """Derived GlobalId must match the committed fixture exactly."""
    actual = derive_globalid(host, host_element_id)
    assert actual == expected, (
        f"GlobalId mismatch for {host}:{host_element_id!r} — "
        f"got {actual!r}, expected {expected!r}. "
        "The STING_GLOBALID_NAMESPACE or encoding has changed: this BREAKS "
        "cross-host element identity for all existing projects."
    )


def test_globalid_length() -> None:
    """IFC GlobalIds are always exactly 22 characters."""
    for row in _load_corpus():
        gid = derive_globalid(row["host"], row["host_element_id"])
        assert len(gid) == 22


def test_globalid_charset() -> None:
    """All characters must be in the IFC base64 alphabet."""
    valid = set("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$")
    for row in _load_corpus():
        gid = derive_globalid(row["host"], row["host_element_id"])
        invalid = set(gid) - valid
        assert not invalid, f"Invalid chars {invalid!r} in GlobalId {gid!r}"


def test_different_hosts_produce_different_ids() -> None:
    """Same host_element_id on different hosts must NOT collide."""
    gid_revit   = derive_globalid("revit",   "1234567")
    gid_bonsai  = derive_globalid("bonsai",  "1234567")
    gid_archicad = derive_globalid("archicad", "1234567")
    assert len({gid_revit, gid_bonsai, gid_archicad}) == 3
