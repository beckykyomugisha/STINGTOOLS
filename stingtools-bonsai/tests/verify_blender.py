"""Headless Blender verification for stingtools-bonsai.

Run with:
    blender --background --factory-startup --python tests/verify_blender.py

Proves, against the LIVE Planscape server:
  A. the packaged extension installs + enables in Blender 4.2 (panel + ops register)
  B. its planscape.client/ingest modules drive a real host="bonsai" push
  C. the same GlobalId pushed as host="revit" resolves cross-host

Exit code 0 only if every check passes; non-zero otherwise.
"""

import os
import sys
import zipfile
import traceback

import bpy
import addon_utils

ZIP = r"C:\Dev\STINGTOOLS\stingtools-bonsai\dist\stingtools_bonsai-0.1.0.zip"
IFC = r"C:\Dev\STINGTOOLS\ifc_drop\minimal_sting.ifc"
MODULE = "bl_ext.user_default.stingtools_bonsai"
BONSAI_MODULE = "bl_ext.user_default.bonsai"

SERVER = "http://localhost:5000"
EMAIL = "admin@planscape.demo"
PASSWORD = "admin123"
PROJECT_ID = "30ea4b59-4526-4897-b816-b1a2f6c5b5a1"  # NHW-2026

failures = []


def check(name, ok, detail=""):
    print(f"[{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))
    if not ok:
        failures.append(name)
    return ok


def section(t):
    print("\n========== " + t + " ==========")


# ---------------------------------------------------------------------------
section("A. ENABLE BONSAI (for ifcopenshell)")
try:
    addon_utils.enable(BONSAI_MODULE, default_set=True, persistent=True)
except Exception as e:  # noqa: BLE001
    print("bonsai enable note:", e)
try:
    import ifcopenshell  # type: ignore
    check("ifcopenshell importable", True, f"v{ifcopenshell.version}")
except Exception as e:  # noqa: BLE001
    check("ifcopenshell importable", False, str(e))


# ---------------------------------------------------------------------------
section("B. INSTALL + ENABLE stingtools-bonsai EXTENSION")
installed = False
try:
    bpy.ops.extensions.package_install_files(
        filepath=ZIP, repo="user_default", enable_on_install=True)
    installed = True
    print("installed via extensions.package_install_files")
except Exception as e:  # noqa: BLE001
    print("package_install_files failed, falling back to manual unzip:", e)

if not installed:
    # Manual install: unzip into the user_default extensions repo dir.
    repo_dir = os.path.join(
        bpy.utils.resource_path('USER'), "extensions", "user_default")
    target = os.path.join(repo_dir, "stingtools_bonsai")
    os.makedirs(repo_dir, exist_ok=True)
    with zipfile.ZipFile(ZIP) as zf:
        zf.extractall(target)
    print("unzipped to", target)
    try:
        bpy.ops.extensions.repo_refresh_all()
    except Exception as e:  # noqa: BLE001
        print("repo_refresh_all note:", e)

# Enable (idempotent if package_install already enabled it).
try:
    addon_utils.enable(MODULE, default_set=True, persistent=True)
except Exception as e:  # noqa: BLE001
    print("addon_enable note:", e)

enabled = any(m.__name__ == MODULE for m in addon_utils.modules() if getattr(m, "__name__", "") == MODULE) \
    or MODULE in bpy.context.preferences.addons
check("extension enabled", MODULE in bpy.context.preferences.addons, MODULE)

# Registration evidence.
check("STING N-panel registered (STING_PT_main)", hasattr(bpy.types, "STING_PT_main"))
check("operator sting.planscape_login registered", hasattr(bpy.ops.sting, "planscape_login"))
check("operator sting.sync_to_planscape registered", hasattr(bpy.ops.sting, "sync_to_planscape"))
try:
    panel_cat = bpy.types.STING_PT_main.bl_category
    check("panel in 'STING' tab of the N-panel", panel_cat == "STING", f"bl_category={panel_cat}")
except Exception as e:  # noqa: BLE001
    check("panel in 'STING' tab of the N-panel", False, str(e))


# ---------------------------------------------------------------------------
section("C. DRIVE THE PUSH MODULE (host=bonsai) AGAINST THE LIVE SERVER")
push_ok = False
try:
    from bl_ext.user_default.stingtools_bonsai.planscape import client as psc  # type: ignore
    from bl_ext.user_default.stingtools_bonsai.planscape import ingest as psi  # type: ignore
    check("packaged planscape.client/ingest import (stdlib-only)", True)

    import ifcopenshell  # type: ignore
    model = ifcopenshell.open(IFC)
    elements = psi.collect_elements(model)
    gids = [e["ifcGlobalId"] for e in elements]
    check("collected IFC elements with GlobalIds", len(elements) > 0,
          f"{len(elements)} elements; sample GID={gids[0] if gids else '-'}")
    doc_guid = psi.document_guid(model)

    cl = psc.PlanscapeClient(SERVER)
    token, resp = cl.login(EMAIL, PASSWORD)
    check("login → JWT (stdlib urllib)", bool(token), f"user={resp.get('userName')}, token_len={len(token)}")

    ingest_resp = cl.ingest_ifc(PROJECT_ID, "bonsai", elements,
                                host_document_guid=doc_guid,
                                plugin_version="stingtools-bonsai/0.1.0",
                                user_name=EMAIL)
    print("  ingest response:", ingest_resp)
    n = ingest_resp.get("newMappings", 0) + ingest_resp.get("updatedMappings", 0)
    check("host=bonsai push accepted", n >= len(elements), ingest_resp.get("summary", ""))

    # Verify bonsai mapping is queryable.
    one = gids[0]
    page = cl.get_mappings(PROJECT_ID, ifc_guid=one)
    hosts = sorted({m["host"] for m in page.get("items", [])})
    check("bonsai mapping resolves via /ifc/mappings", "bonsai" in hosts, f"hosts={hosts}")

    # Cross-host: push the SAME GlobalId as host=revit, confirm both resolve.
    cl.ingest_ifc(PROJECT_ID, "revit",
                  [{"ifcGlobalId": one, "hostElementId": "RVT-998877",
                    "hostDisplayLabel": "Revit Wall", "ifcClass": "IfcWall", "discipline": "A"}],
                  host_document_guid="rvt-blender-xhost", user_name=EMAIL)
    page2 = cl.get_mappings(PROJECT_ID, ifc_guid=one)
    hosts2 = sorted({m["host"] for m in page2.get("items", [])})
    check("CROSS-HOST: bonsai + revit both resolve for one GlobalId",
          "bonsai" in hosts2 and "revit" in hosts2, f"GlobalId={one} hosts={hosts2}")
    push_ok = "bonsai" in hosts2 and "revit" in hosts2
except Exception:  # noqa: BLE001
    traceback.print_exc()
    check("push module drove a real push", False, "exception (see traceback)")


# ---------------------------------------------------------------------------
section("RESULT")
if failures:
    print("FAILURES:", failures)
    sys.exit(1)
print("ALL CHECKS PASSED — extension installs, panel registers, host=bonsai push resolves cross-host.")
sys.exit(0)
