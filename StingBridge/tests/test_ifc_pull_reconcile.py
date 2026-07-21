"""SB-5a — the IFC drop path wired to pull → reconcile → push.

The engine's decision table is already proven by `test_sync_reconcile.py` (fakes)
and `e2e_pull_reconcile.py` (real Postgres). What was NEVER covered is the thing
SB-5a actually builds: that the *watcher* pulls before it pushes, that a remote
win reaches both the written-back IFC and the outgoing payload, that the cursor
survives a restart, and that a loser is written down somewhere.

So these tests drive the real `IFCDropHandler` over a real IFC file with a fake
change feed, and assert on artefacts a user can see — the tokens on disk, the
bytes pushed, the sidecar — rather than on the engine's internal counters.
"""
from __future__ import annotations

import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _p in (str(_REPO_ROOT), str(_REPO_ROOT / "stingtools-core" / "python")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

ifcopenshell = pytest.importorskip(
    "ifcopenshell", reason="the watcher path needs a real IFC parser")

from StingBridge.sync.ifc_reconcile import (  # noqa: E402
    CURSOR_FILENAME, build_local_index, conflict_sidecar_path, cursor_host_key,
    file_modified_utc, pull_and_reconcile,
)
from StingBridge.watch.ifc_watcher import IFCDropHandler  # noqa: E402


_MINIMAL_IFC = """ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
FILE_NAME('sb5a.ifc','2026-01-01T00:00:00',(''),(''),'IfcOpenShell','IfcOpenShell','');
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

WALL_A = "4lBi$tbhf3DBjGJyiGfPLq"
WALL_B = "5lBi$tbhf3DBjGJyiGfPLq"


# ── fakes ─────────────────────────────────────────────────────────────────────

class _Resp:
    def __init__(self, status_code, body):
        self.status_code = status_code
        self._body = body

    def json(self):
        return self._body


class _FakeServer:
    """A Planscape stand-in with a real cursor-paged change feed.

    The cursor is an opaque offset string — enough to exercise the client's
    resume semantics without reimplementing Postgres keyset pagination (the E2E
    covers the real thing against real timestamp precision).
    """

    def __init__(self, items=None, project_id="proj-sb5a"):
        self.base_url = "http://fake.planscape.test"
        self.project_id = project_id
        self.items = list(items or [])
        self.feed_since: list = []       # the `since` sent on each feed call
        self.pushed: list[dict] = []     # each ingest_ifc_data call
        self.counters: dict[str, int] = {}

    # -- change feed ------------------------------------------------------
    def _send(self, verb, url, params=None, timeout=None, **kw):
        assert verb == "get" and url.endswith("/changes"), f"unexpected call: {verb} {url}"
        params = dict(params or {})
        self.feed_since.append(params.get("since"))
        start = int(params["since"]) if params.get("since") else 0
        limit = int(params.get("limit") or 200)
        page = self.items[start: start + limit]
        nxt = start + len(page)
        return _Resp(200, {"items": page, "nextCursor": str(nxt),
                           "hasMore": nxt < len(self.items)})

    # -- push -------------------------------------------------------------
    def ingest_ifc_data(self, elements, host=None, host_document_guid=None, **kw):
        self.pushed.append({"elements": list(elements), "host": host,
                            "doc_guid": host_document_guid})
        return {"newElements": len(elements)}

    # -- SEQ --------------------------------------------------------------
    def reserve_seq(self, reservations):
        blocks = {}
        for key, count in (reservations or {}).items():
            start = self.counters.get(key, 0) + 1
            self.counters[key] = start + count - 1
            blocks[key] = {"start": start, "end": start + count - 1, "count": count}
        return blocks

    # -- everything else the pipeline may touch ---------------------------
    def __getattr__(self, name):
        def _noop(*a, **kw):
            return {}
        return _noop

    @property
    def pushed_elements(self):
        return [e for call in self.pushed for e in call["elements"]]


class _Cfg:
    """Config stub — pull ON, GLB conversion off (not under test)."""
    pull_reconcile = True
    push_chunk_size = 100
    push_max_retries = 0          # no waiting in tests
    write_back = True
    convert_to_glb = False
    planscape_project_id = ""     # suppresses GLB upload

    def __getattr__(self, name):
        return None


# ── helpers ───────────────────────────────────────────────────────────────────

def _write_ifc(tmp_path, name="sb5a.ifc") -> Path:
    src = tmp_path / name
    src.write_text(_MINIMAL_IFC, encoding="utf-8")
    return src


def _delta(gid, payload, when: datetime) -> dict:
    return {"kind": "tag", "globalId": gid, "payload": payload,
            "lastModifiedUtc": when.isoformat().replace("+00:00", "Z")}


def _tokens_on_disk(ifc_path: Path) -> dict[str, dict]:
    """Read STING_TOKENS straight off disk, independent of the watcher's extractor."""
    import ifcopenshell.util.element as ifc_util

    model = ifcopenshell.open(str(ifc_path))
    out: dict[str, dict] = {}
    for el in model.by_type("IfcProduct"):
        pset = ifc_util.get_psets(el).get("STING_TOKENS")
        if pset:
            out[el.GlobalId] = {k: str(v) for k, v in pset.items() if k != "id"}
    return out


def _pushed(server, gid):
    for el in server.pushed_elements:
        if el.get("ifc_global_id") == gid:
            return el
    return None


def _file_times(src: Path):
    """(mtime, one hour later, one hour earlier) as aware UTC datetimes."""
    mtime = datetime.fromisoformat(file_modified_utc(src).replace("Z", "+00:00"))
    return mtime, mtime + timedelta(hours=1), mtime - timedelta(hours=1)


# ── the round trip ────────────────────────────────────────────────────────────

def test_remote_newer_reaches_both_the_written_back_ifc_and_the_push(tmp_path):
    """The whole point of SB-5a: an edit made elsewhere survives an IFC re-drop."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, later)])
    result = IFCDropHandler(server, _Cfg()).process(str(src))

    assert not result["errors"], result["errors"]
    assert result["reconcile"]["applied"] == 1, result["reconcile"]

    # 1. the token map the write-back reads
    written = src.with_name(src.stem + "_sting.ifc")
    assert written.exists()
    assert _tokens_on_disk(written)[WALL_A]["ASS_ZONE_TXT"] == "Z99", (
        "the remote win never reached the IFC — a re-export would revert it again")

    # 2. the payload the hub receives
    assert _pushed(server, WALL_A)["zone"] == "Z99", (
        "we pushed our stale value back over the remote edit we just accepted")


def test_a_sparse_delta_only_overwrites_the_keys_it_names(tmp_path):
    """A delta naming `zone` must not blank out the seven tokens it is silent about."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, later)])
    IFCDropHandler(server, _Cfg()).process(str(src))

    pushed = _pushed(server, WALL_A)
    assert pushed["zone"] == "Z99"
    assert pushed["discipline"], "discipline was wiped by a delta that never mentioned it"
    assert pushed["level"], "level was wiped by a delta that never mentioned it"


def test_local_newer_is_kept_and_the_loser_is_written_down(tmp_path):
    src = _write_ifc(tmp_path)
    _, _, earlier = _file_times(src)

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, earlier)])
    result = IFCDropHandler(server, _Cfg()).process(str(src))

    assert result["reconcile"]["kept_local"] == 1
    assert result["reconcile"]["applied"] == 0
    assert _pushed(server, WALL_A)["zone"] != "Z99", "a stale remote edit overwrote local work"

    sidecar = conflict_sidecar_path(src)
    assert sidecar.exists(), "the losing edit vanished without trace"


