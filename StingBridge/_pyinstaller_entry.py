"""PyInstaller entry point — the frozen equivalent of the `stingbridge` console script.

PyInstaller cannot freeze ``StingBridge/bridge.py`` directly. Handing it that
file makes it the ``__main__`` module with no package context, so its relative
imports (``from ..planscape.client import ...``) fail at startup with::

    ImportError: attempted relative import with no known parent package

This shim is imported AS PART OF the package, so the relative imports resolve
exactly as they do under ``python -m`` or the installed console script. It
mirrors ``[project.scripts] stingbridge = "StingBridge.bridge:main"`` in
pyproject.toml — same target, one for wheels and one for the frozen build.

Kept as a real committed file rather than generated at build time: the
uncommitted version of it is half the reason the beta.3 build was not
reproducible.
"""

from StingBridge.bridge import main

if __name__ == "__main__":
    main()
