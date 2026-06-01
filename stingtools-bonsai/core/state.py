"""Per-session state singleton.

Caches EnumRegistry, PsetRegistry, and TagGrammar instances so they
are loaded once per Blender session (or until invalidate() is called).
Operators read from `StingState.get()` — never instantiate directly.
"""

from __future__ import annotations

import logging
from typing import Any, Optional

logger = logging.getLogger("stingtools_bonsai.core.state")


class StingState:
    """Session-level singleton for substrate caches."""

    _instance: Optional["StingState"] = None

    def __init__(self) -> None:
        self._enum_registry: Optional[Any] = None
        self._pset_registry: Optional[Any] = None
        self._tag_grammar: Optional[Any] = None
        self.stage: str = "Stage_3"

    # ------------------------------------------------------------------
    # singleton access
    # ------------------------------------------------------------------

    @classmethod
    def get(cls) -> "StingState":
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    # ------------------------------------------------------------------
    # cache invalidation
    # ------------------------------------------------------------------

    def invalidate(self) -> None:
        """Drop all cached objects so the next access re-loads from disk."""
        self._enum_registry = None
        self._pset_registry = None
        self._tag_grammar = None
        logger.info("[STING] state cache invalidated")

    # ------------------------------------------------------------------
    # lazy accessors
    # ------------------------------------------------------------------

    @property
    def enum_registry(self) -> Optional[Any]:
        if self._enum_registry is None:
            try:
                from stingtools_core.enums import EnumRegistry  # type: ignore
                self._enum_registry = EnumRegistry.load()
                logger.info(
                    "[STING] EnumRegistry loaded (%d enums)",
                    len(self._enum_registry._enums),
                )
            except Exception as exc:
                logger.error("[STING] EnumRegistry load failed: %s", exc)
        return self._enum_registry

    @property
    def pset_registry(self) -> Optional[Any]:
        if self._pset_registry is None:
            try:
                from stingtools_core.psets import PsetRegistry  # type: ignore
                self._pset_registry = PsetRegistry.load()
                logger.info(
                    "[STING] PsetRegistry loaded (%d psets)",
                    len(self._pset_registry._psets),
                )
            except Exception as exc:
                logger.error("[STING] PsetRegistry load failed: %s", exc)
        return self._pset_registry

    @property
    def tag_grammar(self) -> Optional[Any]:
        if self._tag_grammar is None:
            reg = self.enum_registry
            if reg is not None:
                try:
                    from stingtools_core.tag_grammar import TagGrammar  # type: ignore
                    self._tag_grammar = TagGrammar(reg)
                    logger.info("[STING] TagGrammar ready")
                except Exception as exc:
                    logger.error("[STING] TagGrammar init failed: %s", exc)
        return self._tag_grammar
