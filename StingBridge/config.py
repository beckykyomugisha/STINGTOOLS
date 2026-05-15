"""
Configuration for the STING ArchiCAD bridge.

Values are read from environment variables first, then fall back to defaults.
Environment variable prefix: STING_
"""
from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass
class BridgeConfig:
    # ArchiCAD
    archicad_port: int = 0          # 0 = auto-discover
    archicad_timeout: float = 30.0
    batch_size: int = 100

    # Planscape
    planscape_url: str = "http://localhost:5000"
    planscape_email: str = ""
    planscape_password: str = ""
    planscape_project_id: str = ""

    # Behaviour
    write_back_to_archicad: bool = True
    watch_interval_s: int = 300     # seconds between syncs in watch mode

    @classmethod
    def from_env(cls) -> "BridgeConfig":
        return cls(
            archicad_port=int(os.getenv("STING_ARCHICAD_PORT", "0")),
            archicad_timeout=float(os.getenv("STING_ARCHICAD_TIMEOUT", "30")),
            batch_size=int(os.getenv("STING_BATCH_SIZE", "100")),
            planscape_url=os.getenv("STING_PLANSCAPE_URL", "http://localhost:5000"),
            planscape_email=os.getenv("STING_PLANSCAPE_EMAIL", ""),
            planscape_password=os.getenv("STING_PLANSCAPE_PASSWORD", ""),
            planscape_project_id=os.getenv("STING_PLANSCAPE_PROJECT_ID", ""),
            write_back_to_archicad=os.getenv("STING_WRITE_BACK", "1") not in ("0", "false", "no"),
            watch_interval_s=int(os.getenv("STING_WATCH_INTERVAL", "300")),
        )
