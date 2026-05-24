"""Exception hierarchy for stingtools-core.

All STING-side errors derive from `StingError`. Callers can catch the
base class to handle every STING-originated failure, or catch a
specific subclass for fine-grained recovery.

Stable across 0.x; new subclasses are additive.
"""

from __future__ import annotations


class StingError(Exception):
    """Base class for every error this package raises."""


class SubstrateError(StingError):
    """Errors raised while loading or validating shared/ifc/."""


class EnumLoadError(SubstrateError):
    """Failed to load or parse an enum XML file."""


class PsetLoadError(SubstrateError):
    """Failed to load or parse a Pset XML file."""


class CorporateLockDriftError(SubstrateError):
    """A corporate-locked artefact's stored SHA-256 doesn't match its
    computed canonical-JSON hash. Tampering with locked baseline content
    raises this; project overlay edits do NOT."""

    def __init__(self, name: str, stored: str, computed: str):
        super().__init__(
            f"{name}: lock drift — stored={stored[:16]}... computed={computed[:16]}..."
        )
        self.name = name
        self.stored = stored
        self.computed = computed


class ProjectOverlayError(SubstrateError):
    """A project overlay tried to redefine a reserved code (XX / * /
    SITE / EXT / GF / RF / etc.), or the overlay file is malformed."""


class TagError(StingError):
    """Errors related to the 8-segment tag grammar."""


class TagFormatError(TagError):
    """Tag string doesn't parse as 8 dash-separated segments."""


class PlanscapeError(StingError):
    """Errors from the Planscape REST client."""


class PlanscapeAuthError(PlanscapeError):
    """Authentication / authorization failure (401 / 403 from the server)."""


class PlanscapeNetworkError(PlanscapeError):
    """HTTP transport failure (connection refused, timeout, 5xx)."""


class AuditLogError(StingError):
    """Errors related to AuditLog read / write / verify."""


class AuditChainError(AuditLogError):
    """SHA-256 chain verification failed at a specific entry."""

    def __init__(self, line_number: int, reason: str):
        super().__init__(f"audit chain broken at line {line_number}: {reason}")
        self.line_number = line_number
        self.reason = reason
