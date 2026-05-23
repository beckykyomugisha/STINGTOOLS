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
├── robots.txt             — search-engine policy
├── sitemap.xml            — all URLs for crawlers
├── _headers               — Cloudflare/Netlify HTTP headers (CSP, caching, HSTS)
├── _redirects             — Cloudflare/Netlify redirect rules (/features → /features.html etc.)
├── wrangler.toml          — Cloudflare Pages config for `wrangler pages deploy`
├── DEPLOY.md              — step-by-step deploy + domain-purchase walkthrough
├── README.md              — this file
├── assets/
│   ├── site.css           — shared styles (nav, footer, cards, forms, articles, blog)
│   ├── nav.html           — nav reference (copy into each page)
│   ├── footer.html        — footer reference (copy into each page)
│   └── template-guide.html — boilerplate for new guides/tutorials
├── guides/                — 9 complete guides + hub with 30+ link slots
│   ├── index.html
│   ├── getting-started.html
│   ├── revit-plugin-setup.html
│   ├── mobile-onboarding.html
│   ├── inviting-team.html
│   ├── creating-first-project.html
│   ├── iso-19650-workflow.html
│   ├── billing-and-trials.html
│   ├── self-hosting.html
│   ├── raising-issues.html
│   └── gps-site-map.html
├── tutorials/             — 5 complete tutorials + hub with 25+ link slots
│   ├── index.html
│   ├── create-first-project.html
│   ├── install-mobile-app.html
│   ├── install-revit-plugin.html
│   ├── raise-issue-from-mobile.html
│   └── capture-site-photos.html
└── blog/                  — 3 posts + RSS feed
    ├── index.html
    ├── rss.xml
    ├── 2026-05-23-why-we-built-planscape.html
    ├── 2026-05-20-iso-19650-in-east-africa.html
    └── 2026-05-15-offline-first-mobile-bim.html
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

**Full walkthrough in [DEPLOY.md](./DEPLOY.md)** — covers domain purchase, Cloudflare Pages setup, DNS, email, analytics, and search-engine submission. Total cost: ~$10/year (domain only).

Quick TL;DR for someone who's done this before:

```bash
# Deploy to Cloudflare Pages via CLI
wrangler pages deploy . --project-name=planscape-marketing

# Or connect GitHub repo in the dashboard: Workers & Pages → Create → Pages → Connect to Git
# Build settings: framework = None, build command = empty, output dir = marketing-site
```

The `_headers`, `_redirects`, and `wrangler.toml` are pre-configured for Cloudflare Pages. They also work on Netlify with minimal changes.

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

## What's already written vs still to write

**Top-level pages** — all complete: index, features, pricing, about, contact, privacy, terms.

**Guides** — 9 complete (getting-started, revit-plugin-setup, mobile-onboarding, inviting-team, creating-first-project, iso-19650-workflow, billing-and-trials, self-hosting, raising-issues, gps-site-map). ~20 more linked from the hub for editorial direction.

**Tutorials** — 5 complete (create-first-project, install-mobile-app, install-revit-plugin, raise-issue-from-mobile, capture-site-photos). ~20 more linked from the hub.

**Blog** — 3 complete posts (why-we-built-planscape, iso-19650-in-east-africa, offline-first-mobile-bim) + RSS feed.

Priority articles to write next:

1. `guides/photo-capture.html` (referenced by raising-issues + capture-site-photos)
2. `guides/auto-tagging.html` (highest-volume Revit-plugin command)
3. `tutorials/sync-revit-to-cloud.html` (key conversion driver for Revit users)
4. `tutorials/gps-site-map.html` (key mobile-first workflow)
5. `tutorials/tag-elements-in-revit.html` (advanced ISO 19650 workflow)
