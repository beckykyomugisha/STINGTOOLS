// Planscape billing lifecycle cron Worker. Self-contained (no imports from the
// Pages project) but binds the SAME D1 database, so every query targets the same
// schema the marketing-site Functions own. Dispatch is by cron expression:
//
//   "0 * * * *"  hourly  → expireTrials
//   "0 9 * * *"  daily   → trialReminders + capReminders + dunning
//   "0 6 * * *"  daily   → nightlyDigest
//   "0 3 * * *"  daily   → cleanup
//
// A manual `GET /run?job=<name>` (guarded by ADMIN_API_KEY) triggers any single
// job for testing without waiting for the schedule.

export interface Env {
  WAITLIST_DB: D1Database;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
  APP_ORIGIN?: string;
  SIGNUP_DIGEST_WEBHOOK?: string;
  ADMIN_API_KEY?: string;
}

const DAY_MS = 86_400_000;

// ---- small helpers --------------------------------------------------------

function appOrigin(env: Env): string {
  return env.APP_ORIGIN || "https://planscape.build";
}

interface OwnerRow {
  email: string;
  first_name: string;
}

async function ownerOf(db: D1Database, tenantId: string): Promise<OwnerRow | null> {
  return db
    .prepare(
      `SELECT email, first_name FROM users
        WHERE tenant_id = ? AND role = 'owner' AND deleted_at IS NULL
        ORDER BY created_at LIMIT 1`
    )
    .bind(tenantId)
    .first<OwnerRow>();
}

async function auditLog(
  db: D1Database,
  tenantId: string,
  action: string,
  metadata: unknown
): Promise<void> {
  await db
    .prepare(
      `INSERT INTO audit_log
         (tenant_id, actor_user_id, action, target, metadata, ip, user_agent, created_at)
       VALUES (?,?,?,?,?,?,?,?)`
    )
    .bind(
      tenantId,
      null,
      action,
      null,
      metadata != null ? JSON.stringify(metadata) : null,
      null,
      "cron",
      new Date().toISOString()
    )
    .run();
}

function emailShell(title: string, bodyHtml: string): string {
  return `<!doctype html><html><body style="margin:0;background:#f4f5f7;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1a1a2e;">
  <div style="max-width:480px;margin:0 auto;padding:32px 24px;">
    <div style="background:#fff;border-radius:12px;padding:32px;">
      <h1 style="margin:0 0 16px;font-size:20px;">${title}</h1>
      ${bodyHtml}
      <hr style="border:none;border-top:1px solid #eceef1;margin:28px 0 16px;">
      <p style="margin:0;font-size:12px;color:#8a8f99;">Planscape · ISO 19650 BIM tooling.</p>
    </div>
  </div>
</body></html>`;
}

async function sendEmail(env: Env, to: string, subject: string, html: string): Promise<void> {
  if (!env.RESEND_API_KEY) {
    console.error(`Email skipped (RESEND_API_KEY unset): "${subject}" → ${to}`);
    return;
  }
  try {
    const res = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.RESEND_API_KEY}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from: env.EMAIL_FROM || "Planscape <noreply@planscape.build>",
        to: [to],
        subject,
        html,
      }),
    });
    if (!res.ok) console.error(`Resend send failed (${res.status}) for "${subject}"`);
  } catch (e) {
    console.error("Resend request threw", e);
  }
}

function ctaButton(href: string, label: string): string {
  return `<a href="${href}" style="display:inline-block;background:#E8912D;color:#fff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;font-size:15px;">${label}</a>`;
}

// ---- jobs -----------------------------------------------------------------

interface TenantLite {
  id: string;
  name: string;
  trial_ends_at: string;
  updated_at: string | null;
  plan_tier: string | null;
}

// Hourly: flip elapsed trials to read_only and notify the owner.
async function expireTrials(env: Env, now: number): Promise<number> {
  const db = env.WAITLIST_DB;
  const nowIso = new Date(now).toISOString();
  const rows = await db
    .prepare(
      `SELECT id, name FROM tenants
        WHERE subscription_status = 'trial' AND trial_ends_at < ?`
    )
    .bind(nowIso)
    .all<{ id: string; name: string }>();

  let n = 0;
  for (const t of rows.results ?? []) {
    const res = await db
      .prepare(
        `UPDATE tenants SET subscription_status = 'read_only', updated_at = ?
          WHERE id = ? AND subscription_status = 'trial'`
      )
      .bind(nowIso, t.id)
      .run();
    if (!res.meta?.changes) continue; // someone else flipped it first
    n++;
    await auditLog(db, t.id, "trial.expired", { via: "cron" });
    const owner = await ownerOf(db, t.id);
    if (owner) {
      await sendEmail(
        env,
        owner.email,
        "Your Planscape trial has ended",
        emailShell(
          "Your trial has ended",
          `<p style="font-size:15px;line-height:1.5;">Hi ${owner.first_name}, your 14-day trial for <strong>${t.name}</strong> has ended, so the account is now read-only. Choose a plan to restore full access — your data is safe.</p>
           <p style="margin:24px 0 0;">${ctaButton(`${appOrigin(env)}/pricing.html`, "Choose a plan")}</p>`
        )
      );
    }
  }
  return n;
}

