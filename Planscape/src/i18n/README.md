# Mobile i18n (FLEX-15)

Tiny, zero-dependency translation helper. Swap for `i18next` later if ICU
message-format / pluralisation / namespaces are needed — the public API
(`t`, `useT`, `setLanguage`, `getLanguage`) matches the common shape.

## Usage

```tsx
import { t, useT, setLanguage, initI18n } from "@/i18n";

// App bootstrap (once, before the first render)
await initI18n();

// Plain call — safe anywhere, including outside React.
label = t("common.save");
badge = t("issue.days_open", { n: 3 });

// Reactive hook — component re-renders when setLanguage() is called.
const translate = useT();
const title = translate("tabs.issues");

// Let the user change language from a settings screen.
await setLanguage("de");
```

## Adding a new language

1. Copy `locales/en.json` to `locales/<code>.json` (e.g. `fr.json`).
2. Translate the string values. Leave untranslated keys out — the helper
   falls back to English automatically.
3. Add the import in `src/i18n/index.ts`:
   ```ts
   import fr from "./locales/fr.json";
   const BUNDLES: Record<string, Bundle> = { en, de, es, fr };
   ```
4. (Optional) Add a matching `I18n/fr.json` on the server so server-generated
   strings (emails, push notifications, validation errors) also translate.

## Server-side companion

- `GET /api/i18n` — list of supported languages + resolved language for the caller.
- `GET /api/i18n/{lang}` — full translation bundle (mobile prefetch).
- Pass the current language back to the server via the `X-Language` request header;
  `LocaleMiddleware` honours it before falling back to `Accept-Language` or
  the tenant's default language.

## QA helper

`lint.mjs` compares each locale against `en.json` and flags missing /
empty / placeholder-mismatched strings:

```bash
cd Planscape/src/i18n
node lint.mjs              # human-readable
node lint.mjs --json       # CI output
node lint.mjs --fix-empty  # copy en.json values into missing/empty
                           # slots, prefixed with «EN» so a native-
                           # speaker reviewer can find them at a glance
```

Exit code 0 = clean, 1 = issues, 2 = usage error. Wire into CI
(GitHub Actions step) so a PR that adds an English string but
forgets the other locales fails the build.

### Native-speaker review (sw.json)

Swahili strings shipped in `sw.json` were drafted from working
glossaries and lint-verified for structure (placeholders match,
no missing keys). They have **not** been reviewed by a native
speaker yet.

Recommended review pass:

1. A native Swahili speaker who's also a BIM coordinator opens
   the mobile app with `Settings → Language → Swahili`.
2. They click through every screen and note any string that reads
   awkwardly or is technically wrong. ISO 19650 has English-loan
   technical vocabulary in Swahili construction practice — keep
   those (RFI, NCR, BIM) untranslated where idiomatic.
3. Edits land via PR against `sw.json`. The lint passes
   automatically; placeholders must stay `{name}`-style so the
   translation engine can substitute.

Allocate ~2 hours total for the first pass. Re-review every time
en.json grows by >10 strings.
