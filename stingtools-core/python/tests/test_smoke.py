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


def test_revit_params_guids_distinct_across_psets():
    """sting_to_revit_params.py uses (PsetName, PropName) → UUID v5. Verify
    deterministic + collision-free even when prop names overlap across psets."""
    import sys, os
    sys.path.insert(
        0,
        os.path.join(os.path.dirname(__file__), '..', '..', '..', 'tools', 'converters'),
    )
    from sting_to_revit_params import deterministic_guid

    g1a = deterministic_guid("Pset_StingTags", "Discipline")
    g1b = deterministic_guid("Pset_StingTags", "Discipline")
    g2  = deterministic_guid("Pset_StingHealthcareClinical", "Discipline")
    g3  = deterministic_guid("Pset_StingTags", "Location")

    # Deterministic — same inputs same outputs
    assert g1a == g1b, f"non-deterministic: {g1a} vs {g1b}"
    # Cross-pset same prop name → distinct GUIDs
    assert g1a != g2, f"collision across psets sharing prop name: {g1a} == {g2}"
    # Cross-prop within same pset → distinct GUIDs
    assert g1a != g3, f"collision across props in same pset: {g1a} == {g3}"
    # All are valid UUIDs
    import uuid
    for g in (g1a, g2, g3):
        uuid.UUID(g)  # raises ValueError if malformed


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


# ----------------------------------------------------------------------
# Path-2 closeout — 6 newly-enforced static rules
# ----------------------------------------------------------------------

