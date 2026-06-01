"""Handler: invalidate StingState cache when Bonsai opens an IFC file."""

from __future__ import annotations


def on_load_post(filepath: str) -> None:  # Blender passes filepath as positional arg
    """Called by bpy.app.handlers.load_post after every file load."""
    try:
        from ..core.state import StingState
        StingState.get().invalidate()
    except Exception as exc:
        print(f"[STING] on_load_post — state invalidation failed: {exc}")
    else:
        print("[STING] IFC loaded — substrate cache refreshed")