// Daily 09:00: T-7 / T-3 / T-1 trial-ending reminders.
async function trialReminders(env: Env, now: number): Promise<number> {
  const db = env.WAITLIST_DB;
  let n = 0;
  for (const days of [7, 3, 1]) {
    const lower = new Date(now + days * DAY_MS).toISOString();
    const upper = new Date(now + days * DAY_MS + DAY_MS).toISOString();
    const rows = await db
      .prepare(
        `SELECT id, name FROM tenants
          WHERE subscription_status = 'trial' AND trial_ends_at >= ? AND trial_ends_at < ?`
      )
      .bind(lower, upper)
      .all<{ id: string; name: string }>();
    for (const t of rows.results ?? []) {
      const owner = await ownerOf(db, t.id);
      if (!owner) continue;
      const noun = days === 1 ? "tomorrow" : `in ${days} days`;
      await sendEmail(
        env,
        owner.email,
        `Your Planscape trial ends ${noun}`,
        emailShell(
          `Trial ends ${noun}`,
          `<p style="font-size:15px;line-height:1.5;">Hi ${owner.first_name}, the trial for <strong>${t.name}</strong> ends ${noun}. Pick a plan now to keep full access with no interruption.</p>
           <p style="margin:24px 0 0;">${ctaButton(`${appOrigin(env)}/pricing.html`, "See plans")}</p>`
        )
      );
      n++;
    }
  }
  return n;
}

// Daily 09:00: remind tenants that have been over their seat cap.
async function capReminders(env: Env): Promise<number> {
  const db = env.WAITLIST_DB;
  const rows = await db
    .prepare(
      `SELECT id, name FROM tenants
        WHERE cap_exceeded_since IS NOT NULL
          AND subscription_status NOT IN ('read_only', 'cancelled')`
    )
    .all<{ id: string; name: string }>();
  let n = 0;
  for (const t of rows.results ?? []) {
    const owner = await ownerOf(db, t.id);
    if (!owner) continue;
    await sendEmail(
      env,
      owner.email,
      "You're over your Planscape plan limit",
      emailShell(
        "Over your plan limit",
        `<p style="font-size:15px;line-height:1.5;">Hi ${owner.first_name}, <strong>${t.name}</strong> has more members than your current plan allows. Upgrade to avoid the account being limited once the grace period ends.</p>
         <p style="margin:24px 0 0;">${ctaButton(`${appOrigin(env)}/pricing.html`, "Upgrade plan")}</p>`
      )
    );
    n++;
  }
  return n;
}

// Daily 09:00: dunning for past_due tenants. After 7 days past_due → read_only.
async function dunning(env: Env, now: number): Promise<number> {
  const db = env.WAITLIST_DB;
  const cutoffIso = new Date(now - 7 * DAY_MS).toISOString();
  const rows = await db
    .prepare(
      `SELECT id, name, trial_ends_at, updated_at, plan_tier FROM tenants
        WHERE subscription_status = 'past_due'`
    )
    .all<TenantLite>();
  let n = 0;
  for (const t of rows.results ?? []) {
    const owner = await ownerOf(db, t.id);
    const overdueLong = t.updated_at != null && t.updated_at < cutoffIso;
    if (overdueLong) {
      await db
        .prepare(
          `UPDATE tenants SET subscription_status = 'read_only', updated_at = ?
            WHERE id = ? AND subscription_status = 'past_due'`
        )
        .bind(new Date(now).toISOString(), t.id)
        .run();
      await auditLog(db, t.id, "subscription.read_only", { via: "dunning" });
      if (owner) {
        await sendEmail(
          env,
          owner.email,
          "Your Planscape account is now limited",
          emailShell(
            "Account limited",
            `<p style="font-size:15px;line-height:1.5;">Hi ${owner.first_name}, we couldn't collect payment for <strong>${t.name}</strong> after several attempts, so the account is now read-only. Update your payment details to restore access.</p>
             <p style="margin:24px 0 0;">${ctaButton(`${appOrigin(env)}/pricing.html`, "Update payment")}</p>`
          )
        );
      }
    } else if (owner) {
      await sendEmail(
        env,
        owner.email,
        "Payment failed — action needed",
        emailShell(
          "We couldn't process your payment",
          `<p style="font-size:15px;line-height:1.5;">Hi ${owner.first_name}, the latest payment for <strong>${t.name}</strong> didn't go through. Please update your payment details so your subscription stays active.</p>
           <p style="margin:24px 0 0;">${ctaButton(`${appOrigin(env)}/pricing.html`, "Fix payment")}</p>`
        )
      );
    }
    n++;
  }
  return n;
}

