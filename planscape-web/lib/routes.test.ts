import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { PROJECT_NAV } from '@/components/shell/nav';

/**
 * U4 — invariants across the migrated routes.
 *
 * Two classes of bug here are invisible to both tsc and `next build`:
 *   1. A hard-coded palette utility (`bg-white`, `text-slate-500`). It compiles,
 *      it renders, and it is simply the wrong colour in dark mode — a whole
 *      panel stays white while everything around it turns dark.
 *   2. A rail link to a route that does not exist. The rail is data (nav.ts) and
 *      the routes are directories; nothing ties them together, so a 404 only
 *      shows up when a human clicks it. This is not hypothetical — the rail
 *      shipped in U2 linking to `/projects/[id]/issues`, which had no page.
 */

// process.cwd() is the package root under vitest. import.meta.url is NOT a
// file: URL here — the jsdom environment overrides it with the test page's http
// origin, and fileURLToPath then throws "The URL must be of scheme file".
const appDir = join(process.cwd(), 'app');
const componentsDir = join(process.cwd(), 'components');

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const p = join(dir, entry);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (p.endsWith('.tsx')) out.push(p);
  }
  return out;
}

// Everything except the primitives themselves, which legitimately define the
// one place a raw colour may still appear (and currently don't).
const sourceFiles = [...walk(appDir), ...walk(componentsDir)].filter((p) => !p.includes(`ui${'\\'}`) && !p.includes('ui/'));

const BANNED =
  /\b(?:bg|text|border|ring|divide|from|to|via)-(?:slate|gray|zinc|neutral|stone|blue|green|red|amber|orange|yellow|purple|pink)-\d{2,3}\b|\bbg-white\b/;

describe('route migration (U4)', () => {
  it('uses design tokens everywhere, never a hard-coded palette colour', () => {
    const offenders: string[] = [];
    for (const file of sourceFiles) {
      const src = readFileSync(file, 'utf8');
      for (const [i, line] of src.split('\n').entries()) {
        const m = line.match(BANNED);
        if (m) offenders.push(`${file.split(/[\\/]/).slice(-3).join('/')}:${i + 1} → ${m[0]}`);
      }
    }
    expect(offenders, `dark mode cannot override these:\n${offenders.join('\n')}`).toEqual([]);
  });

  it('has a real page for every project section the rail links to', () => {
    const missing = PROJECT_NAV.filter((item) => {
      const dir = item.segment ? join(appDir, 'projects', '[id]', item.segment) : join(appDir, 'projects', '[id]');
      try {
        return !statSync(join(dir, 'page.tsx')).isFile();
      } catch {
        return true;
      }
    }).map((i) => i.segment || '(overview)');

    expect(missing, `rail links to routes with no page.tsx: ${missing.join(', ')}`).toEqual([]);
  });

  it('renders every page inside the AppShell, so no route escapes the chrome', () => {
    // Deliberately outside the shell: /login and /handoff exist for users with
    // no session yet, the error/not-found boundaries must render even when the
    // shell itself is what broke, and /dashboard is a bare redirect stub kept
    // for old bookmarks (it is not in the rail — see GLOBAL_NAV).
    const outside = ['login', 'handoff', 'dashboard', 'error.tsx', 'not-found.tsx', join('app', 'page.tsx')];
    const escaped = walk(appDir)
      .filter((p) => p.endsWith('page.tsx'))
      .filter((p) => !outside.some((o) => p.includes(o)))
      .filter((p) => !readFileSync(p, 'utf8').includes('AppShell'))
      .map((p) => p.split(/[\\/]/).slice(-3).join('/'));

    expect(escaped, `pages not wrapped in AppShell: ${escaped.join(', ')}`).toEqual([]);
  });
});
