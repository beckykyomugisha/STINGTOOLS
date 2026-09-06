'use client';

// Landing point for the accept-invite / password-reset handoff.
//
// The set-password page (reset-password.html) is served by the API, on a
// DIFFERENT origin to this app — so it cannot write this app's localStorage and
// cannot sign the invitee in itself. It used to just navigate here-ish and hope,
// which left a freshly-activated invitee staring at a blank page or a sign-in
// form seconds after choosing a password. Now /api/auth/reset-password returns a
// real session and that page forwards it to this route.
//
// The session arrives in the URL *fragment*, never the query string: fragments
// are not sent to servers, don't reach access logs, and don't leak via Referer.
// It is stripped from the address bar and from history before we navigate on —
// the same discipline /handoff uses for its ticket.

import { useEffect, useRef, useState } from 'react';
import { setToken } from '@/lib/api';

export default function AcceptPage() {
  const [message, setMessage] = useState('Signing you in…');
  const ran = useRef(false);

  useEffect(() => {
    // StrictMode double-mounts effects in dev; the fragment is consumed and
    // erased on the first pass, so guard against the second.
    if (ran.current) return;
    ran.current = true;

    const frag = new URLSearchParams((window.location.hash || '').replace(/^#/, ''));
    const token = frag.get('token');
    // ?project= stays in the query: it is not a secret and being able to see
    // which project you were invited to is useful if anything goes wrong.
    const project = new URLSearchParams(window.location.search).get('project');

    // Erase the credential from the address bar and history immediately —
    // before any navigation, so no entry ever carries it.
    window.history.replaceState(null, '', project ? `/accept?project=${encodeURIComponent(project)}` : '/accept');

    if (!token) {
      // No session to install (a stale bookmark, or a link opened twice).
      // Sign-in still works — the password was set — so send them there.
      setMessage('This link has already been used. Please sign in.');
      setTimeout(() => window.location.replace('/login'), 2000);
      return;
    }

    setToken(token);

    // FULL navigation, not a router push: AuthProvider reads the token once on
    // mount, so a soft navigation would land on the destination with stale
    // in-memory auth and bounce straight back to /login.
    window.location.replace(project ? `/projects/${encodeURIComponent(project)}` : '/projects');
  }, []);

  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="text-center">
        <p className="text-lg font-medium">{message}</p>
        <p className="mt-2 text-sm text-fg-muted">Planscape cloud</p>
      </div>
    </main>
  );
}
