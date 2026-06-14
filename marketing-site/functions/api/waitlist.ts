// Cloudflare Pages Function — POST /api/waitlist
// Writes a waitlist entry to the bound D1 database (binding name: WAITLIST_DB).
// Set up: `wrangler d1 create planscape-waitlist` then add the binding in
//   Cloudflare dashboard → Pages → planscape-marketing → Settings → Functions → D1
// with variable name WAITLIST_DB. Apply the schema below once via
//   `wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql`.

interface Env {
  WAITLIST_DB: D1Database;
  WAITLIST_WEBHOOK_URL?: string; // optional Slack / Discord / Resend webhook
}

interface WaitlistBody {
  product: string;
  firstName: string;
  lastName: string;
  email: string;
  firm: string;
  country: string;
  teamSize: string;
  role: string;
  notes: string;
  submittedAt: string;
  referrer: string;
  utm: string;
}

const ALLOWED_PRODUCTS = new Set(["sting-tools", "planscape", "both"]);
const ALLOWED_SIZES = new Set(["1-3", "4-10", "11-25", "26-50", "51-100", "100+"]);

function bad(msg: string, status = 400): Response {
  return new Response(JSON.stringify({ error: msg }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function isEmail(s: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s);
}

function clip(s: unknown, max: number): string {
  if (typeof s !== "string") return "";
  return s.slice(0, max).trim();
}

export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
  let body: WaitlistBody;
  try {
    body = await request.json();
  } catch {
    return bad("Invalid JSON");
  }

  // Validate
  const email = clip(body.email, 200).toLowerCase();
  const firstName = clip(body.firstName, 80);
  const lastName = clip(body.lastName, 80);
  const firm = clip(body.firm, 160);
  const product = clip(body.product, 32);
  const teamSize = clip(body.teamSize, 16);
  const country = clip(body.country, 8);
  const role = clip(body.role, 32);
  const notes = clip(body.notes, 2000);
  const referrer = clip(body.referrer, 500);
  const utm = clip(body.utm, 500);

  if (!isEmail(email)) return bad("Please provide a valid email address.");
  if (!firstName || !lastName) return bad("Please provide your name.");
  if (!firm) return bad("Please provide your firm name.");
  if (!ALLOWED_PRODUCTS.has(product)) return bad("Please choose a product.");
  if (!ALLOWED_SIZES.has(teamSize)) return bad("Please choose a team size.");

  const submittedAt = new Date().toISOString();
  const ip = request.headers.get("CF-Connecting-IP") || "";
  const ua = request.headers.get("User-Agent") || "";

  // Insert (ON CONFLICT(email) update — same firm can re-submit to refresh interest)
  try {
    await env.WAITLIST_DB.prepare(
      `INSERT INTO waitlist (
         email, first_name, last_name, firm, product, team_size, country, role, notes,
         referrer, utm, ip, user_agent, submitted_at
       ) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)
       ON CONFLICT(email) DO UPDATE SET
         first_name = excluded.first_name,
         last_name  = excluded.last_name,
         firm       = excluded.firm,
         product    = excluded.product,
         team_size  = excluded.team_size,
         country    = excluded.country,
         role       = excluded.role,
         notes      = excluded.notes,
         updated_at = excluded.submitted_at`
    )
      .bind(
        email, firstName, lastName, firm, product, teamSize, country, role, notes,
        referrer, utm, ip, ua, submittedAt
      )
      .run();
  } catch (e) {
    console.error("D1 insert failed", e);
    return bad("Could not save your entry. Please try again.", 500);
  }

  // Optional: ping a webhook so Becky gets a live alert (Slack/Discord/Resend)
  if (env.WAITLIST_WEBHOOK_URL) {
    const text =
      `🎉 New waitlist signup\n` +
      `*${firstName} ${lastName}* (${email})\n` +
      `Firm: ${firm}\n` +
      `Product: ${product} · Size: ${teamSize} · Country: ${country} · Role: ${role}\n` +
      (notes ? `Notes: ${notes}\n` : "") +
      `When: ${submittedAt}`;
    // Fire-and-forget so the user gets a fast response even if the webhook is slow
    try {
      await fetch(env.WAITLIST_WEBHOOK_URL, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text }),
      });
    } catch (e) {
      console.warn("Webhook failed (non-fatal)", e);
    }
  }

  return new Response(JSON.stringify({ ok: true }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
};

// Anything else (GET, etc) — return method not allowed
export const onRequest: PagesFunction<Env> = async ({ request }) => {
  if (request.method === "POST") {
    // shouldn't reach here, onRequestPost handles POST
    return bad("Use POST", 405);
  }
  return bad("Method not allowed", 405);
};