def test_conflict_sidecar_schema(tmp_path):
    """The sidecar is the audit trail — its shape is a contract, so pin it."""
    src = _write_ifc(tmp_path)
    _, _, earlier = _file_times(src)

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, earlier)])
    IFCDropHandler(server, _Cfg()).process(str(src))

    lines = conflict_sidecar_path(src).read_text(encoding="utf-8").strip().splitlines()
    assert lines, "conflict recorded but no rows written"

    rows = [json.loads(line) for line in lines]
    for row in rows:
        assert set(row) == {"ts", "source", "guid", "key", "local", "remote",
                            "winner", "applied", "reason"}, row
        assert row["guid"] == WALL_A
        assert row["winner"] in ("local", "remote")
        assert row["reason"], "a conflict with no stated reason is not auditable"
        assert row["source"] == src.name

    zone_rows = [r for r in rows if r["key"] == "zone"]
    assert len(zone_rows) == 1, "expected exactly one row for the token that differed"
    assert zone_rows[0]["remote"] == "Z99"
    assert zone_rows[0]["local"] != "Z99"
    assert zone_rows[0]["winner"] == "local" and zone_rows[0]["applied"] is False


def test_sidecar_records_the_local_value_that_was_overwritten(tmp_path):
    """A remote win must record what the local value WAS, not what it became.

    Regression guard. The adapter mutates the token dicts in place, and the
    engine applies before it reports, so a callback reading the live token map
    sees the post-apply value and writes `local == remote` on every remote win —
    silently losing the only fact the audit trail exists to preserve.
    """
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)

    # Learn the real local zone by doing one clean pass with an empty feed.
    probe = _FakeServer([])
    IFCDropHandler(probe, _Cfg(), cursor_dir=tmp_path / "probe").process(str(src))
    original_zone = _pushed(probe, WALL_A)["zone"]

    # A fresh filename so the probe's write-back and cursor cannot influence
    # the run under test.
    src = _write_ifc(tmp_path, "again.ifc")
    _, later, _ = _file_times(src)

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, later)])
    result = IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path).process(str(src))
    assert result["reconcile"]["applied"] == 1, "expected the remote edit to win"

    rows = [json.loads(line) for line
            in conflict_sidecar_path(src).read_text(encoding="utf-8").splitlines()]
    zone_rows = [r for r in rows if r["key"] == "zone"]

    assert zone_rows, (
        "no row recorded for the token that was actually overwritten — the "
        "sidecar compared the post-apply value against itself")
    assert zone_rows[0]["remote"] == "Z99"
    assert zone_rows[0]["local"] == original_zone
    assert zone_rows[0]["local"] != "Z99"
    assert zone_rows[0]["winner"] == "remote" and zone_rows[0]["applied"] is True


