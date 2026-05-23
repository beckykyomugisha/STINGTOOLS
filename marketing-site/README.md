# Planscape marketing site

Multi-page marketing site, no build step. Plain HTML + shared CSS.
Deploys to Cloudflare Pages (recommended), GitHub Pages, Netlify,
or any static-file host.

## Structure

```
marketing-site/
├── index.html             — landing page (hero, features, compare, Africa map, FAQ)
├── features.html          — full feature catalogue grouped by audience
├── pricing.html           — 5 tiers, comparison table, FAQ, currency support
├── about.html             — mission, team, roadmap
├── contact.html           — contact form + WhatsApp + office details
├── privacy.html           — privacy policy
├── terms.html             — terms of service
├── robots.txt
├── sitemap.xml
├── assets/
│   ├── site.css           — shared styles (nav, footer, cards, forms, articles)
│   ├── nav.html           — nav reference (copy into each page)
│   ├── footer.html        — footer reference (copy into each page)
│   └── template-guide.html — boilerplate for new guides/tutorials
├── guides/
│   ├── index.html         — guides hub (30+ guide links by category)
│   ├── getting-started.html
│   ├── revit-plugin-setup.html
│   ├── mobile-onboarding.html
│   └── … (more to write — use assets/template-guide.html as the starting point)
└── tutorials/
    ├── index.html         — tutorials hub (25+ tutorial cards)
    ├── raise-issue-from-mobile.html
    └── … (more to write)
```

## Design system

- **Brand colour**: `#E8912D` (orange) — defined in CSS as `--accent`
- **Ink**: `#1c1f26` (near-black for text)
- **Muted**: `#6c7280`
- **Font**: system stack (`-apple-system, system-ui, Segoe UI, Roboto, sans-serif`)
- **CSS variables** live in `assets/site.css` — change once, propagates everywhere
- **No JS framework** — vanilla JS for form submission and the menu toggle

Component classes (in `site.css`):

| Class | Use |
|---|---|
| `nav.site` | The top navigation bar |
| `footer.site` | The footer (4-column grid) |
| `.hero` | Big homepage hero |
| `.page-header` | Smaller header for sub-pages |
| `section` | Standard 64px-padded section, 1100px max-width |
| `.card` | Generic feature card with icon + title + description |
| `.tier` | Pricing tier card |
| `.tier.highlight` | "Most popular" tier (orange border) |
| `.tbl` | Comparison table |
| `.cta-band` | Dark CTA strip |
| `.form-stack`, `.form-row` | Form layouts |
| `article.doc` | Guide / tutorial article container |
| `.callout`, `.callout.warn`, `.callout.danger` | Inline callout boxes |
| `.faq-list`, `.faq-item` | Collapsible FAQ |
| `.chip` | Small inline pill |

## Adding a new guide or tutorial

1. Copy `assets/template-guide.html` to `guides/<slug>.html` (or `tutorials/<slug>.html`).
2. Replace the `{{TOKEN}}` placeholders.
3. Add a card on the relevant hub page (`guides/index.html` or `tutorials/index.html`).
4. Add a `<url>` entry to `sitemap.xml`.

## Deploy

### Cloudflare Pages (recommended — free, fast, no card)

```bash
# Drag-drop in the Cloudflare dashboard, or:
wrangler pages deploy marketing-site --project-name=planscape-marketing
```

DNS in Cloudflare:

```
A     planscape.app      → Cloudflare proxy
CNAME www.planscape.app  → planscape.app
```

### GitHub Pages

Add the `marketing-site/` folder as the Pages source (Settings → Pages → branch `main`, folder `/marketing-site`).
Cloudflare DNS / CNAME pointing at `<user>.github.io` if you want a custom domain.

### Netlify

```bash
netlify deploy --dir=marketing-site --prod
```

## Environment placeholders

Replace these tokens before going live:

| Token | File | What to set it to |
|---|---|---|
| `PLANSCAPE_MAPBOX_TOKEN` | `index.html` | A free Mapbox public token (no card required) |
| `__CRISP_WEBSITE_ID__` | `index.html` | Crisp Chat website ID (optional — silently no-ops without it) |
| `DEMO_VIDEO_ID` | `index.html` | YouTube video ID for the 90-second demo |
| `+256700000000` | every page | Your real WhatsApp Business number |
| `https://cal.com/planscape/demo` | `contact.html` | Your Cal.com / Calendly link |

## Performance budget

Keep each page under 200 KB on the wire so it loads fast on slow East African mobile connections. Current state:

- `index.html` ~ 30 KB (+ Mapbox GL JS lazy-loaded only when scrolled into view)
- `features.html` ~ 24 KB
- `pricing.html` ~ 20 KB
- Guides / tutorials ~ 12 KB each
- `site.css` ~ 11 KB (shared, cached)

## What's not built yet

Most guide and tutorial pages on the hubs link to pages that don't yet exist. The hub pages are deliberately comprehensive so the SEO structure and editorial direction are clear. Fill in the actual articles using `assets/template-guide.html` as the starting point.

Priority guides to write next (highest impact for trial conversions):

1. `guides/iso-19650-workflow.html`
2. `guides/billing-and-trials.html`
3. `guides/self-hosting.html`
4. `tutorials/install-mobile-app.html`
5. `tutorials/install-revit-plugin.html`
6. `tutorials/sync-revit-to-cloud.html`
