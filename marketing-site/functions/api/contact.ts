// Cloudflare Pages Function — POST /api/contact
//
// The contact form used to post to https://api.planscape.build/marketing/contact.
// That hostname is NXDOMAIN (#705), so every submission failed and the form told
// the visitor "Something went wrong. Email us at hello@planscape.build instead."
// Same-origin now: no DNS to attach, no CORS, and nothing in the CSP to change
// (connect-src already allows 'self').
//
// PUBLIC AND UNAUTHENTICATED, and it sends email — so it is an abuse surface in
// a way the auth endpoints are not. Hence the per-IP flood check below.
//
// THE ROW IS THE DELIVERABLE, NOT THE EMAIL. The insert happens first and the
// submitter is told it worked as long as it lands. A Resend outage must not lose
// an enquiry, and must not tell a prospective customer the site is broken —
// which is exactly the failure this endpoint exists to end.

interface Env {
  WAITLIST_DB: D1Database;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
  CONTACT_TO?: string;
}

import { sendContactNotification } from "./auth/_lib/email";
import type { Env as AuthEnv } from "./auth/_lib/types";

// The address the form itself already offers as the fallback, so a reply comes
// from where the visitor was told to write.
const DEFAULT_CONTACT_TO = "hello@planscape.build";

// Topics are a fixed <select> in contact.html. Anything else is a forged post.
const ALLOWED_TOPICS = new Set([
  "demo", "trial", "pricing", "enterprise", "migration",
  "education", "partner", "support", "press", "other",
]);

// Enough for a genuine burst (someone resubmitting after a typo, a shared
// office NAT) while stopping a script from using us as a mail relay.
const MAX_PER_IP_PER_HOUR = 5;

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function bad(msg: string, status = 400): Response {
  return json({ error: msg }, status);
}

function clip(s: unknown, max: number): string {
  if (typeof s !== "string") return "";
  return s.slice(0, max).trim();
}

function isEmail(s: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s);
}

export const onRequestPost: PagesFunction<Env> = async ({ request, env }) => {
  let body: Record<string, unknown>;
  try {
    body = await request.json();
  } catch {
    return bad("Invalid JSON");
  }

  const name = clip(body.name, 120);
  const email = clip(body.email, 200).toLowerCase();
  const firm = clip(body.firm, 160);
  const topic = clip(body.topic, 32);
  const message = clip(body.message, 5000);

  if (!name) return bad("Please tell us your name.");
  if (!isEmail(email)) return bad("That email address doesn't look right.");
  if (!ALLOWED_TOPICS.has(topic)) return bad("Please choose what your message is about.");
  if (!message) return bad("Please write a message.");

  const ip = request.headers.get("CF-Connecting-IP") || "";
  const userAgent = clip(request.headers.get("User-Agent"), 300);
  const referrer = clip(request.headers.get("Referer"), 300);
  const now = new Date();
  const nowIso = now.toISOString();

  // Flood check. Skipped when the IP header is absent (local dev) rather than
  // treated as one shared bucket, which would rate-limit every local request
  // against every other.
  if (ip) {
    const since = new Date(now.getTime() - 3600_000).toISOString();
    const recent = await env.WAITLIST_DB
      .prepare(`SELECT COUNT(*) AS n FROM contacts WHERE ip = ? AND submitted_at > ?`)
      .bind(ip, since)
      .first<{ n: number }>();
    if ((recent?.n ?? 0) >= MAX_PER_IP_PER_HOUR) {
      return bad(
        `That's several messages in a short time. Email ${env.CONTACT_TO || DEFAULT_CONTACT_TO} directly and we'll pick it up.`,
        429
      );
    }
  }

  // Insert BEFORE emailing: the record is what must not be lost.
  let id: number | null = null;
  try {
    const res = await env.WAITLIST_DB
      .prepare(
        `INSERT INTO contacts (name, email, firm, topic, message, ip, user_agent, referrer, submitted_at)
         VALUES (?,?,?,?,?,?,?,?,?)`
      )
      .bind(name, email, firm, topic, message, ip, userAgent, referrer, nowIso)
      .run();
    id = (res.meta?.last_row_id as number) ?? null;
  } catch (e) {
    // A failed insert is the one case worth refusing on — otherwise we would
    // report success for a message that exists nowhere.
    console.error("contact insert failed", e);
    return bad("We couldn't record that message. Please try again shortly.", 500);
  }

  // Best-effort notification. Never gates the response.
  const to = env.CONTACT_TO || DEFAULT_CONTACT_TO;
  const sent = await sendContactNotification(env as unknown as AuthEnv, to, {
    name, email, firm, topic, message,
  });

  if (sent && id != null) {
    try {
      await env.WAITLIST_DB
        .prepare(`UPDATE contacts SET notified_at = ? WHERE id = ?`)
        .bind(new Date().toISOString(), id)
        .run();
    } catch (e) {
      // The message is safe and the email went out; only the bookkeeping failed.
      console.error("contact notified_at update failed", e);
    }
  } else if (!sent) {
    // notified_at stays NULL, which is the queryable record of this.
    console.error(`Contact ${id}: stored but notification not sent — check RESEND_API_KEY`);
  }

  return json({ ok: true });
};