def test_no_conflict_means_no_sidecar(tmp_path):
    """An empty audit file on every clean drop would train operators to ignore it."""
    src = _write_ifc(tmp_path)
    IFCDropHandler(_FakeServer([]), _Cfg()).process(str(src))
    assert not conflict_sidecar_path(src).exists()


def test_changes_for_other_documents_are_counted_absent_not_failed(tmp_path):
    """The feed is project-wide; a sibling model's elements are not our failures."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)

    server = _FakeServer([
        _delta("NOTINTHISFILEXXXXXXXXX", {"zone": "Z99"}, later),
        _delta(WALL_A, {"zone": "Z77"}, later),
    ])
    result = IFCDropHandler(server, _Cfg()).process(str(src))

    rec = result["reconcile"]
    assert rec["absent"] == 1 and rec["applied"] == 1
    assert rec["failed"] == 0, (
        "an element belonging to another document was reported as a failure, "
        "which makes `failed` useless as a signal")


# ── cursor persistence ────────────────────────────────────────────────────────

def test_cursor_survives_a_watcher_restart(tmp_path):
    """A restart must resume, not replay the whole feed as if it were new."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)
    items = [_delta(WALL_A, {"zone": "Z99"}, later)]

    server = _FakeServer(items)
    IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path).process(str(src))
    assert server.feed_since[0] is None, "first run should start from the beginning"

    cursor_file = tmp_path / CURSOR_FILENAME
    assert cursor_file.exists(), "no cursor persisted — every restart is a full backfill"

    # A brand-new handler and a brand-new client: nothing carried in memory.
    server2 = _FakeServer(items)
    result2 = IFCDropHandler(server2, _Cfg(), cursor_dir=tmp_path).process(str(src))

    assert server2.feed_since[0] is not None, "restart replayed the feed from zero"
    assert result2["reconcile"]["pulled"] == 0, (
        "the resumed drain re-delivered a change already consumed before the restart")
    assert server2.pushed, "the push must still happen even when the pull is empty"


