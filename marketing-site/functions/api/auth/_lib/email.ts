// Transactional email via Resend. Plain inline HTML — no template engine.
// If RESEND_API_KEY is unset (e.g. local/preview), sends are skipped with a
// console.error so the auth flow still works without blowing up.

import type { Env } from "./types";

const DEFAULT_FROM = "Planscape <noreply@planscape.build>";

function appOrigin(env: Env): string {
  return env.APP_ORIGIN || "https://planscape.build";
}

async function send(
  env: Env,
  to: string,
  subject: string,
  html: string
): Promise<void> {
  if (!env.RESEND_API_KEY) {
    // Non-fatal: the surrounding flow (signup/login) must still succeed.
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
        from: env.EMAIL_FROM || DEFAULT_FROM,
        to: [to],
        subject,
        html,
      }),
    });
    if (!res.ok) {
      console.error(`Resend send failed (${res.status}) for "${subject}"`);
    }
  } catch (e) {
    // Never let an email failure break the request.
    console.error("Resend request threw", e);
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
  const link = `${appOrigin(env)}/api/auth/verify?token=${encodeURIComponent(token)}`;
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
