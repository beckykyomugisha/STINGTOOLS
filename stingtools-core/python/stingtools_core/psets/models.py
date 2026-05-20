"""Dataclasses for StingPropertySetTemplate XMLs."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class PsetProperty:
    name: str
    cardinality: str   # required | optional | prohibited
    data_type: str     # IfcLabel | IfcText | IfcInteger | IfcReal | IfcBoolean | IfcDate | IfcDateTime
    definition: str
    enumeration: Optional[str]  # name of a StingPropertyEnumeration, or None
    applies_to: tuple[str, ...]
    since: str


@dataclass(frozen=True)
class ValidationRule:
    id: str
    description: str
    active_from: str
    fail_message: str
    remediation: str


@dataclass
class Pset:
    name: str
    definition: str
    ifd_guid: str
    scope: str
    origin: str
    since_version: str
    maintainer: str
    standards_basis: str
    version: str
    template_type: str
    applicability: tuple[str, ...]
    properties: tuple[PsetProperty, ...]
    rules: tuple[ValidationRule, ...]
    stored_sha256: Optional[str] = None
    source_path: Optional[str] = None

    def get_property(self, name: str) -> Optional[PsetProperty]:
        for p in self.properties:
            if p.name == name:
                return p
        return None

    def required_properties(self) -> list[PsetProperty]:
        return [p for p in self.properties if p.cardinality == "required"]