def test_cursor_is_per_document_not_per_project(tmp_path):
    """Two exports share a project. One draining the feed must not blind the other."""
    a = _write_ifc(tmp_path, "model_a.ifc")
    b = _write_ifc(tmp_path, "model_b.ifc")
    _, later, _ = _file_times(a)
    items = [_delta(WALL_A, {"zone": "Z99"}, later)]

    server = _FakeServer(items)
    cfg = _Cfg()
    IFCDropHandler(server, cfg, cursor_dir=tmp_path).process(str(a))
    IFCDropHandler(server, cfg, cursor_dir=tmp_path).process(str(b))

    stored = json.loads((tmp_path / CURSOR_FILENAME).read_text(encoding="utf-8"))
    assert len(stored) == 2, (
        f"expected one cursor per document, got {list(stored)} — with a shared "
        "cursor the first file to drain the feed consumes every other file's changes")

    # And model_b genuinely saw the change rather than inheriting a's position.
    assert _pushed(server, WALL_A)["zone"] == "Z99"


def test_cursor_defaults_to_the_drop_root_not_the_processing_folder(tmp_path):
    """A cursor written into processing/ is archived away with the file."""
    processing = tmp_path / "processing"
    processing.mkdir()
    src = _write_ifc(processing, "claimed.ifc")

    IFCDropHandler(_FakeServer([]), _Cfg()).process(str(src))

    assert (tmp_path / CURSOR_FILENAME).exists(), "cursor did not land in the drop root"
    assert not (processing / CURSOR_FILENAME).exists()


# ── degradation ───────────────────────────────────────────────────────────────

def test_pull_can_be_switched_off(tmp_path):
    src = _write_ifc(tmp_path)

    class _NoPull(_Cfg):
        pull_reconcile = False

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, datetime.now(timezone.utc))])
    result = IFCDropHandler(server, _NoPull()).process(str(src))

    assert server.feed_since == [], "the feed was queried despite pull being disabled"
    assert result["reconcile"] == {"skipped": "disabled"}
    assert server.pushed, "disabling pull must not disable push"


def test_an_unreachable_feed_degrades_to_push_only(tmp_path):
    """A hub outage must cost the operator nothing but the reconcile."""
    src = _write_ifc(tmp_path)

    class _Broken(_FakeServer):
        def _send(self, *a, **kw):
            raise ConnectionError("hub is down")

    server = _Broken([])
    result = IFCDropHandler(server, _Cfg()).process(str(src))

    assert not result["errors"], "a pull failure must not be reported as an ingest error"
    assert result["reconcile"]["error"], "the pull failure should still be recorded"
    assert server.pushed, "the ingest was abandoned because the pull failed"


def test_a_client_without_a_feed_endpoint_is_not_an_error(tmp_path):
    """Several legitimate callers (single-file mode, test doubles) have no base_url."""
    src = _write_ifc(tmp_path)

    class _NoEndpoint(_FakeServer):
        base_url = ""

    result = IFCDropHandler(_NoEndpoint([]), _Cfg()).process(str(src))
    assert not result["errors"]
    assert result["reconcile"]["pulled"] == 0


# ── unit-level guards on the pieces ───────────────────────────────────────────

def test_local_index_stamps_every_element_with_the_file_mtime(tmp_path):
    """Not merely convenient: leaving it unset hands every contested element away."""
    src = _write_ifc(tmp_path)
    index = build_local_index({"g1": {"zone": "Z1"}, "g2": {"zone": "Z2"}},
                              file_modified_utc(src))

    assert set(index) == {"g1", "g2"}
    assert all(entry["modified_utc"] for entry in index.values()), (
        "an element with no local timestamp always loses to the server, so a "
        "fresh export would be silently reverted")
    assert index["g1"]["tokens"]["zone"] == "Z1"


def test_cursor_host_key_is_document_scoped():
    assert cursor_host_key("abc") != cursor_host_key("def")
    assert cursor_host_key("") == "ifc", "a missing doc guid must still yield a stable key"


# ── cursor is committed only after the push (P1 regression) ────────────────────

