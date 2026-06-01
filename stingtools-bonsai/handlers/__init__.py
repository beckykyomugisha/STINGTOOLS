"""Bonsai event handler registration for STING."""

from __future__ import annotations

import bpy
from .on_load import on_load_post


def register() -> None:
    bpy.app.handlers.load_post.append(on_load_post)


def unregister() -> None:
    if on_load_post in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(on_load_post)
