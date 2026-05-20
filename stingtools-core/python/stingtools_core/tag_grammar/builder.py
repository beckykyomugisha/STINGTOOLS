"""8-segment ISO 19650 tag builder.

Canonical form:  DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ
Example:         M-BLD1-Z01-L02-HVAC-SUP-AHU-0042
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional, TYPE_CHECKING

if TYPE_CHECKING:
    from ..enums.loader import EnumRegistry
    from .validator import TagValidationResult

SEGMENT_NAMES: tuple[str, ...] = (
    "Discipline", "Location", "Zone", "Level",
    "System", "Function", "Product", "Sequence",
)
SEPARATOR = "-"
SENTINEL_UNKNOWN = "XX"
SEQ_PAD = 4


@dataclass(frozen=True)
class Tag:
    discipline: str = SENTINEL_UNKNOWN
    location: str = SENTINEL_UNKNOWN
    zone: str = SENTINEL_UNKNOWN
    level: str = SENTINEL_UNKNOWN
    system: str = SENTINEL_UNKNOWN
    function: str = SENTINEL_UNKNOWN
    product: str = SENTINEL_UNKNOWN
    sequence: str = "0000"

    @property
    def segments(self) -> tuple[str, ...]:
        return (
            self.discipline, self.location, self.zone, self.level,
            self.system, self.function, self.product, self.sequence,
        )

    def to_full_tag(self) -> str:
        return SEPARATOR.join(self.segments)

    def is_complete(self) -> bool:
        return all(s and s != SENTINEL_UNKNOWN for s in self.segments)

    @classmethod
    def from_full_tag(cls, full: str) -> "Tag":
        parts = full.split(SEPARATOR)
        if len(parts) != 8:
            raise ValueError(f"FullTag must have 8 segments, got {len(parts)}: {full!r}")
        return cls(*parts)

    @classmethod
    def from_pset(cls, pset_values: dict) -> "Tag":
        """Build from a dict of Pset_StingTags property values."""
        return cls(
            discipline=str(pset_values.get("Discipline") or SENTINEL_UNKNOWN),
            location=str(pset_values.get("Location") or SENTINEL_UNKNOWN),
            zone=str(pset_values.get("Zone") or SENTINEL_UNKNOWN),
            level=str(pset_values.get("Level") or SENTINEL_UNKNOWN),
            system=str(pset_values.get("System") or SENTINEL_UNKNOWN),
            function=str(pset_values.get("Function") or SENTINEL_UNKNOWN),
            product=str(pset_values.get("Product") or SENTINEL_UNKNOWN),
            sequence=str(pset_values.get("Sequence") or "0000"),
        )

    def with_sequence(self, n: int) -> "Tag":
        from dataclasses import replace
        return replace(self, sequence=str(n).zfill(SEQ_PAD))


class TagGrammar:
    """Helper bound to an EnumRegistry — validates and renders tags."""

    def __init__(self, registry: "EnumRegistry"):
        self._reg = registry

    def validate(self, tag: Tag, stage: str = "Stage_3") -> "TagValidationResult":
        """Validate a tag against the active enums at the given RIBA stage."""
        from .validator import validate_tag
        return validate_tag(tag, self._reg, stage=stage)

    def render(self, tag: Tag) -> str:
        """Render the canonical full-tag string."""
        return tag.to_full_tag()

    @staticmethod
    def parse(full: str) -> Tag:
        return Tag.from_full_tag(full)

    def stage_required_segments(self, stage: str) -> set[str]:
        """Which segments are required at this stage (per Pset_StingTags cross-entity rules)."""
        if stage in ("Stage_0", "Stage_1"):
            return set()
        if stage == "Stage_2":
            return {"Discipline", "Location", "Level"}
        if stage == "Stage_3":
            return {"Discipline", "Location", "Zone", "Level", "System", "Sequence"}
        # Stage_4+
        return {"Discipline", "Location", "Zone", "Level", "System", "Function", "Product", "Sequence"}
