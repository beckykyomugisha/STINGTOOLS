"""
STING ArchiCAD Bridge — CLI entry point.

Usage:
    python -m StingBridge.bridge sync
    python -m StingBridge.bridge watch

Environment variables (see config.py):
    STING_PLANSCAPE_URL       http://localhost:5000
    STING_PLANSCAPE_EMAIL     admin@planscape.demo
    STING_PLANSCAPE_PASSWORD  admin123
    STING_PLANSCAPE_PROJECT_ID  <uuid>
    STING_ARCHICAD_PORT       0   (0 = auto-discover)
    STING_WRITE_BACK          1
    STING_WATCH_INTERVAL      300
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


def main() -> None:
    parser = argparse.ArgumentParser(
        description="STING ArchiCAD ↔ Planscape sync bridge"
    )
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("sync",  help="Run a single sync pass")
    sub.add_parser("watch", help="Run sync on a recurring schedule")

    args = parser.parse_args()
    cfg = BridgeConfig.from_env()

    if args.command == "sync":
        sys.exit(cmd_sync(cfg))
    elif args.command == "watch":
        sys.exit(cmd_watch(cfg))


if __name__ == "__main__":
    main()
