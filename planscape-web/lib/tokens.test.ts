import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * U1 — the design-token contract.
 *
 * `tailwind.config.ts` names tokens (`bg-surface-2`) and `app/tokens.css` defines
 * their values. Nothing enforces that those two agree: a typo'd or deleted custom
 * property produces `background-color: hsl( / 1)`, which is not a build error and
 * not a type error — it silently renders transparent. These tests are the only
 * thing standing between that and a shipped invisible panel.
 */

const read = (p: string) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), 'utf8');
const tokensCss = read('../app/tokens.css');
const tailwindConfig = read('../tailwind.config.ts');
const globalsCss = read('../app/globals.css');

/** Custom properties Tailwind expects, scraped from `hsl(var(--x) / <alpha-value>)` and `var(--x)`. */
function referencedVars(src: string): string[] {
  return [...new Set([...src.matchAll(/var\((--[a-z0-9-]+)\)/g)].map((m) => m[1]))];
}
/** Custom properties actually declared, per selector block. */
function declaredVars(css: string, selector: string): string[] {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) return [];
  const end = css.indexOf('\n}', start);
  const block = css.slice(start, end);
  return [...new Set([...block.matchAll(/^\s*(--[a-z0-9-]+)\s*:/gm)].map((m) => m[1]))];
}

const light = declaredVars(tokensCss, ':root');
const dark = declaredVars(tokensCss, '.dark');

describe('design tokens', () => {
  it('defines every custom property the Tailwind theme references', () => {
    const missing = referencedVars(tailwindConfig).filter((v) => !light.includes(v));
    expect(missing, `tailwind.config.ts references undefined token(s): ${missing.join(', ')}`).toEqual([]);
  });

  it('defines every custom property globals.css consumes', () => {
    const missing = referencedVars(globalsCss).filter((v) => !light.includes(v));
    expect(missing, `globals.css references undefined token(s): ${missing.join(', ')}`).toEqual([]);
  });

  it('gives dark mode a value for every COLOUR token, so nothing falls back to a light value', () => {
    // Geometry (--rail-w, --topbar-h) is intentionally theme-independent and
    // inherits from :root; colours and shadows are not.
    const themed = light.filter((v) => !['--rail-w', '--rail-w-collapsed', '--topbar-h'].includes(v));
    const missing = themed.filter((v) => !dark.includes(v));
    expect(missing, `.dark is missing: ${missing.join(', ')}`).toEqual([]);
  });

  it('declares no token in .dark that does not exist in :root', () => {
    // A dark-only token is a token nobody can use in light mode — almost always a typo.
    const orphans = dark.filter((v) => !light.includes(v));
    expect(orphans, `.dark declares unknown token(s): ${orphans.join(', ')}`).toEqual([]);
  });

  it('stores colours as bare HSL triples so Tailwind can apply an alpha channel', () => {
    // `--accent: hsl(214 90% 44%)` would make `bg-accent/10` emit
    // `hsl(hsl(...) / 0.1)` — invalid, and again silently transparent.
    const colourish = /^\s*(--(?:bg|surface|border|fg|accent|success|warning|danger|info|ring)[a-z0-9-]*)\s*:\s*(.+?);/gm;
    const bad: string[] = [];
    for (const m of tokensCss.matchAll(colourish)) {
      if (/^hsl\(|^rgb|^#/.test(m[2].trim())) bad.push(`${m[1]}: ${m[2]}`);
    }
    expect(bad, `wrap-free HSL triples required: ${bad.join(' | ')}`).toEqual([]);
  });

  it('keeps tokens.css free of @layer, which breaks when imported above @tailwind base', () => {
    // The exact error this cost once: "@layer base is used but no matching
    // @tailwind base directive is present". Keep the values layer-free.
    expect(tokensCss).not.toMatch(/@layer/);
  });

  it('imports tokens before the Tailwind directives', () => {
    // CSS requires @import to precede other rules; if the directives land first
    // the import is dropped and every token resolves to nothing.
    // Comments are stripped first — globals.css *explains* this ordering in prose,
    // and a naive indexOf finds the explanation before the real directive.
    const code = globalsCss.replace(/\/\*[\s\S]*?\*\//g, '');
    expect(code.indexOf("@import './tokens.css'")).toBeLessThan(code.indexOf('@tailwind base'));
  });

  it('enables class-based dark mode, not media-query dark mode', () => {
    // Media-query dark mode would make the theme toggle in U2 a no-op.
    expect(tailwindConfig).toMatch(/darkMode:\s*'class'/);
  });
});
