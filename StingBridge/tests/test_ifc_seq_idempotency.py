"""Adversarial idempotency test for the IFC drop path's SEQ minting (R3).

WHY THIS EXISTS AS A SEPARATE FILE
----------------------------------
`test_seq_minting.py` already asserts that `assign_sequences()` skips elements
that carry a `seq`. That test passes a token dict it built itself, so it only
ever proves the *minter* is idempotent — it cannot catch the bug that shipped in
beta.2, where the minter was fine but the IFC path never told it about the SEQ
already written into the file. Re-feeding your own output proves nothing about
the pipeline that produces it.

So this test re-derives state through the FULL pipeline instead:

    build IFC → process() → write-back to <name>_sting.ifc
              → process() the WRITTEN-BACK FILE
              → re-extract, re-map, re-mint

Round 2 gets no help from round 1's in-memory state: it reads the tokens back
off disk exactly as a user re-dropping the file would. The assertions are the
two things a user actually feels — the counter is not burned, and elements are
not renumbered.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

ifcopenshell = pytest.importorskip(
    "ifcopenshell", reason="IFC round-trip needs a real parser; nothing to fake here"
)

from StingBridge.watch.ifc_watcher import IFCDropHandler  # noqa: E402


# Minimal IFC2x3: 2 walls + 1 door under a storey. Same shape as make_test_ifc.py.
_MINIMAL_IFC = """ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
FILE_NAME('seq_idem.ifc','2026-01-01T00:00:00',(''),(''),'IfcOpenShell','IfcOpenShell','');
FILE_SCHEMA(('IFC2X3'));
ENDSEC;
DATA;
#1=IFCPROJECT('0YvCtVUKr4jAilsiy$6drx',$,'Test Project',$,$,$,$,(#2),#3);
#2=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-5,#4,$);
#3=IFCUNITASSIGNMENT((#5,#6,#7));
#4=IFCAXIS2PLACEMENT3D(#8,$,$);
#5=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#6=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);
#7=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
#8=IFCCARTESIANPOINT((0.,0.,0.));
#9=IFCSITE('1lBi$tbhf3DBjGJyiGfPLq',$,'Site',$,$,#10,$,$,.ELEMENT.,$,$,$,$,$);
#10=IFCLOCALPLACEMENT($,#11);
#11=IFCAXIS2PLACEMENT3D(#12,$,$);
#12=IFCCARTESIANPOINT((0.,0.,0.));
#13=IFCBUILDING('2lBi$tbhf3DBjGJyiGfPLq',$,'Building',$,$,#14,$,$,.ELEMENT.,$,$,$);
#14=IFCLOCALPLACEMENT(#10,#15);
#15=IFCAXIS2PLACEMENT3D(#16,$,$);
#16=IFCCARTESIANPOINT((0.,0.,0.));
#17=IFCBUILDINGSTOREY('3lBi$tbhf3DBjGJyiGfPLq',$,'Level 1',$,$,#18,$,$,.ELEMENT.,0.);
#18=IFCLOCALPLACEMENT(#14,#19);
#19=IFCAXIS2PLACEMENT3D(#20,$,$);
#20=IFCCARTESIANPOINT((0.,0.,0.));
#21=IFCWALL('4lBi$tbhf3DBjGJyiGfPLq',$,'Wall 001',$,'Basic Wall',$,$,$);
#22=IFCWALL('5lBi$tbhf3DBjGJyiGfPLq',$,'Wall 002',$,'Basic Wall',$,$,$);
#23=IFCDOOR('6lBi$tbhf3DBjGJyiGfPLq',$,'Door 001',$,'Door',$,$,$,$,$);
#24=IFCRELAGGREGATES('7lBi$tbhf3DBjGJyiGfPLq',$,$,$,#1,(#9));
#25=IFCRELAGGREGATES('8lBi$tbhf3DBjGJyiGfPLq',$,$,$,#9,(#13));
#26=IFCRELAGGREGATES('9lBi$tbhf3DBjGJyiGfPLq',$,$,$,#13,(#17));
#27=IFCRELCONTAINEDINSPATIALSTRUCTURE('AlBi$tbhf3DBjGJyiGfPLq',$,$,$,(#21,#22,#23),#17);
ENDSEC;
END-ISO-10303-21;
"""


class _CountingCounterServer:
    """Fake /seq/reserve that COUNTS reservations.

    The count is the whole point: an idempotent second run must not ask for a
    single number. Handing out disjoint blocks (never reusing a value) mirrors
    the real endpoint's INSERT … ON CONFLICT … RETURNING, so if the second run
    *does* reserve, the seqs visibly change and both assertions fail together.
    """

    def __init__(self):
        self.counters: dict[str, int] = {}
        self.calls: list[dict] = []
        self.project_id = "proj-idem"

    # -- the SEQ surface --------------------------------------------------
    def reserve_seq(self, reservations):
        self.calls.append(dict(reservations))
        blocks = {}
        for key, count in reservations.items():
            start = self.counters.get(key, 0) + 1
            self.counters[key] = start + count - 1
            blocks[key] = {"start": start, "count": count}
        return blocks

    @property
    def total_reserved(self) -> int:
        return sum(sum(c.values()) for c in self.calls)

    # -- everything else the handler touches, stubbed ---------------------
    def sync_elements(self, *a, **kw):
        return {"newElements": 0, "updatedElements": 0, "skipped": 0}

    def sync_ifc_elements(self, *a, **kw):
        return {"newElements": 0, "updatedElements": 0, "skipped": 0}

    def __getattr__(self, name):
        # Any other client call the pipeline makes is irrelevant here; return a
        # no-op rather than letting an AttributeError masquerade as a SEQ bug.
        def _noop(*a, **kw):
            return {}
        return _noop


class _Cfg:
    """Config stub — write-back ON, GLB conversion OFF (not under test)."""
    write_back = True
    ifc_convert_path = None
    convert_to_glb = False
    project_id = "proj-idem"

    def __getattr__(self, name):
        return None


def _seqs_by_guid(ifc_path: Path) -> dict[str, str]:
    """Read ASS_SEQ_NUM_TXT straight off disk, independent of the pipeline.

    Deliberately does NOT reuse the watcher's extractor: if that had a bug the
    test would inherit it and agree with itself.
    """
    import ifcopenshell.util.element as ifc_util

    model = ifcopenshell.open(str(ifc_path))
    out: dict[str, str] = {}
    for el in model.by_type("IfcProduct"):
        psets = ifc_util.get_psets(el)
        seq = (psets.get("STING_TOKENS") or {}).get("ASS_SEQ_NUM_TXT")
        if seq:
            out[el.GlobalId] = str(seq)
    return out


def test_redropping_written_back_ifc_mints_nothing_new(tmp_path):
    """Re-processing the written-back IFC must reserve ZERO new numbers."""
    src = tmp_path / "seq_idem.ifc"
    src.write_text(_MINIMAL_IFC, encoding="utf-8")

    server = _CountingCounterServer()
    handler = IFCDropHandler(server, _Cfg())

    # ── Round 1: virgin file ────────────────────────────────────────────
    r1 = handler.process(str(src))
    assert not r1["errors"], f"round 1 errored: {r1['errors']}"
    assert r1["elements"] > 0, "fixture produced no elements — test would be vacuous"

    written = src.with_name(src.stem + "_sting.ifc")
    assert written.exists(), "round 1 did not write tokens back; round 2 would be meaningless"

    seqs_after_r1 = _seqs_by_guid(written)
    assert seqs_after_r1, "round 1 minted no SEQ at all — nothing to be idempotent about"

    reserved_r1 = server.total_reserved
    assert reserved_r1 > 0

    # ── Round 2: re-derive from the written-back file, full pipeline ────
    # This is the adversarial bit. Nothing from round 1 is handed forward —
    # process() re-opens the file, re-extracts, re-maps and re-mints.
    r2 = handler.process(str(written))
    assert not r2["errors"], f"round 2 errored: {r2['errors']}"

    assert server.total_reserved == reserved_r1, (
        "round 2 reserved new SEQ numbers "
        f"({server.total_reserved - reserved_r1} extra) — the written-back "
        "ASS_SEQ_NUM_TXT was not adopted, so every re-drop burns the counter "
        "and renumbers stable elements"
    )

    # ── And the numbers themselves are untouched ────────────────────────
    written2 = written.with_name(written.stem + "_sting.ifc")
    seqs_after_r2 = _seqs_by_guid(written2) if written2.exists() else _seqs_by_guid(written)
    assert seqs_after_r2 == seqs_after_r1, (
        f"SEQ values changed across re-drop:\n  r1={seqs_after_r1}\n  r2={seqs_after_r2}"
    )


def test_first_pass_actually_mints(tmp_path):
    """Guard against the idempotency test passing because nothing ever mints."""
    src = tmp_path / "seq_first.ifc"
    src.write_text(_MINIMAL_IFC, encoding="utf-8")

    server = _CountingCounterServer()
    IFCDropHandler(server, _Cfg()).process(str(src))

    assert server.total_reserved > 0, (
        "no SEQ reserved on a virgin file — if this fails, the idempotency "
        "test above is vacuously green"
    )
