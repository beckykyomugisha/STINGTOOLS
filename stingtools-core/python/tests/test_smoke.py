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


# ----------------------------------------------------------------------
# Negative tests — verify the substrate FAILS on bad input
# ----------------------------------------------------------------------

def test_tag_grammar_rejects_bad_discipline():
    """Discipline outside StingDisciplineCodes must fail validation."""
    reg = EnumRegistry().load()
    bad = Tag("XYZ", "BLD1", "Z01", "L01", "HVAC", "SUP", "AHU", "0042")
    res = TagGrammar(reg).validate(bad, stage="Stage_3")
    assert not res.is_valid
    assert any("Discipline" in e and "XYZ" in e for e in res.errors)


def test_tag_grammar_rejects_unpadded_sequence():
    """Sequence not matching ^\\d{4}$ must fail."""
    reg = EnumRegistry().load()
    bad = Tag("M", "BLD1", "Z01", "L01", "HVAC", "SUP", "AHU", "42")  # not zero-padded
    res = TagGrammar(reg).validate(bad, stage="Stage_3")
    assert not res.is_valid
    assert any("Sequence" in e for e in res.errors)


def test_tag_grammar_stage2_skips_seq_requirement():
    """Sequence is not required at Stage_2 — Tag with sentinel SEQ should pass."""
    reg = EnumRegistry().load()
    early = Tag("M", "BLD1", "XX", "L01", "XX", "XX", "XX", "0000")
    res = TagGrammar(reg).validate(early, stage="Stage_2")
    # At Stage_2, only DISC/LOC/LVL are required (per Pset_StingTags rule set)
    # Sentinels in SYS/FUNC/PROD/SEQ are acceptable
    assert res.is_valid, f"unexpected errors at Stage_2: {res.errors}"


def test_audit_log_detects_tampering(tmp_path):
    """If anyone edits a logged entry, verify_chain() must fail."""
    log = AuditLog(tmp_path / "audit.jsonl")
    log.append("first",  "alice", {"k": "v1"})
    log.append("second", "bob",   {"k": "v2"})
    # tamper: rewrite the second entry's payload
    path = tmp_path / "audit.jsonl"
    lines = path.read_text().splitlines()
    import json
    e = json.loads(lines[1])
    e["payload"]["k"] = "TAMPERED"
    lines[1] = json.dumps(e, sort_keys=True, separators=(",", ":"))
    path.write_text("\n".join(lines) + "\n")
    valid, errors = log.verify_chain()
    assert not valid
    assert errors, "expected at least one error message"


def test_audit_log_schema_version_present(tmp_path):
    """Every entry should carry an explicit schema_version field for forward-compat."""
    log = AuditLog(tmp_path / "audit.jsonl")
    e = log.append("test.event", "alice", {})
    assert "schema_version" in e
    assert isinstance(e["schema_version"], int)


def test_enum_overlay_reserved_codes_preserved(tmp_path):
    """A project overlay can NOT redefine reserved sentinel codes (XX, *, SITE, EXT, …)."""
    from stingtools_core.enums.loader import RESERVED_CODES, EnumRegistry as ER
    # Build a fake overlay dir with a StingLocationCodes overlay that omits XX
    overlay_dir = tmp_path / "overlay"
    overlay_dir.mkdir()
    overlay = overlay_dir / "StingLocationCodes.xml"
    overlay.write_text("""<?xml version="1.0" encoding="UTF-8"?>
<StingPropertyEnumeration xmlns="https://stingtools.io/schema/ifc/enums/v1" version="1.0.0" schema_version="1">
  <Identity>
    <Name>StingLocationCodes</Name>
    <Definition>test overlay</Definition>
    <IfdGuid>uuid:a1d15c11-9999-4001-9000-000000000001</IfdGuid>
  </Identity>
  <Governance>
    <Scope>project_template</Scope>
    <Origin>project</Origin>
    <SinceVersion>5.0.0</SinceVersion>
    <Maintainer>test</Maintainer>
    <StandardsBasis>test</StandardsBasis>
  </Governance>
  <IfcMapping>
    <PrimaryType>IfcLabel</PrimaryType>
    <UseAsEnumeratedValue>true</UseAsEnumeratedValue>
    <ApplicableIfcVersions>IFC4</ApplicableIfcVersions>
  </IfcMapping>
  <Values>
    <Value code="MYBLDG" sentinel="false">
      <Definition>project-specific</Definition>
      <SinceVersion>5.0.0</SinceVersion>
    </Value>
  </Values>
</StingPropertyEnumeration>""", encoding="utf-8")
    reg = ER(project_overlay_dir=overlay_dir).load()
    enum = reg.get("StingLocationCodes")
    assert enum is not None
    codes = enum.codes()
    # Project value present
    assert "MYBLDG" in codes
    # Reserved sentinels preserved from baseline
    for reserved in ("XX", "*"):
        assert reserved in codes, f"reserved {reserved!r} should be preserved from baseline"


