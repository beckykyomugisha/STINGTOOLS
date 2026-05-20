"""Per-segment + grammar-level validation for the 8-segment tag."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from ..enums.loader import EnumRegistry
    from .builder import Tag

SEGMENT_TO_ENUM = {
    "Discipline": "StingDisciplineCodes",
    "Location":   "StingLocationCodes",
    "Zone":       "StingZoneCodes",
    "Level":      "StingLevelCodes",
    "System":     "StingSystemCodes",
    "Function":   "StingFunctionCodes",
    "Product":    "StingProductCodes",
}

SEQ_RE = re.compile(r"^\d{4}$")
GENERAL_RE = re.compile(r"^[A-Za-z0-9_\-\*]+$")


@dataclass
class TagValidationResult:
    is_valid: bool = True
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    sentinel_segments: list[str] = field(default_factory=list)
    deprecated_codes: list[tuple[str, str]] = field(default_factory=list)  # (segment, code)

    def fail(self, msg: str) -> None:
        self.is_valid = False
        self.errors.append(msg)

    def warn(self, msg: str) -> None:
        self.warnings.append(msg)


def validate_tag(tag: "Tag", registry: "EnumRegistry", stage: str = "Stage_3") -> TagValidationResult:
    """Validate every segment of a tag against the registry's enums.

    Per-stage required-segment enforcement matches Pset_StingTags
    CrossEntityValidationRules ActiveFrom semantics.
    """
    from .builder import TagGrammar, SENTINEL_UNKNOWN
    res = TagValidationResult()

    required = TagGrammar(registry).stage_required_segments(stage)
    segments = {
        "Discipline": tag.discipline,
        "Location":   tag.location,
        "Zone":       tag.zone,
        "Level":      tag.level,
        "System":     tag.system,
        "Function":   tag.function,
        "Product":    tag.product,
        "Sequence":   tag.sequence,
    }

    for seg_name, value in segments.items():
        # required-at-stage check
        if seg_name in required and (not value or value == SENTINEL_UNKNOWN):
            res.fail(f"{seg_name}: required at {stage} but empty / XX")
            continue

        # sentinel tracking
        if value == SENTINEL_UNKNOWN:
            res.sentinel_segments.append(seg_name)
            continue

        # Sequence: 4-digit zero-padded
        if seg_name == "Sequence":
            if not SEQ_RE.match(value):
                res.fail(f"Sequence: {value!r} must be 4-digit zero-padded (e.g. 0042)")
            continue

        # Other segments: must match general pattern
        if not GENERAL_RE.match(value):
            res.fail(f"{seg_name}: {value!r} contains characters outside [A-Za-z0-9_\\-]")
            continue

        # Membership check against the enum
        enum_name = SEGMENT_TO_ENUM.get(seg_name)
        if not enum_name:
            continue
        enum = registry.get(enum_name)
        if enum is None:
            res.warn(f"{seg_name}: enum {enum_name} not loaded; skipping membership check")
            continue

        ev = enum.get(value)
        if ev is None:
            res.fail(f"{seg_name}: {value!r} not a member of {enum_name}")
            continue
        if ev.deprecated:
            res.deprecated_codes.append((seg_name, value))
            res.warn(f"{seg_name}: {value!r} is deprecated"
                     + (f"; use {ev.replaced_by!r}" if ev.replaced_by else ""))

    return res
