# Planscape marketing site

Single-page marketing site, no build step. Deploy targets:

- Cloudflare Pages (recommended, free tier covers our traffic forever)
- GitHub Pages
- Any static-file host (Netlify, S3 + CloudFront, MinIO + Bunny)

## Pages

- `/`           — landing page (hero + features + compare + testimonial + CTAs)
- `/signup`     — proxy to `https://api.planscape.app/auth/signup` (handled by app router; this site only links)
- `/api/pricing/html` — pricing page served by the API server
- `/privacy`    — TODO (S7.5)
- `/terms`      — TODO (S7.5)

## Deploy

```bash
# Cloudflare Pages — drag-drop or:
wrangler pages deploy marketing-site --project-name=planscape-marketing
```

Set DNS:

```
A  planscape.app    → Cloudflare proxy
A  www.planscape.app → Cloudflare proxy
```

CNAME flatten if needed.

## Editing

Plain HTML + inline CSS, no framework. Add images to `marketing-site/images/`. Keep page weight under 200 KB so it loads fast on EA mobile networks.