def test_disc_not_empty_at_stage_3():
    """DISC_NOT_EMPTY fires when Discipline is missing or 'XX' at Stage_3."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    # Element with Discipline = XX
    w = run("root.create_entity", model, ifc_class="IfcWall", name="w-xx")
    p = run("pset.add_pset", model, product=w, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=p, properties={
        "Discipline": "XX", "Location": "BLD1", "Zone": "Z01", "Level": "L01",
        "System": "HVAC", "Function": "SUP", "Product": "AHU", "Sequence": "0001",
    })

    mismatches = SpatialChecker(model, stage="Stage_3").check_all_elements()
    disc_mm = [m for m in mismatches if m.rule_id == "DISC_NOT_EMPTY"]
    assert len(disc_mm) >= 1, f"expected DISC_NOT_EMPTY, got {[m.rule_id for m in mismatches]}"


def test_disc_not_empty_invalid_value_skipped_at_stage_1():
    """Bug regression: DISC_NOT_EMPTY enum-membership branch must respect ActiveFrom.

    Before the Phase 186b stage-gating fix, an invalid Discipline like 'XYZ'
    fired DISC_NOT_EMPTY at Stage_1 even though the rule's ActiveFrom is
    Stage_2. Verify the whole rule (including enum-membership) honours the gate.
    """
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    w = run("root.create_entity", model, ifc_class="IfcWall", name="w-bad")
    p = run("pset.add_pset", model, product=w, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=p, properties={
        "Discipline": "XYZ",  # not in StingDisciplineCodes
        "Location": "BLD1", "Zone": "Z01", "Level": "L01",
        "System": "HVAC", "Function": "SUP", "Product": "AHU", "Sequence": "0001",
    })

    enums = EnumRegistry().load()
    mismatches = SpatialChecker(model, stage="Stage_1", enum_registry=enums).check_all_elements()
    disc_mm = [m for m in mismatches if m.rule_id == "DISC_NOT_EMPTY"]
    assert disc_mm == [], (
        f"DISC_NOT_EMPTY enum-membership must respect ActiveFrom=Stage_2 — "
        f"got {len(disc_mm)} mismatch(es) at Stage_1"
    )

    # ... and now verify it DOES fire at Stage_2
    mismatches2 = SpatialChecker(model, stage="Stage_2", enum_registry=enums).check_all_elements()
    disc_mm2 = [m for m in mismatches2 if m.rule_id == "DISC_NOT_EMPTY"]
    assert len(disc_mm2) == 1, f"expected 1 DISC_NOT_EMPTY at Stage_2, got {len(disc_mm2)}"


def test_drawing_type_registry_from_json():
    """DrawingTypeRegistry.from_json loads the corporate STING_DRAWING_TYPES.json."""
    from stingtools_core.spatial import DrawingTypeRegistry
    from pathlib import Path

    repo_root = Path(__file__).resolve().parents[3]
    corp_json = repo_root / "StingTools" / "Data" / "STING_DRAWING_TYPES.json"
    if not corp_json.exists():
        return  # repo layout changed; skip rather than fail

    reg = DrawingTypeRegistry.from_json(corp_json)
    assert len(reg) > 0, "expected DrawingType ids in corporate JSON"
    # known ids from the corporate catalogue
    for known in ("arch-plan-A1-1to100", "pipe-spool-A1-1to50"):
        assert known in reg, f"expected {known!r} in registry"


def test_drawing_type_registry_handles_malformed_json(tmp_path):
    """from_json raises ValueError on bad input."""
    from stingtools_core.spatial import DrawingTypeRegistry

    bad = tmp_path / "bad.json"
    bad.write_text("{not valid json", encoding="utf-8")
    try:
        DrawingTypeRegistry.from_json(bad)
        assert False, "should have raised"
    except ValueError:
        pass

    missing = tmp_path / "missing.json"
    missing.write_text('{"version": 1}', encoding="utf-8")  # no drawingTypes key
    try:
        DrawingTypeRegistry.from_json(missing)
        assert False, "should have raised"
    except ValueError:
        pass


def test_drawing_type_registry_from_jsons_layered(tmp_path):
    """from_jsons merges corporate baseline + project override."""
    from stingtools_core.spatial import DrawingTypeRegistry
    import json

    corp = tmp_path / "corp.json"
    proj = tmp_path / "proj.json"
    corp.write_text(json.dumps({
        "drawingTypes": [{"id": "corp-a"}, {"id": "corp-b"}],
    }), encoding="utf-8")
    proj.write_text(json.dumps({
        "drawingTypes": [{"id": "proj-x"}, {"id": "proj-y"}],
    }), encoding="utf-8")
    reg = DrawingTypeRegistry.from_jsons(corp, proj)
    assert len(reg) == 4
    for k in ("corp-a", "corp-b", "proj-x", "proj-y"):
        assert k in reg


def test_drawing_check_fires_on_document_information():
    """DRAWING_TYPE_RESOLVABLE fires on IfcDocumentInformation (G4)."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    # IfcDocumentInformation isn't a rooted entity in IFC4 — create it directly
    doc = model.create_entity(
        "IfcDocumentInformation",
        Identification="DOC-001",
        Name="Drawing Set",
    )
    # Pset attachment via IfcRelAssociatesDocument isn't required here —
    # we attach the Pset directly via ifcopenshell.api.pset.
    try:
        p = run("pset.add_pset", model, product=doc, name="Pset_StingDrawing")
        run("pset.edit_pset", model, pset=p, properties={
            "DrawingTypeId": "bad type with spaces",
        })
    except (TypeError, AttributeError):
        # ifcopenshell.api.pset may not support non-rooted entities;
        # in that case the walk-extension is still verified via the
        # IfcAnnotation path. Skip gracefully.
        return

    mismatches = SpatialChecker(model).check_all_elements()
    dt_mm = [m for m in mismatches if m.rule_id == "DRAWING_TYPE_RESOLVABLE"]
    assert len(dt_mm) == 1, f"expected 1 DRAWING_TYPE_RESOLVABLE on IfcDocumentInformation, got {len(dt_mm)}"


def test_stage_3_rules_skip_at_stage_2():
    """Bug regression: LOC_MATCHES_BUILDING / SEQ_UNIQUE_WITHIN_GROUP /
    FULLTAG_CONSISTENT all have ActiveFrom=Stage_3 and must not fire at Stage_2.
    """
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    # Wall with deliberately-wrong tag data (would trip 3 Stage_3 rules)
    w = run("root.create_entity", model, ifc_class="IfcWall", name="w")
    p = run("pset.add_pset", model, product=w, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=p, properties={
        "Discipline": "M", "Location": "BLD1-NONE-MATCH",
        "Zone": "Z01", "Level": "L01",
        "System": "HVAC", "Function": "SUP", "Product": "AHU", "Sequence": "0001",
        "FullTag": "WRONG-VALUE",
    })

    mismatches = SpatialChecker(model, stage="Stage_2").check_all_elements()
    triggered = {m.rule_id for m in mismatches}
    forbidden = {"LOC_MATCHES_BUILDING", "FULLTAG_CONSISTENT", "SEQ_UNIQUE_WITHIN_GROUP",
                 "LVL_MATCHES_STOREY", "ZONE_MATCHES_ASSIGNEDZONE", "SYS_MATCHES_IFCSYSTEM",
                 "DRAWING_TYPE_RESOLVABLE"}
    found = triggered & forbidden
    assert not found, (
        f"Stage_3 rules fired at Stage_2: {sorted(found)} — they should be gated"
    )


