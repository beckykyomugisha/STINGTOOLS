# Building the StingTools-for-Bonsai extension

The extension bundles `stingtools_core` as a **wheel** declared in
`blender_manifest.toml` (`wheels = [...]`). Blender installs that wheel into the
extension's own isolated environment, so `import stingtools_core` works with no
`sys.path` manipulation — which is what keeps Blender's extension-policy check
clean. (The wheel is git-ignored; it's produced at build time.)

## Build steps

From the repo root, with Python 3.11+ and Blender 4.2+ on the machine:

```bash
# 1. Build the shared-core wheel INTO the extension's wheels/ folder.
#    The filename must match the `wheels = [...]` entry in blender_manifest.toml.
python -m build --wheel --outdir stingtools-bonsai/wheels stingtools-core/python

# 2. Build the extension zip.
blender --command extension build \
  --source-dir stingtools-bonsai \
  --output-dir stingtools-bonsai/dist

# 3. Validate.
blender --command extension validate stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip
```

Output: `stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip`.

## Install (Blender 4.2+)

1. Install **Bonsai** first (its IFC layer is a hard runtime dependency).
2. `Edit → Preferences → Get Extensions → ⌄ → Install from Disk…` → the zip.
3. Enable it → press `N` in the 3D viewport → the **STING** tab.

## Bumping the core version

If `stingtools-core`'s version changes, update the filename in the
`wheels = [...]` line of `blender_manifest.toml` to match the new wheel, then
rebuild.

## Dev checkout (no wheel)

When running from a monorepo checkout without the wheel installed, the
extension's `__init__.py` falls back to adding `../stingtools-core/python` to
`sys.path`. That path is dev-only; a packaged install always uses the wheel.
