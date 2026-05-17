// Placeholder tab screen for the My Actions tab.
// The tab bar points this entry to /inbox via href so the full inbox
// Stack navigator (app/inbox/) is used. This file must exist so Expo
// Router's file-based routing resolves the Tabs.Screen name="myactions".
// Users never see this screen — the href redirect fires immediately.

import { Redirect } from 'expo-router';

export default function MyActionsTabPlaceholder() {
  return <Redirect href="/inbox" />;
}
