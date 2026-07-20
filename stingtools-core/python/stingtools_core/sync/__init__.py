"""Bidirectional sync — pull + reconcile (docs/MULTI_HOST_INTEGRATION_PLAN.md §1.4).

Push already existed. This package adds the read half (``pull``) and the
decision layer (``reconcile``) so a host can learn about edits made anywhere
else and resolve them deterministically.

Pure: no host SDKs, no HTTP client of its own — callers inject both.
"""
from .pull import CursorStore, PullClient
from .reconcile import (
    ReconcileEngine, ReconcileResult, Resolution, parse_utc, tokens_equal, TOKEN_KEYS,
)

__all__ = [
    "CursorStore", "PullClient",
    "ReconcileEngine", "ReconcileResult", "Resolution",
    "parse_utc", "tokens_equal", "TOKEN_KEYS",
]
