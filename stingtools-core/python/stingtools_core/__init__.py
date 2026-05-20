"""stingtools-core — shared Python package for the STING IFC substrate.

Public API. Stable across 0.x; breaking changes get a major bump.
"""

from .version import __version__
from .enums import EnumRegistry, Enum, EnumValue, EnumScope, EnumOrigin
from .psets import PsetRegistry, Pset, PsetProperty, ValidationRule
from .tag_grammar import TagGrammar, Tag, TagValidationResult
from .spatial import SpatialChecker, SpatialMismatch
from .exceptions import (
    StingError,
    SubstrateError,
    EnumLoadError, PsetLoadError, CorporateLockDriftError, ProjectOverlayError,
    TagError, TagFormatError,
    PlanscapeError, PlanscapeAuthError, PlanscapeNetworkError,
    AuditLogError, AuditChainError,
)

__all__ = [
    "__version__",
    # registries + models
    "EnumRegistry", "Enum", "EnumValue", "EnumScope", "EnumOrigin",
    "PsetRegistry", "Pset", "PsetProperty", "ValidationRule",
    "TagGrammar", "Tag", "TagValidationResult",
    "SpatialChecker", "SpatialMismatch",
    # exception hierarchy
    "StingError",
    "SubstrateError",
    "EnumLoadError", "PsetLoadError", "CorporateLockDriftError", "ProjectOverlayError",
    "TagError", "TagFormatError",
    "PlanscapeError", "PlanscapeAuthError", "PlanscapeNetworkError",
    "AuditLogError", "AuditChainError",
]
