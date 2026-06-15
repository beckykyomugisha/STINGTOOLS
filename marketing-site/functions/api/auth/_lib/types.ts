// Shared types for the Planscape auth core (B1).

export interface Env {
  // Reused from the waitlist Function — same D1 database.
  WAITLIST_DB: D1Database;
  // HS256 signing secret (32+ random bytes). Set in CF dashboard, never committed.
  JWT_SECRET: string;
  // Resend API key for transactional email. Set in CF dashboard.
  RESEND_API_KEY?: string;
  // Public origin of the app, used to build email links. e.g. https://planscape.build
  APP_ORIGIN?: string;
  // Optional override for the From: address on transactional email.
  EMAIL_FROM?: string;
  // Billing (B3a — Stripe). Both set as encrypted secrets in the CF dashboard.
  STRIPE_SECRET_KEY?: string;
  STRIPE_WEBHOOK_SECRET?: string;
}

// Row shapes as stored in D1.
export interface TenantRow {
  id: string;
  name: string;
  slug: string;
  country: string | null;
  currency: string;
  plan_product: string | null;
  plan_tier: string | null;
  subscription_status: string;
  trial_started_at: string;
  trial_ends_at: string;
  cap_exceeded_since: string | null;
  stripe_customer_id: string | null; // (B3a) Stripe customer id, null pre-checkout
  created_at: string;
  updated_at: string | null;
}

export interface UserRow {
  id: string;
  tenant_id: string;
  email: string;
  password_hash: string;
  first_name: string;
  last_name: string;
  role: string;
  email_verified_at: string | null;
  email_verify_token: string | null;
  email_verify_expires_at: string | null;
  password_reset_token_hash: string | null;
  password_reset_expires_at: string | null;
  last_login_at: string | null;
  deleted_at: string | null;
  created_at: string;
  updated_at: string | null;
}

export interface InvitationRow {
  id: string;
  tenant_id: string;
  email: string;
  role: string;
  token_hash: string;
  invited_by_user_id: string;
  expires_at: string;
  accepted_at: string | null;
  declined_at: string | null;
  created_at: string;
}

export interface SessionRow {
  id: string;
  user_id: string;
  refresh_token_hash: string;
  user_agent: string | null;
  ip: string | null;
  created_at: string;
  last_used_at: string | null;
  expires_at: string;
  revoked_at: string | null;
  revoked_reason: string | null;
}

// JWT claims (HS256 access token).
export interface JwtClaims {
  iss: string;
  sub: string; // user id
  tid: string; // tenant id
  role: string;
  ev: boolean; // email verified
  ps: string; // subscription_status (trial | active | past_due | read_only | cancelled)
  pt: string | null; // plan_tier (solo | studio | ...)
  pp: string | null; // plan_product (sting-tools | planscape)
  iat: number;
  exp: number;
}

// The shape returned to clients (no secrets).
export interface PublicUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  emailVerified: boolean;
}

export interface PublicTenant {
  id: string;
  name: string;
  slug: string;
  trialEndsAt: string;
  subscriptionStatus: string;
}
