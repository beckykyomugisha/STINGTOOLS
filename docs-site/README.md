# Planscape docs site

mkdocs-material generated. Renders the canonical docs the marketing
site links to and the in-app help widget points at.

## Build locally

```bash
pip install mkdocs-material
cd docs-site
mkdocs serve  # http://localhost:8000
```

## Deploy

Cloudflare Pages with build command `mkdocs build` and output dir
`site/`. Or:

```bash
mkdocs gh-deploy --remote-branch gh-pages
```

## Adding a page

1. Drop a `.md` file under `docs/`.
2. Add the entry to `mkdocs.yml > nav`.
3. Push — Cloudflare Pages rebuilds and ships in ~30 seconds.

## Style

Plain markdown. Admonitions via `!!! note "Title"`. Code blocks get
syntax highlighting + copy button via the material theme. Keep page
weight tiny — this is reference material, not a marketing surface.
