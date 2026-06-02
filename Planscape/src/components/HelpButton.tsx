// S4.6 — in-app help-widget launcher.
//
// Opens the docs site (canonical reference) in the system browser, with a
// secondary 'Email support' fallback. We deliberately don't embed a chat
// SDK in the React Native app — Crisp's RN package adds 6 MB to the
// bundle for a feature that's used a few times per coordinator per month.
// Instead we open a WebView pointing at the public docs site, and a
// chat tile pointing to mailto: hello@planscape.app. When chat volume
// justifies a real SDK, we'll swap it in here without changing callers.

import { Linking } from 'react-native';
import { useTenantStore } from '@/stores/tenantStore';

export const HELP_DOCS_URL = 'https://docs.planscape.app';
export const HELP_SUPPORT_EMAIL = 'hello@planscape.app';

/** Open the docs in the system browser. */
export function openHelpDocs(): Promise<unknown> {
  return Linking.openURL(HELP_DOCS_URL);
}

/** Compose a support email pre-filled with tenant + device info. */
export async function openSupportEmail(subject: string = 'Planscape support request'): Promise<unknown> {
  const ts = useTenantStore.getState();
  const m = ts.memberships.find((x) => x.tenantId === ts.currentTenantId)
    ?? ts.memberships.find((x) => x.isActiveTenant);
  const tenant = m ? { name: m.tenantName, slug: m.tenantSlug } : null;
  const body = [
    'Hi Planscape team,',
    '',
    '(describe your issue here)',
    '',
    '— —',
    `Tenant: ${tenant?.name ?? '(unknown)'}`,
    `Slug:   ${tenant?.slug ?? '(unknown)'}`,
    `Sent from the mobile app at ${new Date().toISOString()}`,
  ].join('\n');
  const url = `mailto:${HELP_SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  return Linking.openURL(url);
}
