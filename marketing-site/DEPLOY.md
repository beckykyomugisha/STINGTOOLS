# Deploying Planscape marketing site

Step-by-step walkthrough from "no domain" to "live on planscape.co with HTTPS". Estimated time: 30 minutes if you've never done this before, 5 minutes if you have.

**Total cost:** ~$10/year (domain only). Cloudflare Pages hosting, Cloudflare DNS, SSL certificate are all free.

---

## Stage 1 — Buy the domain (5 min, ~$10/yr)

### Best option: Cloudflare Registrar

Cloudflare sells domains **at cost** — no markup. A `.com` is $10.44/yr at the time of writing. They don't try to upsell you. They renew at the same price.

**Steps:**

1. Sign up at <https://dash.cloudflare.com/sign-up> (free Cloudflare account).
2. In the dashboard, click **Domain Registration → Register Domains**.
3. Search for `planscape.co` (or whatever you've chosen).
4. The `.app` TLD is $13.18/yr at cost. The `.com` is $10.44/yr. Pick whichever.
5. Pay with card. Domain is active within ~5 minutes.

Cloudflare automatically adds the domain to your account with their DNS — saves you a step later.

### Alternative: Namecheap

If for some reason Cloudflare Registrar isn't available in your country:

1. Go to <https://www.namecheap.com>.
2. Search and buy your domain (~$11/yr for `.com`).
3. **Important**: opt into WhoisGuard (free — keeps your details private).
4. Later you'll point DNS at Cloudflare (Stage 2).

### Domains to avoid

- **GoDaddy** — upsells aggressively, hikes renewal prices, generally hostile UX.
- **Free domains** (`.tk`, `.ga`, etc.) — unreliable, often blacklisted by spam filters, terrible for SEO.

### What to register

- Always register the `.com` if available. It's the default people type.
- Register the `.app` if you're using it as your canonical (we use planscape.co).
- Optionally register your country TLD too (e.g. `.co.ug`) — costs more (~$25/yr) but useful if you want to localise eventually.

---

## Stage 2 — Set up Cloudflare for the domain (5 min)

Skip this stage if you bought through Cloudflare Registrar — already done.

If you bought elsewhere:

1. In the Cloudflare dashboard, click **Add a Site**.
2. Type your domain. Pick the **Free** plan.
3. Cloudflare scans existing DNS records and lists them. Confirm.
4. Cloudflare shows two **nameservers** (e.g. `kane.ns.cloudflare.com`, `april.ns.cloudflare.com`).
5. Go back to your registrar's dashboard (Namecheap), find **Nameservers**, switch to "Custom DNS", paste the two Cloudflare nameservers.
6. Save. Propagation takes 5 minutes to a few hours.
7. Cloudflare emails you when the site is **Active**.

---

## Stage 3 — Push the site to GitHub (5 min)

Cloudflare Pages deploys from GitHub. (You can also drag-drop files, but Git-based is the right long-term move.)

The marketing site is already in your repo at `marketing-site/`. You have two options:

### Option A — Use the existing monorepo

Cloudflare Pages can deploy a subdirectory. Skip to Stage 4 if you're happy with that.

### Option B — Split to a dedicated repo (recommended)

Cleaner deploys, doesn't trigger on every backend change.

```bash
# From your existing repo
cd /tmp
git clone --filter=blob:none --no-checkout https://github.com/beckykyomugisha/stingtools.git planscape-marketing
cd planscape-marketing
git sparse-checkout init --cone
git sparse-checkout set marketing-site
git checkout main
mv marketing-site/* marketing-site/.* . 2>/dev/null
rmdir marketing-site
git remote remove origin

# Create new GitHub repo at github.com/beckykyomugisha/planscape-marketing (public or private — Pages works with both)
git remote add origin git@github.com:beckykyomugisha/planscape-marketing.git
git add -A
git commit -m "Initial commit — Planscape marketing site"
git push -u origin main
```

---

## Stage 4 — Connect Cloudflare Pages to GitHub (5 min)

1. In the Cloudflare dashboard, go to **Workers & Pages → Create application → Pages → Connect to Git**.
2. Authorize Cloudflare to access your GitHub.
3. Pick the repo (`stingtools` for monorepo, or `planscape-marketing` for split).
4. Configure the build:

   | Field | Value |
   |---|---|
   | Project name | `planscape-marketing` |
   | Production branch | `main` |
   | Framework preset | **None** |
   | Build command | (leave empty) |
   | Build output directory | `marketing-site` (monorepo) or `/` (split repo) |
   | Root directory | `/` |

5. Click **Save and Deploy**. First deploy takes ~30 seconds.
6. Cloudflare gives you a preview URL like `https://planscape-marketing.pages.dev`. Open it — your site is live (on the temporary URL).

From now on, every `git push` to `main` triggers an automatic deploy. PRs get preview deploys with their own URLs.

---

## Stage 5 — Point the domain at Cloudflare Pages (3 min)

1. In Cloudflare dashboard, open your Pages project → **Custom domains** tab.
2. Click **Set up a custom domain**.
3. Type your apex domain: `planscape.co`.
4. Cloudflare auto-creates the right DNS record (a CNAME or AAAA flattened to the Pages worker).
5. Repeat for `www.planscape.co` — Cloudflare creates a redirect to the apex.
6. Wait 1–2 minutes for the certificate to issue. You'll see "Active" with a green checkmark.

Now <https://planscape.co> serves your marketing site.

---

## Stage 6 — Replace placeholder tokens (5 min)

Before sharing the URL publicly, swap out the placeholders:

| Token | File(s) | What to set |
|---|---|---|
| `PLANSCAPE_MAPBOX_TOKEN` | `index.html` | Free public token from <https://account.mapbox.com> — no card required |
| `__CRISP_WEBSITE_ID__` | `index.html` | Crisp Chat website ID from <https://app.crisp.chat> (optional; widget silently no-ops without it) |
| `DEMO_VIDEO_ID` | `index.html` | YouTube video ID for your 90-second demo |
| `+256700000000` | every page | Your real WhatsApp Business number |
| `https://cal.com/planscape/demo` | `contact.html` | Your Cal.com / Calendly booking link |
| `https://api.planscape.co/marketing/...` | `index.html`, `contact.html` | Your API base URL (or change endpoints to whatever's live) |

Quickest way:

```bash
cd marketing-site

# Mapbox token
sed -i 's/PLANSCAPE_MAPBOX_TOKEN/pk.eyJ1Ijoi...your-real-token/' index.html

# WhatsApp number
grep -rl "+256700000000" . | xargs sed -i 's/+256700000000/+256777YOUR_REAL/g'
grep -rl "256700000000" . | xargs sed -i 's/256700000000/256777YOUR_REAL/g'
```

Commit and push — Pages auto-redeploys.

---

## Stage 7 — Connect email (10 min)

Your domain needs email for `hello@`, `support@`, `security@`, etc.

### Cheapest: Cloudflare Email Routing (free)

Forward all incoming mail to your existing Gmail / Outlook inbox.

1. Cloudflare dashboard → your domain → **Email → Email Routing**.
2. Enable it. Cloudflare auto-creates the MX records.
3. Add destination email (your existing inbox). Verify via the click-link they email you.
4. Add custom routes:
   - `hello@planscape.co` → your Gmail
   - `support@planscape.co` → your Gmail
   - `security@planscape.co` → your Gmail
   - `*@planscape.co` (catch-all) → your Gmail

For sending email FROM the domain (replying, transactional), see Stage 8.

### Better: Google Workspace (~$6/user/month)

Real email accounts at the domain. Full Gmail/Drive/Calendar.

1. Sign up at <https://workspace.google.com/business>.
2. Add the domain during onboarding — Google walks you through DNS setup.
3. Add the DNS records they specify into Cloudflare.

Skip this for now if you're bootstrapping — Email Routing is fine for the first months.

---

## Stage 8 — Set up transactional email (15 min, free tier)

For the contact form, demo-request form, and trial expiry notifications.

### Use Brevo (recommended)

Free up to 300 emails/day. African-friendly support.

1. Sign up at <https://www.brevo.com>.
2. Verify your domain (add the SPF + DKIM records to Cloudflare DNS — Brevo provides them).
3. Generate an SMTP key for your API server.
4. Set the SMTP env vars on your API host (Render.com → Environment).

You already have the contact form posting to `https://api.planscape.co/marketing/contact` — wire that endpoint to send via Brevo SMTP.

---

## Stage 9 — Add analytics (5 min, free)

### Cloudflare Web Analytics (recommended)

Privacy-first, no cookies, GDPR-friendly. Built into Cloudflare. Free.

1. Cloudflare dashboard → **Analytics → Web Analytics**.
2. Click **Add a site** → enter `planscape.co`.
3. Cloudflare gives you a snippet. Paste into `marketing-site/index.html` just before `</body>`.
4. Copy the same snippet into every other page (or put it in a shared script — but the snippet is so small that inlining works fine).

### Alternative: Plausible Analytics ($9/mo)

If you want named visitor counts, page paths, and a nicer UI. Self-host for free if you have a VPS.

### Alternative: Google Analytics 4 (free)

Industry standard but cookie-based — you'll need a cookie banner.

---

## Stage 10 — Submit to search engines (10 min)

1. **Google Search Console** — <https://search.google.com/search-console>
   - Add the property `https://planscape.co`.
   - Verify via DNS TXT record (Cloudflare DNS, takes 2 minutes).
   - Submit your sitemap: `https://planscape.co/sitemap.xml`.

2. **Bing Webmaster Tools** — <https://www.bing.com/webmasters>
   - Add the site.
   - You can auto-import settings from Google Search Console.

3. **DuckDuckGo** — auto-crawls based on Bing data, no submission needed.

Don't bother with directory submissions (Yandex, Baidu, etc.) unless you're targeting those markets.

---

## Maintenance

### Updating the site

Edit any file → commit → push. Cloudflare Pages auto-redeploys within 60 seconds.

### Rolling back a bad deploy

Cloudflare dashboard → Pages project → Deployments tab → find a previous deployment → click **Rollback to this deployment**. Takes 5 seconds.

### Adding a new guide / tutorial / blog post

1. Copy `assets/template-guide.html` (or one of the existing posts) as the starting point.
2. Edit content.
3. Add an entry to the relevant hub page (`/guides/`, `/tutorials/`, `/blog/`).
4. Add to `sitemap.xml`.
5. For blog posts: also add an item to `blog/rss.xml`.
6. Commit and push.

### Monitoring

- Cloudflare Pages dashboard shows deploy logs, bandwidth, and edge-cache hit rate.
- Cloudflare Analytics shows visitor counts, top pages, top countries, top referrers.
- Brevo dashboard shows email open/click rates if you wire transactional emails through it.

---

## Budget summary

| Item | Cost | Frequency |
|---|---|---|
| Domain (.com or .app via Cloudflare Registrar) | ~$10 | per year |
| Cloudflare Pages hosting (unlimited bandwidth) | $0 | — |
| Cloudflare DNS | $0 | — |
| SSL certificate (Let's Encrypt via Cloudflare) | $0 | — |
| Cloudflare Web Analytics | $0 | — |
| Cloudflare Email Routing (forwarding) | $0 | — |
| Brevo SMTP (up to 300 emails/day) | $0 | — |
| Google Workspace (optional, real email accounts) | $6/user | per month |
| Plausible Analytics (optional, nicer UI) | $9 | per month |

**Minimum viable: ~$10/year.** Everything else is optional.

---

## Troubleshooting

### "Deploy succeeded but the site shows old content"

Cloudflare's edge cache is aggressive. Either wait 5 minutes (HTML has a 5-min cache per `_headers`) or purge cache manually: Cloudflare dashboard → Caching → Purge Cache → Purge Everything.

### "DNS doesn't resolve yet"

Propagation can take up to 24 hours but usually finishes in 5–15 minutes. Check progress at <https://dnschecker.org>.

### "Email Routing isn't delivering"

Check Cloudflare's MX records are still in your DNS (sometimes a manual record gets in the way). Email Routing → Routes → check the destination is verified.

### "Page works but Mapbox map is blank"

You haven't replaced `PLANSCAPE_MAPBOX_TOKEN` in `index.html`. The site detects this and shows a fallback "awaits Mapbox token" message in place of the map — replace the token with a real one from <https://account.mapbox.com>.

### "Forms don't submit"

The contact and demo forms post to `https://api.planscape.co/marketing/*` — these endpoints need to exist on your API. Until the API is deployed, the forms will show the error path and ask users to email instead.

---

## Next steps after the site is live

1. Test every form by submitting yourself.
2. Test the WhatsApp link — make sure your business number receives the message.
3. Share the URL with a friend on a slow phone — confirm it loads in &lt;2s.
4. Run a Lighthouse audit (`https://pagespeed.web.dev/?url=https://planscape.co`) — aim for &gt;90 on Performance, Accessibility, Best Practices, SEO.
5. Submit the sitemap to Google Search Console.
6. Start writing the next blog post.

---

Questions: <hello@planscape.co>.
