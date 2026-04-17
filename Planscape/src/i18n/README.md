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
