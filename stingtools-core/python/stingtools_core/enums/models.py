"""Dataclasses mirroring shared/ifc/enums/_schema.xsd."""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum as PyEnum
from typing import Optional


class EnumScope(PyEnum):
    CORPORATE = "corporate"
    PROJECT_TEMPLATE = "project_template"


class EnumOrigin(PyEnum):
    BASELINE = "baseline"
    PROJECT = "project"


@dataclass(frozen=True)
class EnumValue:
    code: str
    sentinel: bool
    definition: str
    since: str
    deprecated: bool = False
    deprecated_in_version: Optional[str] = None
    replaced_by: Optional[str] = None
    display_form: Optional[str] = None

    def display(self) -> str:
        """Render form: display_form if set, else the raw code."""
        return self.display_form or self.code


@dataclass
class Enum:
    name: str
    definition: str
    ifd_guid: str
    scope: EnumScope
    origin: EnumOrigin
    since_version: str
    maintainer: str
    standards_basis: str
    version: str
    primary_ifc_type: str
    applicable_ifc_versions: tuple[str, ...]
    values: tuple[EnumValue, ...]
    bsdd_iri: Optional[str] = None
    stored_sha256: Optional[str] = None
    locked_at_version: Optional[str] = None
    source_path: Optional[str] = None  # set by the loader

    # convenient lookups
    _by_code: dict = field(default_factory=dict, init=False, repr=False)

    def __post_init__(self):
        self._by_code = {v.code: v for v in self.values}

    def get(self, code: str) -> Optional[EnumValue]:
        return self._by_code.get(code)

    def __contains__(self, code: str) -> bool:
        return code in self._by_code

    def codes(self, include_sentinels: bool = True, include_deprecated: bool = False) -> list[str]:
        return [
            v.code for v in self.values
            if (include_sentinels or not v.sentinel)
            and (include_deprecated or not v.deprecated)
        ]

    def sentinels(self) -> list[str]:
        return [v.code for v in self.values if v.sentinel]

    def is_project_scoped(self) -> bool:
        return self.scope is EnumScope.PROJECT_TEMPLATE