def test_pset_referenced_enums_all_resolve():
    """Every <Enumeration> reference in every Pset must resolve to a loaded enum."""
    enums = EnumRegistry().load()
    psets = PsetRegistry().load()
    missing = [r for r in psets.referenced_enums() if r not in enums]
    assert not missing, f"Pset references unloaded enum(s): {missing}"


def test_spatial_checker_handles_empty_model():
    """SpatialChecker on a model with no IfcElements returns 0 mismatches, no crash."""
    try:
        import ifcopenshell
    except ImportError:
        return  # skip if not installed
    from ifcopenshell.api import run
    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="empty")
    from stingtools_core.spatial import SpatialChecker
    mismatches = SpatialChecker(model).check_all_elements()
    assert mismatches == []


def test_fulltag_consistency_check():
    """SpatialChecker fires FULLTAG_CONSISTENT when stored FullTag doesn't match segments."""
    try:
        import ifcopenshell
    except ImportError:
        return  # skip if not installed
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    project = run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")
    wall = run("root.create_entity", model, ifc_class="IfcWall", name="w")
    # Deliberately set FullTag to NOT match the segments
    pset = run("pset.add_pset", model, product=wall, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=pset, properties={
        "Discipline": "M", "Location": "BLD1", "Zone": "Z01", "Level": "L01",
        "System": "HVAC", "Function": "SUP", "Product": "AHU", "Sequence": "0042",
        "FullTag": "WRONG-VALUE",
    })
    mismatches = SpatialChecker(model).check_all_elements()
    rule_ids = [m.rule_id for m in mismatches]
    assert "FULLTAG_CONSISTENT" in rule_ids, f"expected FULLTAG_CONSISTENT, got {rule_ids}"


def test_seq_uniqueness_check():
    """SpatialChecker fires SEQ_UNIQUE_WITHIN_GROUP when two elements in same (Disc,Sys,Lvl) share SEQ."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    project = run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    common_tag = {
        "Discipline": "M", "Location": "BLD1", "Zone": "Z01", "Level": "L01",
        "System": "HVAC", "Function": "SUP", "Product": "AHU", "Sequence": "0001",
    }
    for name in ("w1", "w2"):
        w = run("root.create_entity", model, ifc_class="IfcWall", name=name)
        p = run("pset.add_pset", model, product=w, name="Pset_StingTags")
        run("pset.edit_pset", model, pset=p, properties=common_tag)

    mismatches = SpatialChecker(model).check_all_elements()
    seq_dupes = [m for m in mismatches if m.rule_id == "SEQ_UNIQUE_WITHIN_GROUP"]
    assert len(seq_dupes) == 2, f"expected 2 SEQ_UNIQUE_WITHIN_GROUP mismatches (one per dup), got {len(seq_dupes)}"


if __name__ == "__main__":
    # Allow `python tests/test_smoke.py` for quick local runs
    import traceback
    tests = [
        # happy-path
        test_enum_registry_loads_all_52,
        test_corporate_locks_verified,
        test_known_enum_codes,
        test_pset_registry_loads,
        test_pset_enum_references_resolve,
        test_tag_render_and_parse,
        test_tag_validation_stage_3,
        # negative + integrity
        test_tag_grammar_rejects_bad_discipline,
        test_tag_grammar_rejects_unpadded_sequence,
        test_tag_grammar_stage2_skips_seq_requirement,
        test_pset_referenced_enums_all_resolve,
        # SpatialChecker (needs ifcopenshell; skip-if-missing internally)
        test_spatial_checker_handles_empty_model,
        test_fulltag_consistency_check,
        test_seq_uniqueness_check,
    ]
    # tmp_path-needing tests skipped in __main__; use pytest for those:
    #   test_audit_log_chain, test_audit_log_detects_tampering,
    #   test_audit_log_schema_version_present, test_enum_overlay_reserved_codes_preserved
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
    print(f"\n{len(tests)} tests run, {failures} failures "
          f"(4 tmp_path tests skipped — run with pytest for full coverage)")
    sys.exit(1 if failures else 0)