def test_disc_not_empty_passes_at_stage_1():
    """DISC_NOT_EMPTY does NOT fire on 'XX' at Stage_1 (rule starts at Stage_3)."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    w = run("root.create_entity", model, ifc_class="IfcWall", name="w-early")
    p = run("pset.add_pset", model, product=w, name="Pset_StingTags")
    run("pset.edit_pset", model, pset=p, properties={
        "Discipline": "XX", "Location": "BLD1", "Zone": "XX", "Level": "L01",
        "System": "XX", "Function": "XX", "Product": "XX", "Sequence": "0000",
    })

    mismatches = SpatialChecker(model, stage="Stage_1").check_all_elements()
    disc_mm = [m for m in mismatches if m.rule_id == "DISC_NOT_EMPTY"]
    assert disc_mm == [], f"DISC_NOT_EMPTY must not fire at Stage_1, got {disc_mm}"


def test_drawing_type_resolvable_format_fails():
    """DRAWING_TYPE_RESOLVABLE fires when DrawingTypeId has invalid format."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    ann = run("root.create_entity", model, ifc_class="IfcAnnotation", name="a-bad")
    p = run("pset.add_pset", model, product=ann, name="Pset_StingDrawing")
    run("pset.edit_pset", model, pset=p, properties={
        "DrawingTypeId": "bad id with spaces!",
    })

    mismatches = SpatialChecker(model).check_all_elements()
    dt_mm = [m for m in mismatches if m.rule_id == "DRAWING_TYPE_RESOLVABLE"]
    assert len(dt_mm) == 1, f"expected 1 DRAWING_TYPE_RESOLVABLE, got {[m.rule_id for m in mismatches]}"
    assert "format" in dt_mm[0].message.lower()


def test_drawing_type_resolvable_registry_lookup():
    """DRAWING_TYPE_RESOLVABLE fires when DrawingTypeId is well-formed but not in registry."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker, DrawingTypeRegistry

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    ann = run("root.create_entity", model, ifc_class="IfcAnnotation", name="a-unknown")
    p = run("pset.add_pset", model, product=ann, name="Pset_StingDrawing")
    run("pset.edit_pset", model, pset=p, properties={
        "DrawingTypeId": "nonexistent-profile-id",
    })

    registry = DrawingTypeRegistry({"arch-plan-A1-1to100", "pipe-spool-A1-1to50"})
    mismatches = SpatialChecker(model, drawing_type_registry=registry).check_all_elements()
    dt_mm = [m for m in mismatches if m.rule_id == "DRAWING_TYPE_RESOLVABLE"]
    assert len(dt_mm) == 1
    assert "does not resolve" in dt_mm[0].message

    # Now with a known id — must pass
    ann2 = run("root.create_entity", model, ifc_class="IfcAnnotation", name="a-known")
    p2 = run("pset.add_pset", model, product=ann2, name="Pset_StingDrawing")
    run("pset.edit_pset", model, pset=p2, properties={
        "DrawingTypeId": "arch-plan-A1-1to100",
    })
    mismatches2 = SpatialChecker(model, drawing_type_registry=registry).check_all_elements()
    dt_mm2 = [m for m in mismatches2 if m.rule_id == "DRAWING_TYPE_RESOLVABLE"
              and m.ifc_global_id == ann2.GlobalId]
    assert dt_mm2 == [], f"known id must pass, got {dt_mm2}"


def test_projectorg_project_code_required():
    """PROJECTORG_PROJECT_CODE_REQUIRED fires on missing or malformed ProjectCode."""
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

    # Malformed: lowercase
    p = run("pset.add_pset", model, product=project, name="Pset_StingProjectOrg")
    run("pset.edit_pset", model, pset=p, properties={
        "ProjectCode": "abc123",
    })

    mismatches = SpatialChecker(model).check_all_elements()
    pc_mm = [m for m in mismatches if m.rule_id == "PROJECTORG_PROJECT_CODE_REQUIRED"]
    assert len(pc_mm) == 1, f"expected 1 PROJECTORG_PROJECT_CODE_REQUIRED, got {[m.rule_id for m in mismatches]}"
    assert "doesn't match" in pc_mm[0].message


def test_projectorg_phase_valid():
    """PROJECTORG_PHASE_VALID fires on unknown phase when enum_registry supplied."""
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

    p = run("pset.add_pset", model, product=project, name="Pset_StingProjectOrg")
    run("pset.edit_pset", model, pset=p, properties={
        "ProjectCode": "AHS01",
        "Phase": "NotARealStage",
    })

    enums = EnumRegistry().load()
    mismatches = SpatialChecker(model, enum_registry=enums).check_all_elements()
    phase_mm = [m for m in mismatches if m.rule_id == "PROJECTORG_PHASE_VALID"]
    assert len(phase_mm) == 1, f"expected 1 PROJECTORG_PHASE_VALID, got {[m.rule_id for m in mismatches]}"


def test_building_loc_unique():
    """BUILDING_LOC_UNIQUE fires when two IfcBuildings share LocationCode."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    for name in ("A", "B"):
        b = run("root.create_entity", model, ifc_class="IfcBuilding", name=name)
        p = run("pset.add_pset", model, product=b, name="Pset_StingSpatialCodes")
        run("pset.edit_pset", model, pset=p, properties={"LocationCode": "BLD1"})

    mismatches = SpatialChecker(model).check_all_elements()
    loc_mm = [m for m in mismatches if m.rule_id == "BUILDING_LOC_UNIQUE"]
    assert len(loc_mm) == 2, f"expected 2 BUILDING_LOC_UNIQUE (one per dupe), got {len(loc_mm)}"


