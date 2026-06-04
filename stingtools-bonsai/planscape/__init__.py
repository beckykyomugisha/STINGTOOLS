"""Blender-side Planscape federation for StingTools-for-Bonsai.

Self-contained and dependency-free: the HTTP client (`client.py`) uses
only the Python standard library (`urllib`), and the element collector
(`ingest.py`) imports nothing from Blender (`bpy`) so it can be driven
headlessly (`blender --background --python`) and unit-tested.

This deliberately does NOT depend on `stingtools_core` / `requests` /
any unvendored package, so the extension works on a stock Blender 4.2+
(with Bonsai for the IFC layer) with no pip installs.
"""
