"""
Configuration for the STING ArchiCAD bridge.

Resolution order (first hit wins):

  1. environment variables  (STING_*)
  2. a config file          (stingbridge.toml, else .env)
  3. the dataclass defaults

Environment always beats the file, so an operator can override a checked-in
config for a one-off run without editing it.

The config file is looked up at ``STING_CONFIG_FILE`` if set, otherwise
``stingbridge.toml`` then ``.env`` in the working directory. Keys are matched
case-insensitively and the ``STING_`` prefix is optional, so all of
``STING_PLANSCAPE_URL``, ``planscape_url`` and ``Planscape_Url`` are the same
setting.
"""
from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from pathlib import Path

log = logging.getLogger(__name__)

_TRUTHY_FALSE = ("0", "false", "no", "off")


def _normalise_key(key: str) -> str:
    k = key.strip().upper().replace("-", "_")
    return k if k.startswith("STING_") else f"STING_{k}"


def _parse_env_file(path: Path) -> dict[str, str]:
    """Parse a minimal KEY=VALUE .env file. Blank lines and # comments skipped."""
    out: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        value = value.strip()
        # Strip one matching pair of surrounding quotes.
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        out[_normalise_key(key)] = value
    return out


def _parse_toml_file(path: Path) -> dict[str, str]:
    """Parse stingbridge.toml — flat keys, or nested under [stingbridge]."""
    try:
        import tomllib  # stdlib on Python 3.11+
    except ModuleNotFoundError:  # pragma: no cover - we require >=3.11
        log.warning("tomllib unavailable — %s ignored", path.name)
        return {}
    with open(path, "rb") as f:
        data = tomllib.load(f)
    if isinstance(data.get("stingbridge"), dict):
        data = data["stingbridge"]
    out: dict[str, str] = {}
    for key, value in data.items():
        if isinstance(value, dict):
            continue  # ignore unrelated sub-tables
        if isinstance(value, bool):
            value = "1" if value else "0"
        out[_normalise_key(key)] = str(value)
    return out


def load_config_file(explicit_path: str | None = None) -> dict[str, str]:
    """Return settings from the config file, or {} when there is none."""
    candidates: list[Path] = []
    chosen = explicit_path or os.getenv("STING_CONFIG_FILE", "")
    if chosen:
        candidates.append(Path(chosen))
    else:
        candidates.extend([Path("stingbridge.toml"), Path(".env")])

    for path in candidates:
        if not path.is_file():
            continue
        try:
            values = (
                _parse_toml_file(path)
                if path.suffix.lower() == ".toml"
                else _parse_env_file(path)
            )
        except Exception as e:  # noqa: BLE001 — a bad config must not be fatal
            log.warning("Could not read config file %s: %s", path, e)
            return {}
        log.info("Loaded %d settings from %s", len(values), path)
        return values

    return {}


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
    verify_write_back: bool = True   # re-read AC after write to confirm
    watch_interval_s: int = 300      # seconds between syncs in watch mode
    ifc_drop_dir: str = ""           # folder to watch for IFC files
    building_name: str = ""          # drives the LOC token; AC exposes no building name

    @classmethod
    def from_env(cls, config_file: str | None = None) -> "BridgeConfig":
        file_values = load_config_file(config_file)

        def get(name: str, default: str) -> str:
            """Environment first, then the config file, then the default."""
            key = _normalise_key(name)
            env = os.getenv(key)
            if env is not None and env != "":
                return env
            return file_values.get(key, default)

        def get_bool(name: str, default: str = "1") -> bool:
            return get(name, default).strip().lower() not in _TRUTHY_FALSE

        def get_int(name: str, default: str) -> int:
            raw = get(name, default)
            try:
                return int(str(raw).strip())
            except ValueError:
                log.warning("%s=%r is not an integer — using %s", name, raw, default)
                return int(default)

        def get_float(name: str, default: str) -> float:
            raw = get(name, default)
            try:
                return float(str(raw).strip())
            except ValueError:
                log.warning("%s=%r is not a number — using %s", name, raw, default)
                return float(default)

        return cls(
            archicad_port=get_int("ARCHICAD_PORT", "0"),
            archicad_timeout=get_float("ARCHICAD_TIMEOUT", "30"),
            batch_size=get_int("BATCH_SIZE", "100"),
            planscape_url=get("PLANSCAPE_URL", "http://localhost:5000"),
            planscape_email=get("PLANSCAPE_EMAIL", ""),
            planscape_password=get("PLANSCAPE_PASSWORD", ""),
            planscape_project_id=get("PLANSCAPE_PROJECT_ID", ""),
            write_back_to_archicad=get_bool("WRITE_BACK"),
            verify_write_back=get_bool("VERIFY_WRITE_BACK"),
            watch_interval_s=get_int("WATCH_INTERVAL", "300"),
            ifc_drop_dir=get("IFC_DROP_DIR", ""),
            building_name=get("BUILDING_NAME", ""),
        )
