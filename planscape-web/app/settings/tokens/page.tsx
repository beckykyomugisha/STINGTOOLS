'use client';

// Access-token management (DEP-9).
//
// The mint/list/revoke API has existed since Phase 201, but there was no screen
// for it: a subscriber had to run curl to get a StingBridge credential. That is
// fine for the platform owner and impossible to ask of a paying customer.
//
// The one rule this screen exists to honour: the plaintext secret is returned
// EXACTLY once, because the server stores only a hash and cannot re-issue it.
// So a freshly minted token is pinned in a panel that does not disappear on
// re-render, with an explicit copy affordance and an explicit dismiss — never
// a toast, never something a stray click can lose.

import { useCallback, useEffect, useState } from 'react';
import { AppShell } from '@/components/AppShell';
import { listAccessTokens, createAccessToken, revokeAccessToken } from '@/lib/data';
import type { AccessToken, MintedAccessToken } from '@/lib/types';

// The server accepts 1..365 only (DefaultPatExpiryDays 90, MaxPatExpiryDays 365
// in AuthController). There is deliberately no "never": a credential that lives
// on disk in CI and on laptops should age out on its own. Offering "Never" here
// would be a lie — omitting the field makes the server apply 90 days, so the
// user would believe they had a permanent token and be locked out in a quarter.
const EXPIRY_CHOICES = [
  { label: '30 days', value: 30 },
  { label: '90 days (default)', value: 90 },
  { label: '180 days', value: 180 },
  { label: '1 year (maximum)', value: 365 },
];

function formatDate(iso?: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString();
}

function isExpired(t: AccessToken): boolean {
  return !!t.expiresAt && new Date(t.expiresAt).getTime() < Date.now();
}

