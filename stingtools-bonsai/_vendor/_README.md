# stingtools-bonsai/_vendor/

Empty in the Day-1 scaffold + in dev installs. Populated only by the
**packaged-extension build step** for distribution:

```bash
# Done at release time, not in dev:
cp -r stingtools-core/python/stingtools_core \
      stingtools-bonsai/_vendor/stingtools_core
```

The add-on's `__init__.py` injects `_vendor/` onto `sys.path` so a
`zip`-packaged extension is self-contained (no PyPI dependency, no
relative-path discovery of `stingtools-core/` upstream).

In dev: `_vendor/` stays empty; `stingtools_core` resolves via the
relative-path injection that walks up to the repo root.
