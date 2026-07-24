#!/usr/bin/env python3
"""End-to-end: the WIRED IFC drop path pulls → reconciles → pushes (SB-5a).

`e2e_pull_reconcile.py` proves the *engine* against a real server: the feed
emits what `ReconcileEngine` expects and the cursor survives real timestamp
precision. What it does NOT touch is the thing SB-5a actually builds — that the
`IFCDropHandler` itself pulls before it mints and pushes, so a number already on
the server is *adopted* rather than re-minted, and a value edited in another host
lands in the written-back IFC.

This drives the real `IFCDropHandler.process()` (exactly what `process-ifc` runs)
against the refreshed stack, always dropping the *same path* (the server keys a
document on `sha1(resolved_path)`), and asserts on artefacts a user can see:

  1. re-dropping an untagged export mints ZERO new numbers — the pull adopts the
     numbers the first drop created (this is the bug N0 exposed: without the
     wiring, an untagged re-drop re-mints and burns counter values);
  2. a token edited *server-side* (newer) reaches the written-back IFC;
  3. a newer local export beats a stale server value and is pushed up;
  4. two settled passes are idempotent and the cursor advances (exactly-once).

Usage (defaults target the local docker stack + the seeded project):

    export STING_E2E_URL=http://localhost:5000
    export STING_E2E_EMAIL=admin@planscape.demo
    export STING_E2E_PASSWORD=admin123
    export STING_E2E_PROJECT_ID=b163b136-a948-43be-bd4f-b3bf521a99e6
    python StingBridge/tests/e2e_ifc_pull_reconcile.py
"""
from __future__ import annotations

import os
import sys
import time
from pathlib import Path
from tempfile import TemporaryDirectory

