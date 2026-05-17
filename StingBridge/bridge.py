"""
STING ArchiCAD Bridge — CLI entry point.

Usage:
    python -m StingBridge.bridge sync
    python -m StingBridge.bridge watch
    python -m StingBridge.bridge watch-ifc --drop-dir /path/to/IFC_DROP
    python -m StingBridge.bridge process-ifc /path/to/model.ifc

Environment variables (see config.py):
    STING_PLANSCAPE_URL       http://localhost:5000
    STING_PLANSCAPE_EMAIL     admin@planscape.demo
    STING_PLANSCAPE_PASSWORD  admin123
    STING_PLANSCAPE_PROJECT_ID  <uuid>
    STING_ARCHICAD_PORT       0   (0 = auto-discover)
    STING_WRITE_BACK          1
    STING_WATCH_INTERVAL      300
    STING_IFC_DROP_DIR        (path to watch for IFC files)
"""
from __future__ import annotations

import argparse
import logging
import sys
import time

from .archicad.client import ArchiCadClient, ArchiCadError
from .config import BridgeConfig
from .planscape.client import PlanscapeClient, PlanscapeAuthError
from .sync.engine import SyncEngine

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s — %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("stingbridge")


def _make_clients(cfg: BridgeConfig) -> tuple[ArchiCadClient, PlanscapeClient]:
    # ArchiCAD
    if cfg.archicad_port > 0:
        ac = ArchiCadClient(port=cfg.archicad_port)
        info = ac.get_product_info()
        log.info("ArchiCAD connected: %s", info.get("productName", "?"))
    else:
        log.info("Discovering ArchiCAD …")
        ac = ArchiCadClient.discover()

    # Planscape
    if not cfg.planscape_email or not cfg.planscape_password:
        log.error(
            "STING_PLANSCAPE_EMAIL and STING_PLANSCAPE_PASSWORD must be set"
        )
        sys.exit(1)
    if not cfg.planscape_project_id:
        log.error("STING_PLANSCAPE_PROJECT_ID must be set")
        sys.exit(1)

    ps = PlanscapeClient(
        base_url=cfg.planscape_url,
        project_id=cfg.planscape_project_id,
    )
    ps.login(cfg.planscape_email, cfg.planscape_password)

    return ac, ps


def cmd_sync(cfg: BridgeConfig) -> int:
    ac, ps = _make_clients(cfg)
    engine = SyncEngine(
        ac_client=ac,
        planscape_client=ps,
        write_back=cfg.write_back_to_archicad,
        batch_size=cfg.batch_size,
    )
    result = engine.run()
    log.info("Sync complete — %s", result.summary())
    for err in result.errors:
        log.error("  ! %s", err)
    return 1 if result.errors else 0


def cmd_watch(cfg: BridgeConfig) -> int:
    log.info(
        "Watch mode — syncing every %d seconds. Ctrl+C to stop.",
        cfg.watch_interval_s,
    )
    while True:
        try:
            rc = cmd_sync(cfg)
            if rc != 0:
                log.warning("Sync had errors — will retry next interval")
        except ArchiCadError as e:
            log.error("ArchiCAD unreachable: %s — will retry", e)
        except PlanscapeAuthError as e:
            log.error("Planscape auth error: %s — will retry", e)
        except KeyboardInterrupt:
            log.info("Watch stopped.")
            return 0

        try:
            time.sleep(cfg.watch_interval_s)
        except KeyboardInterrupt:
            log.info("Watch stopped.")
            return 0


def _make_ps_client(cfg: BridgeConfig) -> PlanscapeClient:
    """Create and log in a PlanscapeClient without needing ArchiCAD."""
    if not cfg.planscape_email or not cfg.planscape_password:
        log.error("STING_PLANSCAPE_EMAIL and STING_PLANSCAPE_PASSWORD must be set")
        sys.exit(1)
    if not cfg.planscape_project_id:
        log.error("STING_PLANSCAPE_PROJECT_ID must be set")
        sys.exit(1)
    ps = PlanscapeClient(base_url=cfg.planscape_url, project_id=cfg.planscape_project_id)
    ps.login(cfg.planscape_email, cfg.planscape_password)
    return ps


def cmd_watch_ifc(cfg: BridgeConfig, drop_dir: str) -> int:
    """Event-driven IFC drop-folder watcher — no ArchiCAD required."""
    from .watch.ifc_watcher import watch_drop_folder
    ps = _make_ps_client(cfg)
    log.info("IFC drop-folder watch starting: %s", drop_dir)
    try:
        watch_drop_folder(
            drop_dir=drop_dir,
            planscape_client=ps,
            config=cfg,
            on_progress=lambda msg: log.info("  %s", msg),
        )
    except RuntimeError as e:
        log.error("%s", e)
        return 1
    return 0


def cmd_process_ifc(cfg: BridgeConfig, ifc_path: str) -> int:
    """Process a single IFC file immediately — no ArchiCAD required."""
    from .watch.ifc_watcher import IFCDropHandler
    ps = _make_ps_client(cfg)
    handler = IFCDropHandler(
        planscape_client=ps,
        config=cfg,
        on_progress=lambda msg: log.info("  %s", msg),
    )
    result = handler.process(ifc_path)
    log.info(
        "IFC processed — elements: %d  synced: %d  errors: %d",
        result["elements"], result["synced"], len(result["errors"]),
    )
    for err in result["errors"]:
        log.error("  ! %s", err)
    if result["glb_path"]:
        log.info("  GLB: %s", result["glb_path"])
    return 1 if result["errors"] else 0


def main() -> None:
    parser = argparse.ArgumentParser(
        description="STING ArchiCAD ↔ Planscape sync bridge"
    )
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("sync",  help="Run a single sync pass (ArchiCAD must be open)")
    sub.add_parser("watch", help="Run sync on a recurring schedule (ArchiCAD must be open)")

    p_watch_ifc = sub.add_parser(
        "watch-ifc", help="Watch a folder for IFC files — no ArchiCAD needed"
    )
    p_watch_ifc.add_argument(
        "--drop-dir", default=None,
        help="Folder to watch (default: STING_IFC_DROP_DIR env or ./IFC_DROP)",
    )

    p_proc_ifc = sub.add_parser(
        "process-ifc", help="Process a single IFC file immediately"
    )
    p_proc_ifc.add_argument("ifc_path", help="Path to the .ifc file")

    args = parser.parse_args()
    cfg = BridgeConfig.from_env()

    if args.command == "sync":
        sys.exit(cmd_sync(cfg))
    elif args.command == "watch":
        sys.exit(cmd_watch(cfg))
    elif args.command == "watch-ifc":
        drop_dir = (
            args.drop_dir
            or cfg.ifc_drop_dir
            or "./IFC_DROP"
        )
        sys.exit(cmd_watch_ifc(cfg, drop_dir))
    elif args.command == "process-ifc":
        sys.exit(cmd_process_ifc(cfg, args.ifc_path))


if __name__ == "__main__":
    main()
