#!/usr/bin/env python3
"""Live cross-host round-trip integration test (Prompt 11).

Validates the two host-side cross-host-key derivations that are "equal by
construction" in the code but were never confirmed against a *real* IFC
export, plus the server-side join that depends on them:

  Test A — REVIT derivation:    IFC_GLOBAL_ID_TXT (the snapshot
            StabilizeIfcGuidsCommand writes) == the GlobalId in Revit's
            EXPORTED IFC for the same element.   [Prompt 7 assumption]

  Test B — ARCHICAD derivation: ifcopenshell.guid.compress(normalised GUID)
            (StingBridge/sync/engine.py:_ifc_global_id_from_acguid) == the
            GlobalId in ArchiCAD's EXPORTED IFC for the same element.
            [Prompt 9 assumption]

  Test C — SERVER join:         a GlobalId that genuinely appears in two
            hosts' /ifc/data payloads (same IFC lineage — e.g. Revit's
            export opened in Bonsai) resolves to BOTH hosts via
            GET /api/projects/{id}/ifc/mappings?ifcGuid=...

ARCHITECTURAL NOTE — read before interpreting results
------------------------------------------------------
Modeling the "same physical element" independently in Revit and ArchiCAD
does NOT give it one shared GlobalId: each host derives its GlobalId from
its own native GUID, so they are different 22-char strings. The cross-host
join (`/ifc/mappings?ifcGuid=`) unifies rows that SHARE a GlobalId — i.e.
one IFC lineage (Revit→IFC→Bonsai, or ArchiCAD→IFC→Bonsai). So Test C
asserts {revit, blender} on the Revit-lineage GlobalId and {archicad,
blender} on the ArchiCAD-lineage GlobalId — NOT a tri-host unification of
an independently-authored twin. See docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md.

DESIGN
------
This test is the automated half of the Prompt-11 acceptance ("or an
automated integration test if the lab has the hosts"). It is GUARDED:
every test that lacks its inputs prints SKIP and the run still exits 0, so
it is safe in CI without Revit/ArchiCAD/server present. It only FAILS on a
real mismatch — and a mismatch is the deliverable: a captured element where
a derivation equality does not hold is the drift the construction proof
cannot see.

INPUTS (all optional; each test runs only when its inputs are present)
  --revit-ifc PATH           Revit's exported .ifc
  --revit-snapshot CSV/JSON  what the Revit plugin SENT: per element
                             {label, ifcGlobalId} where ifcGlobalId is the
                             IFC_GLOBAL_ID_TXT snapshot. CSV header:
                             label,ifcGlobalId
  --archicad-ifc PATH        ArchiCAD's exported .ifc
  --archicad-guids CSV/JSON  per element {label, acGuid} as the bridge sees
                             them. CSV header: label,acGuid
  --match-by {name,tag}      how a row's `label` maps to an IFC element:
                             name = IfcRoot.Name; tag = Pset_StingTags.FullTag
                             (default: name)
  --server URL --project GUID --email --password   enable Test C

OUTPUT
  - human table to stdout
  - JSON evidence file (--out, default cross_host_evidence.json) listing
    every match, every mismatch, and the join results — paste into
    docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md § Results.

EXIT  0 = all runnable tests passed (or all skipped); 1 = a real mismatch.
"""
from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Any, Optional


# ── soft deps ───────────────────────────────────────────────────────────────
def _try_import_ifcopenshell():
    try:
        import ifcopenshell  # noqa: F401
        import ifcopenshell.guid  # noqa: F401
        return ifcopenshell
    except Exception:  # noqa: BLE001
        return None


def _try_import_requests():
    try:
        import requests  # noqa: F401
        return requests
    except Exception:  # noqa: BLE001
        return None


# ── result accounting ─────────────────────────────────────────────────────────
class Outcome:
    def __init__(self) -> None:
        self.evidence: dict[str, Any] = {"tests": {}}
        self.failed = False

    def record(self, name: str, status: str, detail: dict[str, Any]) -> None:
        self.evidence["tests"][name] = {"status": status, **detail}
        if status == "FAIL":
            self.failed = True

    def banner(self, name: str, status: str, msg: str) -> None:
        mark = {"PASS": "OK  ", "FAIL": "FAIL", "SKIP": "SKIP"}.get(status, "????")
        print(f"  [{mark}] {name}: {msg}")


# ── input loaders ───────────────────────────────────────────────────────────
def _load_label_map(path: str, value_key: str) -> dict[str, str]:
    """Load {label: value} from a CSV (header: label,<value_key>) or a JSON
    list of {label, <value_key>} / a JSON object {label: value}."""
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if p.suffix.lower() == ".json":
        data = json.loads(text)
        if isinstance(data, dict):
            return {str(k): str(v) for k, v in data.items()}
        out: dict[str, str] = {}
        for row in data:
            out[str(row["label"])] = str(row[value_key])
        return out
    # CSV
    out = {}
    reader = csv.DictReader(text.splitlines())
    for row in reader:
        out[str(row["label"]).strip()] = str(row[value_key]).strip()
    return out


