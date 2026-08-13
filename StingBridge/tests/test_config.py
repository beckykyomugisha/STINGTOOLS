"""Tests for config-file support and its precedence against the environment.

The contract: environment variables ALWAYS win over the config file, so an
operator can override a checked-in config for a one-off run.

Run from the repo root:  python StingBridge/tests/test_config.py
"""
from __future__ import annotations

import os
import sys
import tempfile
from contextlib import contextmanager
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.config import BridgeConfig  # noqa: E402


@contextmanager
def _env(**kw):
    """Set env vars for the block, restoring prior values afterwards."""
    saved = {k: os.environ.get(k) for k in kw}
    try:
        for k, v in kw.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v
        yield
    finally:
        for k, v in saved.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v


@contextmanager
def _config_file(name: str, body: str):
    with tempfile.TemporaryDirectory() as d:
        path = Path(d) / name
        path.write_text(body, encoding="utf-8")
        yield str(path)


# Env vars that could leak in from the developer's shell and skew assertions.
_CLEAR = {k: None for k in (
    "STING_PLANSCAPE_URL", "STING_PLANSCAPE_EMAIL", "STING_PLANSCAPE_PASSWORD",
    "STING_PLANSCAPE_PROJECT_ID", "STING_BUILDING_NAME", "STING_WATCH_INTERVAL",
    "STING_WRITE_BACK", "STING_BATCH_SIZE", "STING_CONFIG_FILE",
)}


def test_toml_config_is_loaded():
    body = """
    planscape_url = "https://api.planscape.build"
    planscape_email = "a@b.com"
    building_name = "Block C"
    watch_interval = 60
    write_back = false
    """
    with _env(**_CLEAR), _config_file("stingbridge.toml", body) as p:
        cfg = BridgeConfig.from_env(p)
    assert cfg.planscape_url == "https://api.planscape.build"
    assert cfg.planscape_email == "a@b.com"
    assert cfg.building_name == "Block C"
    assert cfg.watch_interval_s == 60
    assert cfg.write_back_to_archicad is False


def test_toml_section_form_is_accepted():
    body = '[stingbridge]\nplanscape_project_id = "proj-9"\n'
    with _env(**_CLEAR), _config_file("stingbridge.toml", body) as p:
        assert BridgeConfig.from_env(p).planscape_project_id == "proj-9"


def test_env_file_is_loaded_with_prefix_and_quotes_optional():
    body = (
        "# comment line\n"
        "STING_PLANSCAPE_URL=https://from-env-file\n"
        'PLANSCAPE_EMAIL="quoted@b.com"\n'
        "\n"
        "building_name=Tower A\n"
    )
    with _env(**_CLEAR), _config_file(".env", body) as p:
        cfg = BridgeConfig.from_env(p)
    assert cfg.planscape_url == "https://from-env-file"
    assert cfg.planscape_email == "quoted@b.com"   # quotes stripped
    assert cfg.building_name == "Tower A"          # prefix optional


def test_environment_overrides_the_config_file():
    body = 'planscape_url = "https://from-file"\nbuilding_name = "From File"\n'
    with _config_file("stingbridge.toml", body) as p:
        with _env(**{**_CLEAR, "STING_PLANSCAPE_URL": "https://from-env"}):
            cfg = BridgeConfig.from_env(p)
    assert cfg.planscape_url == "https://from-env", "env must beat the file"
    assert cfg.building_name == "From File", "unset env must fall through to the file"


def test_defaults_hold_with_no_file_and_no_env():
    with _env(**_CLEAR):
        cfg = BridgeConfig.from_env("does-not-exist.toml")
    assert cfg.planscape_url == "http://localhost:5000"
    assert cfg.building_name == ""
    assert cfg.watch_interval_s == 300
    assert cfg.write_back_to_archicad is True


def test_malformed_values_fall_back_without_crashing():
    body = 'watch_interval = "not-a-number"\n'
    with _env(**_CLEAR), _config_file("stingbridge.toml", body) as p:
        assert BridgeConfig.from_env(p).watch_interval_s == 300


def test_unreadable_config_file_is_not_fatal():
    with _env(**_CLEAR), _config_file("stingbridge.toml", "this is not [ valid toml") as p:
        cfg = BridgeConfig.from_env(p)
    assert cfg.planscape_url == "http://localhost:5000"


if __name__ == "__main__":
    import traceback
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    fails = 0
    for t in tests:
        try:
            t()
            print(f"  OK   {t.__name__}")
        except Exception:
            fails += 1
            print(f"  FAIL {t.__name__}")
            traceback.print_exc()
    print(f"\n{len(tests)} tests, {fails} failures")
    sys.exit(1 if fails else 0)
