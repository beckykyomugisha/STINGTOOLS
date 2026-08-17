// Transactional email via Resend. Plain inline HTML — no template engine.
// If RESEND_API_KEY is unset (e.g. local/preview), sends are skipped with a
// console.error so the auth flow still works without blowing up.

import type { Env } from "./types";

const DEFAULT_FROM = "Planscape <noreply@planscape.build>";

function appOrigin(env: Env): string {
  return env.APP_ORIGIN || "https://planscape.build";
}

// NEVER log the sender VALUE. This line used to interpolate env.EMAIL_FROM, and
// that variable had been set to a 44-character base64 string — so every failed
// send wrote a secret-shaped value into Cloudflare's Function logs (#711).
//
// Classifying it is also strictly more useful: it names the fault ("not an
// email address") instead of asking a human to eyeball 44 characters and notice
// they are not an address.
function describeFrom(env: Env): string {
  const raw = (env.EMAIL_FROM || "").trim();
  if (!raw) return "default";
  const looksLikeAddress =
    /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(raw) ||
    /^[^<>]*<[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+>$/.test(raw);
  return looksLikeAddress
    ? "EMAIL_FROM"
    : "EMAIL_FROM (NOT an email address — Resend will reject every send)";
}

// One row per attempt, so "did that email actually go out?" is a query rather
// than an appeal to logs nobody reads.
//
// This lives in send() and not at the six call sites on purpose: every sender
// gets it with no change of its own, and a sender added later cannot forget to
// opt in. That absence is exactly what let a total email outage go unnoticed —
// the contact form was the first sender to record its result, and it found the
// outage on its first submission.
//
// Never throws: a ledger failure must not break an email, and an email failure
// must not break a signup.
async function record(
  env: Env,
  to: string,
  subject: string,
  ok: boolean,
  error: string | null
): Promise<void> {
  try {
    await env.WAITLIST_DB.prepare(
      `INSERT INTO email_log (to_address, subject, ok, error, created_at)
       VALUES (?,?,?,?,?)`
    )
      .bind(to, subject.slice(0, 200), ok ? 1 : 0, error ? error.slice(0, 300) : null, new Date().toISOString())
      .run();
  } catch (e) {
    console.error("email_log write failed", e);
  }
}

// Returns whether Resend accepted the message. Every existing caller ignores
// it — an auth email failing must not fail the request. The contact form uses
// it to record notified_at; every sender is now recorded in email_log too.
async function send(
  env: Env,
  to: string,
  subject: string,
  html: string,
  replyTo?: string
): Promise<boolean> {
  if (!env.RESEND_API_KEY) {
    // Non-fatal: the surrounding flow (signup/login) must still succeed.
    console.error(`Email skipped (RESEND_API_KEY unset): "${subject}" → ${to}`);
    await record(env, to, subject, false, "RESEND_API_KEY unset");
    return false;
  }
  try {
    const res = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.RESEND_API_KEY}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from: env.EMAIL_FROM || DEFAULT_FROM,
        to: [to],
        subject,
        html,
        ...(replyTo ? { reply_to: replyTo } : {}),
      }),
    });
    if (!res.ok) {
      // Resend's own message, plus a classification of the sender — the status
      // alone is not actionable (401 = bad API key, 422 = malformed or
      // unverified sender).
      const detail = (await res.text().catch(() => "")).slice(0, 300);
      const summary = `Resend send failed (${res.status}) for "${subject}" [from=${describeFrom(env)}]: ${detail}`;
      console.error(summary);
      await record(env, to, subject, false, summary);
      return false;
    }
    await record(env, to, subject, true, null);
    return true;
  } catch (e) {
    // Never let an email failure break the request.
    console.error("Resend request threw", e);
    await record(env, to, subject, false, `threw: ${String(e).slice(0, 200)}`);
    return false;
  }
}

function shell(title: string, bodyHtml: string): string {
  return `<!doctype html><html><body style="margin:0;background:#f4f5f7;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1a1a2e;">
  <div style="max-width:480px;margin:0 auto;padding:32px 24px;">
    <div style="background:#ffffff;border-radius:12px;padding:32px;">
      <h1 style="margin:0 0 16px;font-size:20px;color:#1a1a2e;">${title}</h1>
      ${bodyHtml}
      <hr style="border:none;border-top:1px solid #eceef1;margin:28px 0 16px;">
      <p style="margin:0;font-size:12px;color:#8a8f99;">Planscape · ISO 19650 BIM tooling. If you didn't expect this email, you can safely ignore it.</p>
    </div>
  </div>
</body></html>`;
}

function button(href: string, label: string): string {
  return `<a href="${href}" style="display:inline-block;background:#2b59ff;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;font-size:15px;">${label}</a>`;
}