def _index_ifc_globalids(ifc, match_by: str) -> dict[str, str]:
    """Return {label: GlobalId} for every product in the IFC, keyed per
    --match-by. Duplicate labels keep the first (and are reported)."""
    out: dict[str, str] = {}
    dupes: list[str] = []
    products = ifc.by_type("IfcProduct")
    for el in products:
        gid = getattr(el, "GlobalId", None)
        if not gid:
            continue
        if match_by == "tag":
            label = _sting_full_tag(el)
        else:
            label = getattr(el, "Name", None)
        if not label:
            continue
        label = str(label).strip()
        if label in out:
            dupes.append(label)
            continue
        out[label] = str(gid)
    if dupes:
        print(f"      (note: {len(dupes)} duplicate label(s) in IFC, first kept: "
              f"{', '.join(sorted(set(dupes))[:5])}{'…' if len(set(dupes)) > 5 else ''})")
    return out


def _sting_full_tag(el) -> Optional[str]:
    """Read Pset_StingTags.FullTag off an IFC element via its property sets."""
    try:
        for rel in getattr(el, "IsDefinedBy", []) or []:
            pset = getattr(rel, "RelatingPropertyDefinition", None)
            if pset is None or pset.is_a() != "IfcPropertySet":
                continue
            if pset.Name != "Pset_StingTags":
                continue
            for prop in getattr(pset, "HasProperties", []) or []:
                if getattr(prop, "Name", "") == "FullTag":
                    val = getattr(prop, "NominalValue", None)
                    if val is not None and getattr(val, "wrappedValue", None):
                        return str(val.wrappedValue)
    except Exception:  # noqa: BLE001
        return None
    return None


def _normalise_acguid(ac_guid: str) -> str:
    """Mirror StingBridge/sync/engine.py:_ifc_global_id_from_acguid exactly."""
    return ac_guid.strip().strip("{}").replace("-", "").lower()


# ── Test A — Revit snapshot == exported GlobalId ──────────────────────────────
def test_revit_derivation(args, ifcopenshell, out: Outcome) -> None:
    name = "A:revit-snapshot==export"
    if not (args.revit_ifc and args.revit_snapshot):
        out.banner(name, "SKIP", "need --revit-ifc and --revit-snapshot")
        out.record(name, "SKIP", {"reason": "missing inputs"})
        return
    if ifcopenshell is None:
        out.banner(name, "SKIP", "ifcopenshell not installed")
        out.record(name, "SKIP", {"reason": "no ifcopenshell"})
        return

    ifc = ifcopenshell.open(args.revit_ifc)
    by_label = _index_ifc_globalids(ifc, args.match_by)
    snapshot = _load_label_map(args.revit_snapshot, "ifcGlobalId")

    matches, mismatches, not_in_ifc = [], [], []
    for label, sent_gid in snapshot.items():
        export_gid = by_label.get(label)
        if export_gid is None:
            not_in_ifc.append(label)
            continue
        if sent_gid == export_gid:
            matches.append(label)
        else:
            mismatches.append({"label": label, "snapshot": sent_gid,
                               "export": export_gid})

    detail = {
        "elements_in_snapshot": len(snapshot),
        "matched": len(matches),
        "mismatched": len(mismatches),
        "label_not_found_in_ifc": len(not_in_ifc),
        "mismatches": mismatches[:50],
        "missing_labels": not_in_ifc[:50],
    }
    if mismatches:
        out.banner(name, "FAIL",
                   f"{len(mismatches)} element(s) where snapshot != exported "
                   f"GlobalId — Revit re-mapped on export. SEE EVIDENCE.")
        out.record(name, "FAIL", detail)
    elif matches:
        msg = f"{len(matches)} element(s) snapshot == exported GlobalId"
        if not_in_ifc:
            msg += f" ({len(not_in_ifc)} snapshot label(s) not found in IFC — check --match-by)"
        out.banner(name, "PASS", msg)
        out.record(name, "PASS", detail)
    else:
        out.banner(name, "SKIP", "0 labels joined IFC↔snapshot (check --match-by)")
        out.record(name, "SKIP", detail)


