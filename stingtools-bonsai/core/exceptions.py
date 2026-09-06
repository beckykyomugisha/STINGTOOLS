"""STING Bonsai — domain exceptions.

Importable headlessly (no bpy / ifcopenshell dependency at top level).
"""
from __future__ import annotations


class StingTokenLockError(Exception):
    """Raised when a write_tag_segment() call targets a locked token.

    The STING_TOKEN_LOCK_BOOL property on Pset_StingTags prevents the named
    token from being overwritten once set.  The error carries the names and
    values involved so the caller can surface a helpful message.

    Attributes:
        param_name:      The Pset_StingTags property that is locked.
        locked_value:    The current (locked) value of that property.
        attempted_value: The value that was rejected.
    """

    def __init__(self, param_name: str, locked_value: str, attempted_value: str) -> None:
        self.param_name = param_name
        self.locked_value = locked_value
        self.attempted_value = attempted_value
        super().__init__(
            f"Token '{param_name}' is locked to '{locked_value}'; "
            f"cannot overwrite with '{attempted_value}'. "
            "Set STING_TOKEN_LOCK_BOOL=False to unlock."
        )
