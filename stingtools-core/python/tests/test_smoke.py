"""Smoke tests — verify the package loads the real shared/ifc/ contents."""

from __future__ import annotations

import sys
from pathlib import Path

# Allow running from repo root without install:
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from stingtools_core import (
    EnumRegistry, PsetRegistry, TagGrammar, Tag,
)
from stingtools_core.enums import EnumScope
from stingtools_core.planscape import AuditLog


def test_enum_registry_loads_all_52():
    reg = EnumRegistry().load()
    assert len(reg) == 52, f"expected 52 enums, got {len(reg)}"
    assert reg.drift == [], f"unexpected drift: {reg.drift}"


def test_corporate_locks_verified():
    reg = EnumRegistry(verify_locks=True).load()
    assert reg.drift == [], (
        "Corporate-lock SHA-256 drift detected — run "
        "tools/enums/compute_checksums.py to regenerate"
    )


def test_known_enum_codes():
    reg = EnumRegistry().load()
    disc = reg.require("StingDisciplineCodes")
    assert "M" in disc and "E" in disc and "XX" in disc
    assert "*" in disc.sentinels()
    assert disc.scope is EnumScope.CORPORATE


def test_pset_registry_loads():
    reg = PsetRegistry().load()
    assert "Pset_StingTags" in reg
    assert "Pset_StingSpatialCodes" in reg
    pst = reg.require("Pset_StingTags")
    assert len(pst.properties) == 12
    assert len(pst.rules) == 9


def test_pset_enum_references_resolve():
    enums = EnumRegistry().load()
    psets = PsetRegistry().load()
    for ref in psets.referenced_enums():
        assert ref in enums, f"Pset references unknown enum {ref!r}"


def test_tag_render_and_parse():
    t = Tag(
        discipline="M", location="BLD1", zone="Z01", level="L02",
        system="HVAC", function="SUP", product="AHU", sequence="0042",
    )
    rendered = t.to_full_tag()
    assert rendered == "M-BLD1-Z01-L02-HVAC-SUP-AHU-0042"
    assert Tag.from_full_tag(rendered) == t


def test_tag_validation_stage_3():
    reg = EnumRegistry().load()
    grammar = TagGrammar(reg)
    good = Tag("M", "BLD1", "Z01", "L02", "HVAC", "SUP", "AHU", "0042")
    res = grammar.validate(good, stage="Stage_3")
    # function/product not required at Stage_3 so this passes
    assert res.is_valid, f"unexpected errors: {res.errors}"

    bad = Tag("XYZ", "BLD1", "Z01", "L02", "HVAC", "SUP", "AHU", "1")
    res = grammar.validate(bad, stage="Stage_3")
    assert not res.is_valid
    assert any("Discipline" in e for e in res.errors)
    assert any("Sequence" in e for e in res.errors)


def test_audit_log_chain(tmp_path):
    log = AuditLog(tmp_path / "audit.jsonl")
    e1 = log.append("test.event.a", "alice", {"k": "v"})
    e2 = log.append("test.event.b", "alice", {"k": "v2"}, ifc_global_id="1Abc")
    valid, errors = log.verify_chain()
    assert valid, errors
    assert e2["prev_sha"] == e1["entry_sha"]


if __name__ == "__main__":
    # Allow `python tests/test_smoke.py` for quick local runs
    import traceback
    tests = [
        test_enum_registry_loads_all_52,
        test_corporate_locks_verified,
        test_known_enum_codes,
        test_pset_registry_loads,
        test_pset_enum_references_resolve,
        test_tag_render_and_parse,
        test_tag_validation_stage_3,
    ]
    failures = 0
    for t in tests:
        try:
            t()
            print(f"  OK   {t.__name__}")
        except AssertionError as e:
            failures += 1
            print(f"  FAIL {t.__name__}: {e}")
        except Exception:
            failures += 1
            print(f"  ERR  {t.__name__}:")
            traceback.print_exc()
    # audit log test needs tmp_path -> skipped in __main__
    print(f"\n{len(tests)} tests run, {failures} failures (audit-log test skipped, needs pytest)")
    sys.exit(1 if failures else 0)