# ── Test B — ArchiCAD compress(GUID) == exported GlobalId ─────────────────────
def test_archicad_derivation(args, ifcopenshell, out: Outcome) -> None:
    name = "B:archicad-compress==export"
    if not (args.archicad_ifc and args.archicad_guids):
        out.banner(name, "SKIP", "need --archicad-ifc and --archicad-guids")
        out.record(name, "SKIP", {"reason": "missing inputs"})
        return
    if ifcopenshell is None:
        out.banner(name, "SKIP", "ifcopenshell not installed")
        out.record(name, "SKIP", {"reason": "no ifcopenshell"})
        return

    import ifcopenshell.guid as guid

    ifc = ifcopenshell.open(args.archicad_ifc)
    by_label = _index_ifc_globalids(ifc, args.match_by)
    ac_guids = _load_label_map(args.archicad_guids, "acGuid")

    matches, mismatches, not_in_ifc, bad_guid = [], [], [], []
    by_category: dict[str, dict[str, int]] = {}
    for label, raw_guid in ac_guids.items():
        export_gid = by_label.get(label)
        if export_gid is None:
            not_in_ifc.append(label)
            continue
        cleaned = _normalise_acguid(raw_guid)
        if len(cleaned) != 32:
            bad_guid.append(label)
            continue
        try:
            derived = guid.compress(cleaned)
        except Exception as e:  # noqa: BLE001
            bad_guid.append(f"{label} ({e})")
            continue
        # per-category bucketing so we catch "some categories derive differently"
        cat = _ifc_category_for_globalid(ifc, export_gid)
        bucket = by_category.setdefault(cat, {"match": 0, "mismatch": 0})
        if derived == export_gid:
            matches.append(label)
            bucket["match"] += 1
        else:
            mismatches.append({"label": label, "acGuid": raw_guid,
                               "compressed": derived, "export": export_gid,
                               "category": cat})
            bucket["mismatch"] += 1

    detail = {
        "elements_in_guidset": len(ac_guids),
        "matched": len(matches),
        "mismatched": len(mismatches),
        "label_not_found_in_ifc": len(not_in_ifc),
        "bad_guid": len(bad_guid),
        "by_category": by_category,
        "mismatches": mismatches[:50],
    }
    if mismatches:
        cats = sorted({m["category"] for m in mismatches})
        out.banner(name, "FAIL",
                   f"{len(mismatches)} element(s) where compress(GUID) != "
                   f"exported GlobalId; categories: {', '.join(cats)}. SEE EVIDENCE.")
        out.record(name, "FAIL", detail)
    elif matches:
        out.banner(name, "PASS",
                   f"{len(matches)} element(s) compress(GUID) == exported "
                   f"GlobalId across {len(by_category)} categor(ies)")
        out.record(name, "PASS", detail)
    else:
        out.banner(name, "SKIP", "0 labels joined IFC↔guidset (check --match-by)")
        out.record(name, "SKIP", detail)


def _ifc_category_for_globalid(ifc, gid: str) -> str:
    try:
        el = ifc.by_guid(gid)
        return el.is_a()
    except Exception:  # noqa: BLE001
        return "?"


