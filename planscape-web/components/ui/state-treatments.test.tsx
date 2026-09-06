/**
 * The four answers, rendered — LOADING / EMPTY / ERROR / FORBIDDEN.
 *
 * #558 added FORBIDDEN as the app's fourth answer. #643 wired it through the
 * web surfaces. Neither had been SEEN. Component rendering was available and
 * already in use elsewhere (`Menu.test.tsx`, `DataGrid.test.tsx`), but every
 * test #643 itself shipped was a `lib/*` logic test — `api.test.ts` and
 * `capabilities.test.ts`. Those prove `isForbidden()` returns true for a 403.
 * They do not prove a user can tell a refusal from a failure, which is the
 * entire claim of the change.
 *
 * The specific risk this file exists to rule out: four states that all render
 * the same. A forbidden state that inherits the error treatment is not a fourth
 * answer, it is an error message with different words — it still tells the user
 * the system is broken and still sends them to IT, when what they need is to
 * ask whoever holds the role.
 *
 * So these assertions are about DISTINGUISHABILITY, not prettiness: each state
 * must differ from the other three in the channels a user actually perceives —
 * the accessibility role, the colour family, and the presence of an icon —
 * and every assertion below is made against the real rendered DOM.
 */

import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import { EmptyState, ErrorNote, ForbiddenNote, ForbiddenPanel, LoadingBlock } from './primitives';

afterEach(cleanup);

/** The outermost element a state primitive renders. */
function rootOf(container: HTMLElement): HTMLElement {
  return container.firstElementChild as HTMLElement;
}

/** Colour family carried by the Tailwind classes actually applied. */
function toneOf(el: HTMLElement): 'danger' | 'warning' | 'neutral' {
  const cls = el.className;
  if (/\bborder-danger|bg-danger|text-danger\b/.test(cls)) return 'danger';
  if (/\bborder-warning|bg-warning|text-warning\b/.test(cls)) return 'warning';
  return 'neutral';
}

describe('the four states are actually four', () => {
  it('renders each state and reports what it looks like', () => {
    const seen: Record<string, { role: string | null; busy: string | null; tone: string; lock: boolean; text: string }> = {};

    const capture = (name: string, ui: React.ReactElement) => {
      const { container } = render(ui);
      const root = rootOf(container);
      seen[name] = {
        role: root.getAttribute('role'),
        busy: root.getAttribute('aria-busy'),
        tone: toneOf(root),
        lock: (root.textContent ?? '').includes('🔒'),
        text: (root.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 70),
      };
      cleanup();
    };

    capture('LOADING', <LoadingBlock rows={3} />);
    capture('EMPTY', <EmptyState title="No documents yet" description="Upload one to get started." />);
    capture('ERROR', <ErrorNote>The server did not respond.</ErrorNote>);
    capture('FORBIDDEN', <ForbiddenNote>You need the Coordinator role to publish.</ForbiddenNote>);

    // Printed so the four states can be READ, not just asserted. This is the
    // "capture" half of the verification.
    // eslint-disable-next-line no-console
    console.log('\n  ── the four states, as rendered ──');
    for (const [name, s] of Object.entries(seen)) {
      // eslint-disable-next-line no-console
      console.log(
        `  ${name.padEnd(10)} role=${String(s.role).padEnd(7)} busy=${String(s.busy).padEnd(5)} ` +
        `tone=${s.tone.padEnd(8)} lock=${s.lock ? 'yes' : 'no '}  "${s.text}"`,
      );
    }

    // ── ERROR and FORBIDDEN must not look the same ──────────────────────────
    // This is the assertion the whole file is for.
    expect(seen.ERROR.tone).toBe('danger');
    expect(seen.FORBIDDEN.tone).toBe('warning');
    expect(seen.FORBIDDEN.tone).not.toBe(seen.ERROR.tone);

    // Different a11y roles: a failure is an alert (interrupt me), a refusal is
    // a status (tell me). A screen-reader user gets a different signal too, not
    // just a different colour — colour alone would fail anyone who cannot see it.
    expect(seen.ERROR.role).toBe('alert');
    expect(seen.FORBIDDEN.role).toBe('status');

    // A third, non-colour, non-role channel: the lock glyph. Present on the
    // refusal only.
    expect(seen.FORBIDDEN.lock).toBe(true);
    expect(seen.ERROR.lock).toBe(false);

    // ── LOADING vs FORBIDDEN share role="status" ────────────────────────────
    // They are separated by aria-busy. Pinned deliberately: if aria-busy were
    // dropped from LoadingBlock these two would be indistinguishable to a
    // screen reader, and that regression is otherwise invisible.
    expect(seen.LOADING.role).toBe('status');
    expect(seen.FORBIDDEN.role).toBe('status');
    expect(seen.LOADING.busy).toBe('true');
    expect(seen.FORBIDDEN.busy).toBeNull();

    // ── EMPTY is neither an error nor a refusal ─────────────────────────────
    // An empty list is a true answer. It must not borrow either alarm colour —
    // this is the "empty result standing in for a failure" trap, in CSS.
    expect(seen.EMPTY.tone).toBe('neutral');
    expect(seen.EMPTY.role).toBeNull();

    // ── All four differ pairwise on (role, aria-busy, tone, lock) ───────────
    const fingerprints = Object.entries(seen).map(
      ([n, s]) => [n, `${s.role}|${s.busy}|${s.tone}|${s.lock}`] as const,
    );
    const unique = new Set(fingerprints.map(([, f]) => f));
    expect(unique.size, `states collide: ${fingerprints.map(([n, f]) => `${n}=${f}`).join('  ')}`).toBe(4);
  });

  // ── The states carry real content, not placeholders ───────────────────────

  // NB: plain DOM assertions rather than jest-dom matchers. This repo has
  // @testing-library/jest-dom installed but `vitest.config.ts` declares no
  // `setupFiles`, so its matchers are not registered and `toBeInTheDocument()`
  // fails with "Invalid Chai property" — further evidence that the component
  // testing path was set up and never walked. Keeping these assertions
  // self-contained means this file does not depend on a shared-config change.

  it('FORBIDDEN shows the reason it was given, not a status code', () => {
    render(<ForbiddenNote>You need the Coordinator role to publish this document.</ForbiddenNote>);
    const text = screen.getByRole('status').textContent ?? '';
    expect(text).toContain('You need the Coordinator role to publish this document.');
    // The literal strings users were shown before #643.
    expect(text).not.toMatch(/HTTP\s*\d{3}/);
    expect(text).not.toMatch(/Request failed/i);
  });

  it('LOADING announces itself to a screen reader', () => {
    render(<LoadingBlock rows={2} label="Loading documents" />);
    const el = screen.getByRole('status');
    expect(el.getAttribute('aria-live')).toBe('polite');
    expect(el.textContent ?? '').toContain('Loading documents');
  });

  it('EMPTY states the answer rather than implying a failure', () => {
    const { container } = render(
      <EmptyState title="No documents yet" description="Upload one to get started." />,
    );
    expect(screen.getByText('No documents yet')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
    // and it does not borrow the failure colour
    expect(toneOf(rootOf(container))).toBe('neutral');
  });

  it('ForbiddenPanel — the whole-pane refusal — is also a status, not an alert', () => {
    render(<ForbiddenPanel message="You cannot see this project's members." hint="Ask a project Admin." />);
    const text = screen.getByRole('status').textContent ?? '';
    expect(text).toContain("You cannot see this project's members.");
    expect(text).toContain('Ask a project Admin.');
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
