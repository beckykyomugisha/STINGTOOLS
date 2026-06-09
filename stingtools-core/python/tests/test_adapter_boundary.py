"""Phase A6 — the adapter-boundary lint is itself under test.

Loads tools/ci/check_adapter_boundary.py by path (it is a CI script, not a
package module) and asserts: (1) every banned signature trips on a synthetic
violating adapter, (2) legit Blender-UI glue does NOT false-positive, and
(3) the real adapter tree is currently clean. A regression that re-introduces
core logic into an adapter — or that weakens a rule — fails here in the core
pytest gate, not only in the standalone CI step.
"""

from __future__ import annotations

import importlib.util
from pathlib import Path

_REPO = Path(__file__).resolve().parents[3]
_LINT = _REPO / "tools" / "ci" / "check_adapter_boundary.py"


def _load():
    spec = importlib.util.spec_from_file_location("check_adapter_boundary", _LINT)
    mod = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(mod)
    return mod


def test_lint_script_exists():
    assert _LINT.exists(), f"missing lint script at {_LINT}"


def test_violating_fixture_trips_every_rule():
    mod = _load()
    violating = (
        'D = {"IfcWall": "A", "IfcDuctSegment": "M", '
        '"IfcCableSegment": "E", "IfcPipeSegment": "P"}\n'
        "rel = m.by_type('IfcRelAssignsToGroup')\n"
        "code = ''.join(c for c in name if c.isalpha())\n"
        "_SEQ_COUNTERS = {}\n"
    )
    hit = set(mod.scan_text(violating))
    expected = {vid for vid, _m, _rx in mod.BANNED}
    assert hit == expected, f"rules not all tripped: missing {expected - hit}"


def test_clean_glue_does_not_false_positive():
    mod = _load()
    clean = (
        '_PRIORITY_ITEMS = [("LOW", "Low", ""), ("HIGH", "High", "")]\n'
        "counters = {}\n"
    )
    assert mod.scan_text(clean) == []


def test_real_adapters_are_clean():
    mod = _load()
    assert mod.main(verbose=False) == 0


def test_builtin_self_test_passes():
    mod = _load()
    assert mod._self_test() == 0
