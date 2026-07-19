'use client';

// Landing point for the planscape.build → cloud handoff
// (docs/PLANSCAPE_IDENTITY_HANDOFF.md). The marketing site mints a short-lived
// single-use ticket and redirects here; we exchange it for a normal session and
// land the user on /projects.
//
// The exchange result is stored exactly as a password login would store it
// (setToken → localStorage), and we then do a FULL navigation rather than a
// router push: AuthProvider reads the token once on mount, so a soft navigation
// would leave the in-memory auth state stale.
//
// On any failure the user goes to /login with a plain message. The ticket is
// never echoed back into the page — it is already in the URL bar, which is
// exposure enough; we also strip it from history before leaving.

import { useEffect, useRef, useState } from 'react';
import { API_BASE, setToken } from '@/lib/api';

export default function HandoffPage() {
  const [message, setMessage] = useState('Signing you in…');
  const ran = useRef(false);

  useEffect(() => {
    // React StrictMode mounts effects twice in dev. The ticket is single-use on
    // the server, so a second POST would burn it and bounce a legitimate user.
    if (ran.current) return;
    ran.current = true;

    const ticket = new URLSearchParams(window.location.search).get('ticket');
    if (!ticket) {
      window.location.replace('/login');
      return;
    }
    // Take the ticket out of the address bar / history immediately.
    window.history.replaceState(null, '', '/handoff');

    (async () => {
      try {
        const res = await fetch(`${API_BASE}/api/auth/handoff/exchange`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ticket }),
        });
        if (!res.ok) {
          let msg = 'Sign-in link expired — please try again from planscape.build.';
          try {
            const body = await res.json();
            msg = body.message || body.error || msg;
          } catch {
            /* keep generic */
          }
          setMessage(msg);
          setTimeout(() => window.location.replace('/login'), 2500);
          return;
        }
        const data = (await res.json()) as { accessToken?: string; token?: string };
        const token = data.accessToken || data.token;
        if (!token) throw new Error('no token');
        setToken(token);
        window.location.replace('/projects');
      } catch {
        setMessage('Could not reach Planscape — please try again from planscape.build.');
        setTimeout(() => window.location.replace('/login'), 2500);
      }
    })();
  }, []);

  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="text-center">
        <p className="text-lg font-medium">{message}</p>
        <p className="mt-2 text-sm text-neutral-500">Planscape cloud</p>
      </div>
    </main>
  );
}