def test_a_failed_push_does_not_advance_the_cursor(tmp_path):
    """The cursor must move only once the reconciled state has been pushed.

    Regression guard. Committing the cursor at reconcile time meant a later push
    failure left the cursor advanced past a change that never reached the hub:
    the next drop would not re-pull it and would push the local value straight
    back over the remote edit — the silent cross-host revert SB-5a exists to kill.
    """
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)
    items = [_delta(WALL_A, {"zone": "Z99"}, later)]

    class _PushDown(_FakeServer):
        def ingest_ifc_data(self, *a, **kw):
            raise ConnectionError("hub rejected the push")

    down = _PushDown(items)
    r1 = IFCDropHandler(down, _Cfg(), cursor_dir=tmp_path).process(str(src))

    assert r1["reconcile"]["applied"] == 1, "the remote edit should reconcile in memory"
    assert r1["errors"], "the push failure must be surfaced, not swallowed"
    assert "_pending_cursor" not in r1["reconcile"], "internal cursor plumbing leaked into the result"

    cursor_file = tmp_path / CURSOR_FILENAME
    assert not cursor_file.exists(), (
        "the cursor advanced despite the push failing — the reconciled remote "
        "edit is now unrecoverable on the next drop")

    # The hub recovers; re-dropping the SAME source must re-pull and re-apply,
    # not skip past the change because the cursor had already moved.
    up = _FakeServer(items)
    r2 = IFCDropHandler(up, _Cfg(), cursor_dir=tmp_path).process(str(src))

    assert r2["reconcile"]["pulled"] == 1, "the change was skipped — the cursor had advanced"
    assert r2["reconcile"]["applied"] == 1
    assert _pushed(up, WALL_A)["zone"] == "Z99", "the recovered drop failed to re-apply the remote edit"
    assert cursor_file.exists(), "the cursor should persist once the push finally succeeds"


def test_a_successful_push_still_advances_the_cursor(tmp_path):
    """The deferral must not break the happy path: a clean push commits the cursor."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)
    items = [_delta(WALL_A, {"zone": "Z99"}, later)]

    server = _FakeServer(items)
    result = IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path).process(str(src))

    assert not result["errors"]
    assert "_pending_cursor" not in result["reconcile"]
    assert (tmp_path / CURSOR_FILENAME).exists(), "a successful push must persist the cursor"


# ── a corrupt cursor resets rather than disabling pull (P2) ────────────────────

def test_a_corrupt_cursor_file_resets_instead_of_disabling_pull(tmp_path):
    """A cursor file that parses to a JSON non-dict must not crash reconcile into
    permanent push-only — it should reset, re-pull, and heal after the push."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)
    (tmp_path / CURSOR_FILENAME).write_text("[]", encoding="utf-8")  # valid JSON, not a dict

    server = _FakeServer([_delta(WALL_A, {"zone": "Z99"}, later)])
    result = IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path).process(str(src))

    rec = result["reconcile"]
    assert rec.get("error") is None, "a corrupt cursor was treated as a pull failure — pull is now dead"
    assert rec.get("cursor_reset"), "the corrupt cursor should be reported as reset"
    assert rec["pulled"] == 1, "the corrupt cursor should reset and re-pull from the beginning"
    assert rec["applied"] == 1

    stored = json.loads((tmp_path / CURSOR_FILENAME).read_text(encoding="utf-8"))
    assert isinstance(stored, dict), "the corrupt cursor was not repaired after a successful push"


# ── an echoed push is neutralised, not re-applied (P2 test gap) ────────────────

def test_an_echoed_push_is_not_re_applied_or_flagged(tmp_path):
    """Echo/loop guard: the feed has no host/origin filter, so an element we just
    pushed can reappear in it. When local already equals the echoed payload,
    reconcile must short-circuit as unchanged — never a conflict, never a revert —
    however new the echo's timestamp is."""
    src = _write_ifc(tmp_path)
    _, later, _ = _file_times(src)

    local = {"disc": "A", "loc": "BLD1", "zone": "Z01", "lvl": "L01",
             "sys": "", "func": "", "prod": "WALL", "seq": "0001", "status": ""}
    token_map = {WALL_A: dict(local)}

    # The hub echoes back exactly what we pushed, with a *later* timestamp.
    server = _FakeServer([_delta(WALL_A, dict(local), later)])
    summary = pull_and_reconcile(
        server, token_map=token_map, ifc_path=src,
        doc_guid="doc-echo", cursor_path=tmp_path / CURSOR_FILENAME)

    assert summary["pulled"] == 1
    assert summary["unchanged"] == 1, "an identical echo was not recognised as a no-op"
    assert summary["applied"] == 0 and summary["kept_local"] == 0
    assert summary["conflicts"] == 0, "an identical echo was recorded as a conflict"
    assert token_map[WALL_A]["zone"] == "Z01", "an identical echo mutated the local tokens"
    assert not conflict_sidecar_path(src).exists(), "an identical echo produced a spurious sidecar"