export async function sendVerifyEmail(
  env: Env,
  to: string,
  firstName: string,
  token: string
): Promise<void> {
  const link = `${appOrigin(env)}/verify-email?token=${encodeURIComponent(token)}`;
  const html = shell(
    "Confirm your email",
    `<p style="margin:0 0 20px;font-size:15px;line-height:1.5;">Hi ${firstName}, welcome to Planscape. Confirm your email address to activate your 14-day trial.</p>
     <p style="margin:0 0 24px;">${button(link, "Verify email")}</p>
     <p style="margin:0;font-size:13px;color:#8a8f99;line-height:1.5;">Or paste this link into your browser:<br><span style="color:#2b59ff;word-break:break-all;">${link}</span><br><br>This link expires in 24 hours.</p>`
  );
  await send(env, to, "Confirm your Planscape email", html);
}

export async function sendResetEmail(
  env: Env,
  to: string,
  firstName: string,
  token: string
): Promise<void> {
  const link = `${appOrigin(env)}/reset-password?token=${encodeURIComponent(token)}`;
  const html = shell(
    "Reset your password",
    `<p style="margin:0 0 20px;font-size:15px;line-height:1.5;">Hi ${firstName}, we received a request to reset your Planscape password. Click below to choose a new one.</p>
     <p style="margin:0 0 24px;">${button(link, "Reset password")}</p>
     <p style="margin:0;font-size:13px;color:#8a8f99;line-height:1.5;">Or paste this link into your browser:<br><span style="color:#2b59ff;word-break:break-all;">${link}</span><br><br>This link expires in 1 hour. If you didn't request a reset, ignore this email — your password stays unchanged.</p>`
  );
  await send(env, to, "Reset your Planscape password", html);
}

export async function sendInviteEmail(
  env: Env,
  to: string,
  inviterName: string,
  tenantName: string,
  role: string,
  token: string
): Promise<void> {
  const link = `${appOrigin(env)}/accept-invite?token=${encodeURIComponent(token)}`;
  const roleLabel = role.replace(/_/g, " ");
  const html = shell(
    `You're invited to ${tenantName}`,
    `<p style="margin:0 0 20px;font-size:15px;line-height:1.5;">${inviterName} has invited you to join <strong>${tenantName}</strong> on Planscape as <strong>${roleLabel}</strong>.</p>
     <p style="margin:0 0 24px;">${button(link, "Accept invitation")}</p>
     <p style="margin:0;font-size:13px;color:#8a8f99;line-height:1.5;">Or paste this link into your browser:<br><span style="color:#2b59ff;word-break:break-all;">${link}</span><br><br>This invitation expires in 7 days.</p>`
  );
  await send(env, to, `Join ${tenantName} on Planscape`, html);
}

export async function sendWelcomeEmail(
  env: Env,
  to: string,
  firstName: string
): Promise<void> {
  const html = shell(
    "You're all set",
    `<p style="margin:0 0 20px;font-size:15px;line-height:1.5;">Hi ${firstName}, your email is verified and your Planscape trial is live. You've got 14 days of full access — no card required.</p>
     <p style="margin:0;font-size:13px;color:#8a8f99;line-height:1.5;">Questions? Just reply to this email.</p>`
  );
  await send(env, to, "Welcome to Planscape", html);
}

// Notification for a public contact-form submission. Distinct from the senders
// above in three ways, all deliberate:
//
//   * It goes to US, not to the submitter, so it is not wrapped in shell() —
//     that template ends with "if you didn't expect this email, ignore it",
//     which is wrong for a message you asked to receive.
//   * reply_to is the submitter, so hitting Reply in a mail client answers the
//     person rather than noreply@.
//   * The body is escaped. Everything in it is attacker-controlled text from an
//     unauthenticated public form.
export async function sendContactNotification(
  env: Env,
  to: string,
  from: { name: string; email: string; firm: string; topic: string; message: string }
): Promise<boolean> {
  const e = (s: string) =>
    s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c] as string));

  const html = `<!doctype html><html><body style="margin:0;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1a1a2e;">
  <div style="max-width:560px;margin:0 auto;padding:24px;">
    <h1 style="margin:0 0 4px;font-size:18px;">Contact form — ${e(from.topic)}</h1>
    <p style="margin:0 0 20px;font-size:13px;color:#8a8f99;">Reply to this email to answer ${e(from.name)} directly.</p>
    <table style="width:100%;border-collapse:collapse;font-size:14px;">
      <tr><td style="padding:6px 0;color:#8a8f99;width:80px;">Name</td><td style="padding:6px 0;">${e(from.name)}</td></tr>
      <tr><td style="padding:6px 0;color:#8a8f99;">Email</td><td style="padding:6px 0;">${e(from.email)}</td></tr>
      <tr><td style="padding:6px 0;color:#8a8f99;">Firm</td><td style="padding:6px 0;">${e(from.firm) || "—"}</td></tr>
      <tr><td style="padding:6px 0;color:#8a8f99;">Topic</td><td style="padding:6px 0;">${e(from.topic)}</td></tr>
    </table>
    <div style="margin-top:18px;padding:16px;background:#f4f5f7;border-radius:8px;white-space:pre-wrap;font-size:14px;line-height:1.5;">${e(from.message)}</div>
  </div>
</body></html>`;

  return send(env, to, `[Contact] ${from.topic} — ${from.name}`, html, from.email);
}
