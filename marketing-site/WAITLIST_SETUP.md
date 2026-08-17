# Waitlist setup (one-time, ~10 min)

The signup form posts to `POST /api/waitlist` (a Cloudflare Pages Function),
which writes to a D1 database. You need to create the database once, bind it
to the Pages project, and apply the schema. Then every submission gets logged
automatically — no server, no cost.

## 1. Create the D1 database

```bash
cd marketing-site
wrangler d1 create planscape-waitlist
```

Wrangler prints something like:

```
✅ Successfully created DB 'planscape-waitlist'
[[d1_databases]]
binding = "WAITLIST_DB"
database_name = "planscape-waitlist"
database_id = "abc12345-6789-..."
```

**Copy the `database_id`** — you'll need it.

## 2. Bind D1 in `wrangler.toml`

The binding is already defined in `marketing-site/wrangler.toml`:

```toml
[[d1_databases]]
binding       = "WAITLIST_DB"
database_name = "planscape-waitlist"
database_id   = "b7029da2-6019-4ceb-bd87-22870a313db7"
```

If you ever re-create the D1 database, update the `database_id` here (and in the `[[env.production.d1_databases]]` block at the bottom) before the next deploy.

> **`env.preview` deliberately points at a DIFFERENT database** —
> `planscape-waitlist-preview` (`75855f69-…`), created 2026-08-17. Do not "fix"
> the mismatch by pointing it back at `planscape-waitlist`.
>
> All three blocks used to share one id, so a preview deployment read and wrote
> live licence data: issuing real licences against a real tenant's cap, revoking
> them, stamping `last_seen_at` — none of it distinguishable afterwards from
> legitimate activity, because it landed in the same rows the production path
> writes (#652). That mattered more once #621 made `licenses` the seat-entitlement
> source of truth.
>
> **When you change `schema.sql`, apply it to both** or preview breaks in ways
> production does not:
> ```
> npx wrangler d1 execute planscape-waitlist         --remote --file=./functions/api/schema.sql
> npx wrangler d1 execute planscape-waitlist-preview --remote --file=./functions/api/schema.sql
> ```
> Careful with the first one on an existing database: `schema.sql` contains bare
> `ALTER TABLE` statements that fail on re-run. Apply only new DDL to production.

> Note: the Cloudflare dashboard's **+ Add binding** button is greyed out for projects using `wrangler.toml` — that's intentional. The toml is the source of truth; the dashboard shows the binding as read-only after the next deploy.

## 3. Apply the schema (creates the `waitlist` table)

```bash
wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
```

Re-running this command is safe — every statement is `IF NOT EXISTS`.

## 4. Optional — get notified on each signup

Set a `WAITLIST_WEBHOOK_URL` environment variable on the Pages project so each new entry pings a Slack / Discord / Resend webhook.

Dashboard → **Pages → planscape-marketing → Settings → Environment variables → Production → Add variable**:

- **Variable name:** `WAITLIST_WEBHOOK_URL`
- **Value:** your Slack/Discord incoming-webhook URL (or a Resend email-relay URL)
- **Encrypt:** ✓ (it's effectively a secret)

If you don't set it, signups still save to D1 — you just have to check the table manually or build an admin page later.

## 5. Deploy

```bash
cd marketing-site
wrangler pages deploy . --project-name=planscape-marketing --branch=main --commit-dirty=true
```

## 6. Test it

Visit https://planscape.build/signup and submit a test entry.

Then query D1 to confirm it landed:

```bash
wrangler d1 execute planscape-waitlist --remote --command="SELECT id, email, firm, product, team_size, submitted_at FROM waitlist ORDER BY id DESC LIMIT 10"
```

## Viewing all waitlist entries later

```bash
# Quick count + breakdown
wrangler d1 execute planscape-waitlist --remote --command="SELECT product, COUNT(*) FROM waitlist GROUP BY product"

# Full export to CSV
wrangler d1 execute planscape-waitlist --remote --json --command="SELECT * FROM waitlist ORDER BY submitted_at DESC" > waitlist_export.json
```

## Moving to the real API (Option B) later

When the real API is live at `api.planscape.build`:

1. Migrate D1 → Postgres (one-time `wrangler d1 export` → `psql` import).
2. Change `_redirects` so `/signup`, `/login`, `/app/*` point at `api.planscape.build/auth/*` (the lines that used to be there, before the waitlist was wired in).
3. Either delete `signup.html` or repurpose it as the "still waitlisted? thanks for waiting" page.

The D1 table schema is intentionally Postgres-compatible (no SQLite-only types) so the migration is `pg_dump`-style flat.