def test_storey_lvl_unique_within_building():
    """STOREY_LVL_UNIQUE_WITHIN_BUILDING fires when two storeys in one building share LevelCode."""
    try:
        import ifcopenshell
    except ImportError:
        return
    from ifcopenshell.api import run
    from stingtools_core.spatial import SpatialChecker

    model = run("project.create_file", version="IFC4")
    run("root.create_entity", model, ifc_class="IfcProject", name="t")
    run("unit.assign_unit", model)
    run("context.add_context", model, context_type="Model")

    bldg = run("root.create_entity", model, ifc_class="IfcBuilding", name="B1")
    bp = run("pset.add_pset", model, product=bldg, name="Pset_StingSpatialCodes")
    run("pset.edit_pset", model, pset=bp, properties={"LocationCode": "BLD1"})

    storeys = []
    for name in ("L01a", "L01b"):
        s = run("root.create_entity", model, ifc_class="IfcBuildingStorey", name=name)
        sp = run("pset.add_pset", model, product=s, name="Pset_StingSpatialCodes")
        run("pset.edit_pset", model, pset=sp, properties={"LevelCode": "L01"})
        storeys.append(s)

    run("aggregate.assign_object", model, relating_object=bldg, products=storeys)

    mismatches = SpatialChecker(model).check_all_elements()
    lvl_mm = [m for m in mismatches if m.rule_id == "STOREY_LVL_UNIQUE_WITHIN_BUILDING"]
    assert len(lvl_mm) == 2, f"expected 2 STOREY_LVL_UNIQUE_WITHIN_BUILDING (one per dupe), got {len(lvl_mm)}"


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
        # Converters
        test_revit_params_guids_distinct_across_psets,
        # SpatialChecker (needs ifcopenshell; skip-if-missing internally)
        test_spatial_checker_handles_empty_model,
        test_fulltag_consistency_check,
        test_seq_uniqueness_check,
        # Path-2 closeout — 6 newly-enforced rules
        test_disc_not_empty_at_stage_3,
        test_disc_not_empty_passes_at_stage_1,
        test_disc_not_empty_invalid_value_skipped_at_stage_1,
        test_drawing_type_registry_from_json,
        # test_drawing_type_registry_from_jsons_layered (tmp_path — pytest only)
        # test_drawing_type_registry_handles_malformed_json (tmp_path — pytest only)
        test_drawing_check_fires_on_document_information,
        test_stage_3_rules_skip_at_stage_2,
        test_drawing_type_resolvable_format_fails,
        test_drawing_type_resolvable_registry_lookup,
        test_projectorg_project_code_required,
        test_projectorg_phase_valid,
        test_building_loc_unique,
        test_storey_lvl_unique_within_building,
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
          f"(tmp_path tests skipped — run with pytest for full coverage)")
    sys.exit(1 if failures else 0)