// Daily 06:00: post a 24h signup/revenue digest to a Slack/Discord webhook.
async function nightlyDigest(env: Env, now: number): Promise<number> {
  if (!env.SIGNUP_DIGEST_WEBHOOK) return 0;
  const db = env.WAITLIST_DB;
  const since = new Date(now - DAY_MS).toISOString();

  const signups = await db
    .prepare(`SELECT COUNT(*) AS n FROM tenants WHERE created_at >= ?`)
    .bind(since)
    .first<{ n: number }>();
  const newSubs = await db
    .prepare(`SELECT COUNT(*) AS n FROM subscriptions WHERE created_at >= ? AND status = 'active'`)
    .bind(since)
    .first<{ n: number }>();
  const paidInvoices = await db
    .prepare(`SELECT COUNT(*) AS n FROM invoices WHERE status = 'paid' AND created_at >= ?`)
    .bind(since)
    .first<{ n: number }>();
  const waitlist = await db
    .prepare(`SELECT COUNT(*) AS n FROM waitlist WHERE submitted_at >= ?`)
    .bind(since)
    .first<{ n: number }>();

  const text =
    `📊 *Planscape — last 24h*\n` +
    `• New signups: ${signups?.n ?? 0}\n` +
    `• New active subscriptions: ${newSubs?.n ?? 0}\n` +
    `• Invoices paid: ${paidInvoices?.n ?? 0}\n` +
    `• Waitlist submissions: ${waitlist?.n ?? 0}`;

  try {
    await fetch(env.SIGNUP_DIGEST_WEBHOOK, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text, content: text }), // Slack uses `text`, Discord `content`
    });
  } catch (e) {
    console.error("Digest webhook failed", e);
  }
  return 1;
}

// Daily 03:00: prune expired sessions + idempotency keys + old webhook logs.
async function cleanup(env: Env, now: number): Promise<Record<string, number>> {
  const db = env.WAITLIST_DB;
  const nowIso = new Date(now).toISOString();
  const revokedCutoff = new Date(now - 30 * DAY_MS).toISOString();
  const webhookCutoff = new Date(now - 90 * DAY_MS).toISOString();

  const sessions = await db
    .prepare(
      `DELETE FROM sessions
        WHERE expires_at < ? OR (revoked_at IS NOT NULL AND revoked_at < ?)`
    )
    .bind(nowIso, revokedCutoff)
    .run();
  const idem = await db
    .prepare(`DELETE FROM idempotency_keys WHERE expires_at < ?`)
    .bind(nowIso)
    .run();
  const webhooks = await db
    .prepare(
      `DELETE FROM webhooks_log
        WHERE created_at < ? AND status IN ('processed', 'ignored', 'bad_signature')`
    )
    .bind(webhookCutoff)
    .run();

  return {
    sessions: sessions.meta?.changes ?? 0,
    idempotency_keys: idem.meta?.changes ?? 0,
    webhooks_log: webhooks.meta?.changes ?? 0,
  };
}

// ---- dispatch -------------------------------------------------------------

async function runDaily09(env: Env, now: number) {
  const reminders = await trialReminders(env, now);
  const caps = await capReminders(env);
  const dun = await dunning(env, now);
  return { reminders, caps, dunning: dun };
}

async function runJob(job: string, env: Env, now: number): Promise<unknown> {
  switch (job) {
    case "expireTrials":
      return { expired: await expireTrials(env, now) };
    case "daily09":
      return runDaily09(env, now);
    case "trialReminders":
      return { reminders: await trialReminders(env, now) };
    case "capReminders":
      return { caps: await capReminders(env) };
    case "dunning":
      return { dunning: await dunning(env, now) };
    case "nightlyDigest":
      return { digest: await nightlyDigest(env, now) };
    case "cleanup":
      return cleanup(env, now);
    default:
      return { error: `unknown job '${job}'` };
  }
}

function jobForCron(cron: string): string {
  switch (cron) {
    case "0 * * * *":
      return "expireTrials";
    case "0 9 * * *":
      return "daily09";
    case "0 6 * * *":
      return "nightlyDigest";
    case "0 3 * * *":
      return "cleanup";
    default:
      return "expireTrials";
  }
}

export default {
  async scheduled(event: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    const job = jobForCron(event.cron);
    const now = event.scheduledTime || Date.now();
    ctx.waitUntil(
      runJob(job, env, now).then(
        (r) => console.log(`cron ${event.cron} → ${job}: ${JSON.stringify(r)}`),
        (e) => console.error(`cron ${event.cron} → ${job} failed`, e)
      )
    );
  },

  // Manual trigger for testing: GET /run?job=cleanup  (X-Admin-Key: ADMIN_API_KEY)
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname !== "/run") {
      return new Response("planscape-cron — POST/GET /run?job=<name> with X-Admin-Key", {
        status: 200,
      });
    }
    if (!env.ADMIN_API_KEY || request.headers.get("X-Admin-Key") !== env.ADMIN_API_KEY) {
      return new Response(JSON.stringify({ error: "forbidden" }), {
        status: 403,
        headers: { "Content-Type": "application/json" },
      });
    }
    const job = url.searchParams.get("job") || "";
    const result = await runJob(job, env, Date.now());
    return new Response(JSON.stringify({ job, result }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  },
};