# ── Test C — server cross-host join ───────────────────────────────────────────
def test_server_join(args, ifcopenshell, requests, out: Outcome) -> None:
    name = "C:server-join"
    if not (args.server and args.project and args.email and args.password):
        out.banner(name, "SKIP", "need --server --project --email --password")
        out.record(name, "SKIP", {"reason": "missing server args"})
        return
    if requests is None:
        out.banner(name, "SKIP", "requests not installed")
        out.record(name, "SKIP", {"reason": "no requests"})
        return
    if ifcopenshell is None or not (args.revit_ifc or args.archicad_ifc):
        out.banner(name, "SKIP", "need ifcopenshell + at least one --*-ifc")
        out.record(name, "SKIP", {"reason": "no IFC to ingest"})
        return

    base = args.server.rstrip("/")
    sess = requests.Session()
    sess.headers["Content-Type"] = "application/json"

    # login
    r = sess.post(f"{base}/api/auth/login",
                  json={"email": args.email, "password": args.password}, timeout=30)
    if r.status_code != 200:
        out.banner(name, "FAIL", f"login failed: HTTP {r.status_code}")
        out.record(name, "FAIL", {"login_status": r.status_code})
        return
    token = r.json().get("token") or r.json().get("accessToken")
    sess.headers["Authorization"] = f"Bearer {token}"

    lineages: list[dict[str, Any]] = []
    # Revit lineage: Revit-host row + Blender-host row on the SAME GlobalId
    if args.revit_ifc:
        lineages.append({"ifc": args.revit_ifc, "host": "revit",
                         "label": "revit-lineage", "expect": {"revit", "blender"}})
    # ArchiCAD lineage: ArchiCAD-host row + Blender-host row on the SAME GlobalId
    if args.archicad_ifc:
        lineages.append({"ifc": args.archicad_ifc, "host": "archicad",
                         "label": "archicad-lineage", "expect": {"archicad", "blender"}})

    join_results: list[dict[str, Any]] = []
    for lin in lineages:
        ifc = ifcopenshell.open(lin["ifc"])
        gids = [str(el.GlobalId) for el in ifc.by_type("IfcProduct")
                if getattr(el, "GlobalId", None)][: args.sample]
        if not gids:
            join_results.append({"lineage": lin["label"], "status": "SKIP",
                                 "reason": "no GlobalIds in IFC"})
            continue

        # Ingest the native host (revit/archicad) AND blender on the same file.
        for host in (lin["host"], "blender"):
            els = [_ingest_element(g, host) for g in gids]
            payload = {"host": host, "hostDocumentGuid": f"prompt11-{lin['host']}",
                       "pluginVersion": "prompt11-test", "userName": "prompt11",
                       "elements": els}
            ri = sess.post(f"{base}/api/projects/{args.project}/ifc/data",
                           json=payload, timeout=60)
            if ri.status_code != 200:
                join_results.append({"lineage": lin["label"], "status": "FAIL",
                                     "reason": f"ingest {host} HTTP {ri.status_code}",
                                     "body": ri.text[:300]})
                break
        else:
            # Join: pick the first GlobalId and confirm the host set.
            probe = gids[0]
            rg = sess.get(f"{base}/api/projects/{args.project}/ifc/mappings",
                          params={"ifcGuid": probe}, timeout=30)
            hosts_found = set()
            if rg.status_code == 200:
                for row in rg.json().get("items", rg.json().get("Items", [])):
                    h = (row.get("host") or row.get("Host") or "").lower()
                    if h:
                        hosts_found.add(h)
            ok = lin["expect"].issubset(hosts_found)
            join_results.append({
                "lineage": lin["label"], "probe_globalid": probe,
                "expected_hosts": sorted(lin["expect"]),
                "hosts_found": sorted(hosts_found),
                "status": "PASS" if ok else "FAIL",
                "mappings_http": rg.status_code,
            })

    any_fail = any(j["status"] == "FAIL" for j in join_results)
    any_pass = any(j["status"] == "PASS" for j in join_results)
    detail = {"lineages": join_results}
    if any_fail:
        out.banner(name, "FAIL", "a lineage did not resolve to its expected hosts. SEE EVIDENCE.")
        out.record(name, "FAIL", detail)
    elif any_pass:
        out.banner(name, "PASS",
                   "every lineage's GlobalId resolves to all hosts that share it")
        out.record(name, "PASS", detail)
    else:
        out.banner(name, "SKIP", "no lineage produced a result")
        out.record(name, "SKIP", detail)


def _ingest_element(gid: str, host: str) -> dict[str, Any]:
    """Minimal IfcElementDto (camelCase) — only the cross-host key matters here."""
    return {
        "ifcGlobalId": gid,
        "hostElementId": f"{host}:{gid}",
        "discipline": "M", "system": "HVAC", "product": "AHU", "sequence": "0001",
        "fullTag": "M-HVAC-AHU-0001",
        "categoryName": "Prompt11", "familyName": "round-trip",
        "isComplete": True,
    }


# ── main ──────────────────────────────────────────────────────────────────────
def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Prompt 11 live cross-host round-trip test")
    ap.add_argument("--revit-ifc")
    ap.add_argument("--revit-snapshot")
    ap.add_argument("--archicad-ifc")
    ap.add_argument("--archicad-guids")
    ap.add_argument("--match-by", choices=("name", "tag"), default="name")
    ap.add_argument("--server")
    ap.add_argument("--project")
    ap.add_argument("--email")
    ap.add_argument("--password")
    ap.add_argument("--sample", type=int, default=25,
                    help="max GlobalIds per lineage to ingest in Test C")
    ap.add_argument("--out", default="cross_host_evidence.json")
    args = ap.parse_args(argv)

    ifcopenshell = _try_import_ifcopenshell()
    requests = _try_import_requests()
    out = Outcome()

    print("Prompt 11 — live cross-host round-trip")
    print(f"  ifcopenshell: {'present' if ifcopenshell else 'ABSENT (Tests A/B/C IFC steps skip)'}")
    print(f"  requests:     {'present' if requests else 'ABSENT (Test C skips)'}")
    print("")

    test_revit_derivation(args, ifcopenshell, out)
    test_archicad_derivation(args, ifcopenshell, out)
    test_server_join(args, ifcopenshell, requests, out)

    Path(args.out).write_text(json.dumps(out.evidence, indent=2), encoding="utf-8")
    print("")
    print(f"  evidence written -> {args.out}")
    statuses = [t["status"] for t in out.evidence["tests"].values()]
    print(f"  {statuses.count('PASS')} pass · {statuses.count('FAIL')} fail · "
          f"{statuses.count('SKIP')} skip")
    return 1 if out.failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