export default function TokensPage() {
  const [tokens, setTokens] = useState<AccessToken[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [expiresInDays, setExpiresInDays] = useState<number>(90);
  const [creating, setCreating] = useState(false);

  // The one-time secret. Held in state until explicitly dismissed.
  const [minted, setMinted] = useState<MintedAccessToken | null>(null);
  const [copied, setCopied] = useState(false);

  const [revoking, setRevoking] = useState<string | null>(null);

  const refresh = useCallback(() => {
    listAccessTokens()
      .then(setTokens)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load tokens'));
  }, []);

  useEffect(refresh, [refresh]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || creating) return;
    setCreating(true);
    setError(null);
    try {
      const result = await createAccessToken({
        name: name.trim(),
        expiresInDays,
      });
      setMinted(result);
      setCopied(false);
      setName('');
      refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the token');
    } finally {
      setCreating(false);
    }
  }

  async function handleRevoke(t: AccessToken) {
    // Revocation is immediate and cannot be undone from here — anything using
    // this credential stops working at once, so make the caller name it.
    if (!window.confirm(`Revoke "${t.name}"? Anything using it will stop working immediately.`)) return;
    setRevoking(t.id);
    setError(null);
    try {
      await revokeAccessToken(t.id);
      // If the caller just revoked the token still pinned in the one-time
      // panel, drop the panel too — otherwise a dead secret stays on screen
      // looking every bit as usable as a live one.
      if (minted?.id === t.id) setMinted(null);
      refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not revoke the token');
    } finally {
      setRevoking(null);
    }
  }

  async function copySecret() {
    if (!minted) return;
    try {
      await navigator.clipboard.writeText(minted.token);
      setCopied(true);
    } catch {
      // Clipboard is blocked on insecure origins and in some browsers. The
      // secret is already on screen and selectable, so this is not fatal —
      // just stop claiming it was copied.
      setCopied(false);
      setError('Could not reach the clipboard — select the token and copy it manually.');
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <h1 className="text-xl font-semibold">Access tokens</h1>
        <p className="mt-1 text-sm text-fg-muted">
          Long-lived credentials for tools that sign in without a browser — StingBridge,
          scripts, CI. Set one as <code className="rounded bg-surface-3 px-1">STING_PLANSCAPE_TOKEN</code>.
        </p>
      </div>

      {error && (
        <p role="alert" className="mb-3 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">
          {error}
        </p>
      )}

      {/* One-time secret. Deliberately loud, deliberately manually dismissed. */}
      {minted && (
        <div className="mb-5 rounded-lg border border-warning bg-warning-subtle p-4">
          <h2 className="font-medium text-warning">Copy this token now</h2>
          <p className="mt-1 text-sm text-warning">
            This is the only time it will be shown. We store a hash, so it cannot be
            recovered — if you lose it, revoke it and create another.
          </p>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <code className="flex-1 break-all rounded border border-warning bg-surface px-3 py-2 font-mono text-sm">
              {minted.token}
            </code>
            <button
              onClick={copySecret}
              className="rounded bg-warning px-3 py-2 text-sm font-medium text-fg-on-accent hover:opacity-90"
            >
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
          <p className="mt-2 text-xs text-warning">
            Listed below as <span className="font-mono">{minted.prefix}</span>
            {minted.expiresAt && <> · expires {formatDate(minted.expiresAt)}</>}
          </p>
          <button
            onClick={() => setMinted(null)}
            className="mt-3 text-sm text-warning underline hover:no-underline"
          >
            I&apos;ve saved it — dismiss
          </button>
        </div>
      )}

      <form onSubmit={handleCreate} className="mb-6 rounded-lg bg-surface p-4 ring-1 ring-border">
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex-1">
            <span className="mb-1 block text-sm font-medium">Name</span>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="StingBridge on the studio workstation"
              required
              maxLength={120}
              className="w-full rounded border border-border-strong px-3 py-2 text-sm"
            />
          </label>
          <label>
            <span className="mb-1 block text-sm font-medium">Expires</span>
            <select
              value={expiresInDays}
              onChange={(e) => setExpiresInDays(Number(e.target.value))}
              className="rounded border border-border-strong px-3 py-2 text-sm"
            >
              {EXPIRY_CHOICES.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </select>
          </label>
          <button
            type="submit"
            disabled={creating || !name.trim()}
            className="rounded bg-accent px-3 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover disabled:opacity-50"
          >
            {creating ? 'Creating…' : 'Create token'}
          </button>
        </div>
        <p className="mt-2 text-xs text-fg-muted">
          Use a name you&apos;ll recognise later — the secret is never shown again, so the
          name is all you really have to tell tokens apart when it comes time to revoke one.
        </p>
      </form>

      {!tokens && !error && <p className="text-fg-subtle">Loading…</p>}
      {tokens && tokens.length === 0 && (
        <p className="text-fg-muted">No active tokens.</p>
      )}

      {tokens && tokens.length > 0 && (
        <div className="overflow-x-auto rounded-lg bg-surface ring-1 ring-border">
          <table className="w-full text-sm">
            <thead className="border-b border-border text-left text-fg-muted">
              <tr>
                <th className="px-4 py-2 font-medium">Name</th>
                {/* A random label, not part of the secret — see AccessToken.prefix.
                    Rendered without a trailing ellipsis so it doesn't read as a
                    truncated token the user could match against their own copy. */}
                <th className="px-4 py-2 font-medium">Label</th>
                <th className="px-4 py-2 font-medium">Created</th>
                <th className="px-4 py-2 font-medium">Last used</th>
                <th className="px-4 py-2 font-medium">Expires</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody>
              {tokens.map((t) => (
                <tr key={t.id} className="border-b border-border last:border-0">
                  <td className="px-4 py-2 font-medium">{t.name}</td>
                  <td className="px-4 py-2 font-mono text-xs text-fg-muted">{t.prefix}</td>
                  <td className="px-4 py-2 text-fg-muted">{formatDate(t.createdAt)}</td>
                  {/* Never used is worth seeing: it usually means a misconfigured client. */}
                  <td className="px-4 py-2 text-fg-muted">
                    {t.lastUsedAt ? formatDate(t.lastUsedAt) : <span className="text-fg-subtle">Never</span>}
                  </td>
                  <td className="px-4 py-2 text-fg-muted">
                    {isExpired(t) ? (
                      <span className="text-danger">Expired {formatDate(t.expiresAt)}</span>
                    ) : (
                      formatDate(t.expiresAt)
                    )}
                  </td>
                  <td className="px-4 py-2 text-right">
                    <button
                      onClick={() => handleRevoke(t)}
                      disabled={revoking === t.id}
                      className="rounded border border-border-strong px-2 py-1 text-xs text-danger transition hover:bg-danger-subtle disabled:opacity-50"
                    >
                      {revoking === t.id ? 'Revoking…' : 'Revoke'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <p className="mt-4 text-xs text-fg-muted">
        Up to 20 active tokens per user. Revoking is a soft delete, so the audit trail
        survives.
      </p>
    </AppShell>
  );
}
