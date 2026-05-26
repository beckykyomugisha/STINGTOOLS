"""ifctester wrapper. Skip-if-missing — importing this module is safe
even without ifctester / ifcopenshell installed."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional


@dataclass
class IdsSpecResult:
    spec_name: str
    identifier: str
    passed: bool
    applicable_count: int
    failed_count: int
    failure_messages: list[str] = field(default_factory=list)


@dataclass
class IdsResult:
    ids_path: str
    ifc_path: str
    available: bool                         # ifctester + ifcopenshell installed?
    all_passed: bool = True
    specs: list[IdsSpecResult] = field(default_factory=list)
    skip_reason: Optional[str] = None

    def summary(self) -> str:
        if not self.available:
            return f"SKIP {Path(self.ids_path).name}: {self.skip_reason}"
        passed = sum(1 for s in self.specs if s.passed)
        return f"{Path(self.ids_path).name}: {passed}/{len(self.specs)} specs passed"


class IdsRunner:
    """Optional integration with buildingSMART ifctester.

    Usage:
        runner = IdsRunner()
        if runner.available:
            result = runner.run("path/to/spec.ids", "path/to/model.ifc")
    """

    def __init__(self) -> None:
        self._ifctester = self._try_import("ifctester")
        self._ifcopenshell = self._try_import("ifcopenshell")

    @staticmethod
    def _try_import(name: str) -> Any:
        try:
            return __import__(name)
        except ImportError:
            return None

    @property
    def available(self) -> bool:
        return self._ifctester is not None and self._ifcopenshell is not None

    def run(self, ids_path: str | Path, ifc_path: str | Path) -> IdsResult:
        if not self.available:
            return IdsResult(
                ids_path=str(ids_path),
                ifc_path=str(ifc_path),
                available=False,
                skip_reason="ifctester / ifcopenshell not installed (pip install stingtools-core[ids,ifc])",
            )

        from ifctester import ids as ids_mod  # type: ignore
        ifc = self._ifcopenshell.open(str(ifc_path))
        spec = ids_mod.open(str(ids_path))
        spec.validate(ifc)

        specs: list[IdsSpecResult] = []
        all_passed = True
        for s in getattr(spec, "specifications", []) or []:
            passed = getattr(s, "status", None) is True
            if not passed:
                all_passed = False
            specs.append(IdsSpecResult(
                spec_name=getattr(s, "name", ""),
                identifier=getattr(s, "identifier", "") or "",
                passed=passed,
                applicable_count=len(getattr(s, "applicable_entities", []) or []),
                failed_count=len(getattr(s, "failed_entities", []) or []),
                failure_messages=[
                    str(getattr(e, "reason", "")) for e in (getattr(s, "failed_entities", []) or [])
                ][:10],  # cap at 10 to keep result objects small
            ))

        return IdsResult(
            ids_path=str(ids_path),
            ifc_path=str(ifc_path),
            available=True,
            all_passed=all_passed,
            specs=specs,
        )