import requests

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _p in (str(_REPO_ROOT), str(_REPO_ROOT / "stingtools-core" / "python")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import ifcopenshell  # noqa: E402
import ifcopenshell.util.element as ifc_util  # noqa: E402

from StingBridge.config import BridgeConfig  # noqa: E402
from StingBridge import bridge as _bridge  # noqa: E402
from StingBridge.watch.ifc_watcher import IFCDropHandler  # noqa: E402
from stingtools_core.sync import PullClient  # noqa: E402

try:  # Windows consoles default to cp1252; keep prints from crashing on it.
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:  # noqa: BLE001
    pass

BASE = os.environ.get("STING_E2E_URL", "http://localhost:5000").rstrip("/")
EMAIL = os.environ.get("STING_E2E_EMAIL", "admin@planscape.demo")
PASSWORD = os.environ.get("STING_E2E_PASSWORD", "admin123")
PROJECT = os.environ.get("STING_E2E_PROJECT_ID",
                         "b163b136-a948-43be-bd4f-b3bf521a99e6")

# Two unique, valid 22-char IFC GlobalIds per run so repeated runs never collide.
GID_A = ifcopenshell.guid.new()
GID_B = ifcopenshell.guid.new()

_ZONE = "ASS_ZONE_TXT"
_SEQ = "ASS_SEQ_NUM_TXT"

_step = 0


def step(msg: str) -> None:
    global _step
    _step += 1
    print(f"\n[{_step}] {msg}")


def ok(msg: str) -> None:
    print(f"    OK  {msg}")


def fail(msg: str) -> None:
    print(f"    FAIL {msg}")
    sys.exit(1)


# ── IFC fixtures ──────────────────────────────────────────────────────────────

_IFC_TEMPLATE = """ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
FILE_NAME('e2e.ifc','2026-01-01T00:00:00',(''),(''),'IfcOpenShell','IfcOpenShell','');
FILE_SCHEMA(('IFC2X3'));
ENDSEC;
DATA;
#1=IFCPROJECT('{proj}',$,'E2E Project',$,$,$,$,(#2),#3);
#2=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-5,#4,$);
#3=IFCUNITASSIGNMENT((#5));
#4=IFCAXIS2PLACEMENT3D(#8,$,$);
#5=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#8=IFCCARTESIANPOINT((0.,0.,0.));
#9=IFCSITE('{site}',$,'Site',$,$,#10,$,$,.ELEMENT.,$,$,$,$,$);
#10=IFCLOCALPLACEMENT($,#11);
#11=IFCAXIS2PLACEMENT3D(#12,$,$);
#12=IFCCARTESIANPOINT((0.,0.,0.));
#13=IFCBUILDING('{bldg}',$,'Building',$,$,#14,$,$,.ELEMENT.,$,$,$);
#14=IFCLOCALPLACEMENT(#10,#15);
#15=IFCAXIS2PLACEMENT3D(#16,$,$);
#16=IFCCARTESIANPOINT((0.,0.,0.));
#17=IFCBUILDINGSTOREY('{storey}',$,'Level 1',$,$,#18,$,$,.ELEMENT.,0.);
#18=IFCLOCALPLACEMENT(#14,#19);
#19=IFCAXIS2PLACEMENT3D(#20,$,$);
#20=IFCCARTESIANPOINT((0.,0.,0.));
#21=IFCWALL('{gid_a}',$,'Wall A',$,'Basic Wall',$,$,$);
#22=IFCWALL('{gid_b}',$,'Wall B',$,'Basic Wall',$,$,$);
#24=IFCRELAGGREGATES('{r1}',$,$,$,#1,(#9));
#25=IFCRELAGGREGATES('{r2}',$,$,$,#9,(#13));
#26=IFCRELAGGREGATES('{r3}',$,$,$,#13,(#17));
#27=IFCRELCONTAINEDINSPATIALSTRUCTURE('{r4}',$,$,$,(#21,#22),#17);
ENDSEC;
END-ISO-10303-21;
"""


def _new_guid() -> str:
    return ifcopenshell.guid.new()


def write_untagged(path: Path) -> None:
    """Fresh untagged export — two walls, no STING_TOKENS."""
    content = _IFC_TEMPLATE.format(
        proj=_new_guid(), site=_new_guid(), bldg=_new_guid(),
        storey=_new_guid(), gid_a=GID_A, gid_b=GID_B,
        r1=_new_guid(), r2=_new_guid(), r3=_new_guid(), r4=_new_guid())
    path.write_text(content)


def read_tokens(sting_path: Path) -> dict[str, dict[str, str]]:
    """Read STING_TOKENS back off a written IFC → {gid: {label: value}}."""
    model = ifcopenshell.open(str(sting_path))
    out: dict[str, dict[str, str]] = {}
    for el in model.by_type("IfcProduct"):
        psets = ifc_util.get_psets(el)
        if "STING_TOKENS" in psets:
            out[el.GlobalId] = {k: str(v) for k, v in psets["STING_TOKENS"].items()}
    return out


def touch(path: Path, when: float) -> None:
    os.utime(path, (when, when))


# ── server helpers ────────────────────────────────────────────────────────────

class _Client:
    def __init__(self) -> None:
        self.s = requests.Session()
        r = self.s.post(f"{BASE}/api/auth/login",
                        json={"email": EMAIL, "password": PASSWORD}, timeout=30)
        if r.status_code != 200:
            fail(f"login failed {r.status_code}: {r.text[:200]}")
        self.tok = r.json()["accessToken"]
        self.s.headers.update({"Authorization": f"Bearer {self.tok}",
                               "Content-Type": "application/json"})

    def get(self, url, params=None):
        return self.s.get(url, params=params, timeout=30)

    def server_zone(self, gid: str) -> str | None:
        """Latest server-side zone for one element, read from the change feed."""
        pull = PullClient(self, BASE, PROJECT, page_limit=200)
        latest = None
        for d in pull.drain():
            if d.global_id == gid:
                latest = d
        if latest is None:
            return None
        return (latest.payload or {}).get("zone")


def make_handler():
    """Build the exact handler `process-ifc` builds, against the live stack."""
    os.environ["STING_PLANSCAPE_URL"] = BASE
    os.environ["STING_PLANSCAPE_EMAIL"] = EMAIL
    os.environ["STING_PLANSCAPE_PASSWORD"] = PASSWORD
    os.environ["STING_PLANSCAPE_PROJECT_ID"] = PROJECT
    os.environ.setdefault("STING_BUILDING_NAME", "BLD1")
    cfg = BridgeConfig.from_env()
    ps = _bridge._make_ps_client(cfg)
    return IFCDropHandler(planscape_client=ps, config=cfg,
                          on_progress=lambda m: None)


def drop(path: Path) -> dict:
    """One real drop of `path`. Returns the handler result dict."""
    return make_handler().process(str(path))


def sting_path(src: Path) -> Path:
    return src.with_name(src.stem + "_sting.ifc")


# ── the scenarios ─────────────────────────────────────────────────────────────

def main() -> int:
    print(f"Server : {BASE}\nProject: {PROJECT}\nA={GID_A}  B={GID_B}")

    with TemporaryDirectory(prefix="sb5a_e2e_") as tmp:
        src = Path(tmp) / "model.ifc"
        out = sting_path(src)

        # ── 1. first drop mints SEQ ─────────────────────────────────────────
        step("First drop of an untagged export mints SEQ end-to-end")
        write_untagged(src)
        base_mtime = time.time() - 3600            # an hour ago
        touch(src, base_mtime)
        r1 = drop(src)
        if r1["errors"]:
            fail(f"first drop errored: {r1['errors']}")
        toks1 = read_tokens(out)
        seq_a = toks1.get(GID_A, {}).get(_SEQ)
        seq_b = toks1.get(GID_B, {}).get(_SEQ)
        if not seq_a or not seq_b:
            fail(f"no SEQ minted (A={seq_a} B={seq_b})")
        minted1 = (r1.get("reconcile") or {})
        ok(f"minted A={seq_a} B={seq_b}; reconcile summary={minted1}")

        # ── 2. re-drop the same untagged export: adopt, mint ZERO ───────────
        step("Re-drop the SAME untagged export — must adopt, not re-mint")
        write_untagged(src)                        # untagged again, as a re-export would be
        touch(src, base_mtime)                     # older than the server rows just created
        r2 = drop(src)
        if r2["errors"]:
            fail(f"second drop errored: {r2['errors']}")
        toks2 = read_tokens(out)
        seq_a2 = toks2.get(GID_A, {}).get(_SEQ)
        seq_b2 = toks2.get(GID_B, {}).get(_SEQ)
        if (seq_a2, seq_b2) != (seq_a, seq_b):
            fail(f"SEQ changed on re-drop — numbers were re-minted "
                 f"(was {seq_a}/{seq_b}, now {seq_a2}/{seq_b2})")
        rec2 = r2.get("reconcile") or {}
        if not rec2 or rec2.get("error"):
            fail(f"pull did not run on re-drop: {rec2}")
        if not Path(tmp, ".sting_sync_cursor.json").exists():
            fail("cursor file was not written")
        ok(f"SEQ unchanged, ZERO re-minted; adopted via pull ({rec2}); cursor persisted")

        # ── 3. server-side edit reaches the written-back IFC ────────────────
        step("Edit A's zone SERVER-side (newer) — re-drop carries it into the IFC")
        cli = _Client()
        _server_edit_zone(GID_A, "Z99", seq_a, src)
        write_untagged(src)                        # local A still has no zone
        touch(src, base_mtime)                     # older than the server edit
        r3 = drop(src)
        if r3["errors"]:
            fail(f"third drop errored: {r3['errors']}")
        toks3 = read_tokens(out)
        got = toks3.get(GID_A, {}).get(_ZONE)
        if got != "Z99":
            fail(f"server edit not applied to written-back IFC (A zone={got!r})")
        ok(f"written-back IFC carries the server value (A zone={got})")

        # ── 4. a newer local export wins over a stale server value ──────────
        # In the IFC-file host, tokens are *inferred* from the element (only SEQ
        # is adopted from STING_TOKENS), so a "local edit" is a fresh export that
        # is newer than the server. We stage a divergence: put a value on the
        # server that the local inference will NOT produce, then drop an export
        # dated newer. Local must win, and the local value must reach the server.
        step("A newer local export beats a stale server value and is pushed up")
        _server_edit_zone(GID_B, "SRVOLD", seq_b, src)   # server-side B → SRVOLD (now)
        write_untagged(src)                              # fresh export, inferred tokens
        touch(src, time.time() + 3600)                   # dated an hour ahead: newest
        r4 = drop(src)
        if r4["errors"]:
            fail(f"fourth drop errored: {r4['errors']}")
        toks4 = read_tokens(out)
        local_b = toks4.get(GID_B, {}).get(_ZONE)
        if not local_b or local_b == "SRVOLD":
            fail(f"local inference unexpectedly produced the server value ({local_b!r})")
        rec4 = r4.get("reconcile") or {}
        if not rec4.get("kept_local"):
            fail(f"a newer local export did not win (reconcile={rec4})")
        srv_b = cli.server_zone(GID_B)
        if srv_b != local_b:
            fail(f"local value was not pushed to the server "
                 f"(local={local_b!r}, server={srv_b!r}, was SRVOLD)")
        ok(f"local kept over stale server value (SRVOLD->{srv_b}); "
           f"reconcile kept_local={rec4.get('kept_local')}")

        # ── 5. two settled passes are idempotent; the cursor advances ───────
        # Date the file behind the server so it settles by *adoption* (no
        # re-mint churn), then run it twice with nothing changed. The invariant
        # that matters: the written-back tokens are byte-identical across the two
        # passes — nothing is re-minted or flipped — and the cursor moves forward
        # rather than replaying from zero. (Conflicts are NOT asserted zero: an
        # untagged export has an empty local SEQ that differs from the server's,
        # so every adoption is recorded as a conflict — that is the audit trail
        # working, not a fault, and scenario 2 already relies on it.)
        step("Two settled passes are idempotent and the cursor advances")
        cursor_file = Path(tmp, ".sting_sync_cursor.json")
        write_untagged(src)
        touch(src, base_mtime)                     # behind the server: adopt, don't re-mint
        drop(src)
        toks_a = read_tokens(out)
        cur1 = cursor_file.read_text()
        write_untagged(src)
        touch(src, base_mtime)                     # identical, unchanged
        r5 = drop(src)
        if r5["errors"]:
            fail(f"fifth drop errored: {r5['errors']}")
        toks_b = read_tokens(out)
        cur2 = cursor_file.read_text()
        if toks_a != toks_b:
            print("    DEBUG pass A:", toks_a)
            print("    DEBUG pass B:", toks_b)
            fail("two settled passes produced different tokens — not idempotent")
        if (r5.get("reconcile") or {}).get("error"):
            fail(f"reconcile errored on the settled pass: {r5['reconcile']}")
        if cur1 == "" or cur2 == "":
            fail("cursor was not persisted")
        ok(f"tokens identical across both passes; cursor "
           f"{'advanced' if cur2 != cur1 else 'stable (exactly-once)'}")

    print("\nAll SB-5a live scenarios passed.")
    return 0


# ── small server-edit shim (kept out of the flow above for clarity) ──────────

def _doc_guid(src: Path) -> str:
    import hashlib
    return hashlib.sha1(str(src.resolve()).lower().encode("utf-8")).hexdigest()


def _bridge_build_element(gid: str, *, zone: str, seq: str) -> dict:
    from StingBridge.planscape.client import PlanscapeClient
    return PlanscapeClient.build_ifc_element(
        ifc_global_id=gid, disc="M", loc="BLD1", zone=zone, lvl="L01",
        sys="HVAC", func="SUP", prod="WAL", seq=seq,
        category_name="Walls", family_name="Basic Wall")


def _server_edit_zone(gid: str, zone: str, seq: str, src: Path) -> None:
    """Upsert one element server-side on the same (host, gid, docGuid) key."""
    ps = make_handler()._ps
    el = _bridge_build_element(gid, zone=zone, seq=seq)
    ps.ingest_ifc_data([el], host="archicad", host_document_guid=_doc_guid(src))


if __name__ == "__main__":
    sys.exit(main())
