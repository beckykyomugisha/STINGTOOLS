'use client';

import { Suspense, useState, type FormEvent } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useAuth } from '@/lib/auth';

/**
 * Only ever return a same-origin path. `next` reaches us from the address bar,
 * so an absolute URL here would turn the sign-in form into an open redirect.
 */
function safeNext(raw: string | null): string {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//')) return '/projects';
  return raw;
}

/**
 * useSearchParams opts the subtree out of static prerendering, and `next build`
 * fails outright if it isn't inside a Suspense boundary — hence the split.
 */
export default function LoginPage() {
  return (
    <Suspense fallback={<main className="grid min-h-screen place-items-center p-4" />}>
      <LoginForm />
    </Suspense>
  );
}

function LoginForm() {
  const { login } = useAuth();
  const router = useRouter();
  const next = safeNext(useSearchParams()?.get('next') ?? null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(email.trim(), password);
      router.replace(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="grid min-h-screen place-items-center p-4">
      <form onSubmit={onSubmit} className="w-full max-w-sm rounded-xl bg-surface p-8 shadow-sm ring-1 ring-border">
        <h1 className="mb-1 text-2xl font-semibold">Planscape</h1>
        <p className="mb-6 text-sm text-fg-muted">Sign in to coordinate online.</p>

        <label className="mb-3 block">
          <span className="mb-1 block text-sm font-medium">Email</span>
          <input
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
            className="w-full rounded border border-border-strong px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        <label className="mb-4 block">
          <span className="mb-1 block text-sm font-medium">Password</span>
          <input
            type="password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            className="w-full rounded border border-border-strong px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        {error && <p className="mb-4 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}

        <button
          type="submit"
          disabled={busy}
          className="w-full rounded bg-accent px-4 py-2 font-medium text-fg-on-accent transition hover:bg-accent-hover disabled:opacity-60"
        >
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </main>
  );
}
